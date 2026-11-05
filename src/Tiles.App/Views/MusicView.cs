using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tiles.Core;
using Windows.Media.Control;

namespace Tiles.App;

/// <summary>
/// Музыка: обложка, трек, альбом, кнопки, прогресс. Берём у Windows (как во всплывашке громкости),
/// поэтому работает с любым плеером. Без плеера просит спрятать окно.
/// Длинный — обложка слева; квадрат — обложка с названием сверху; высокий — большая обложка над всем.
/// </summary>
internal sealed class MusicView : WidgetView
{
    private static readonly Brush BarBack = new SolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
    private static readonly Brush ButtonHover = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

    private readonly Border _cover;
    private readonly Image _backdrop;
    private readonly TextBlock _title, _artist, _album, _time;
    private readonly Border _bar, _barFill;
    private readonly Border _playButton;
    private readonly StackPanel _buttonRow;
    private readonly Grid _bottom = new();
    private readonly List<Border> _buttons = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _playing;
    private TimeSpan _pos, _len;
    private DateTimeOffset _posAt;
    private string _coverKey = "";
    // плеер при смене трека на миг пропадает у Windows — прячемся, только если его нет пару секунд
    private readonly DispatcherTimer _hideLater = new() { Interval = TimeSpan.FromSeconds(2.5) };

    public override IReadOnlyList<(string, Func<bool>, Action<bool>)> Options => new[]
    {
        Opt(Loc.T("Обложка"), "cover"),
        Opt(Loc.T("Фон в цвет обложки"), "tint"),
        Opt(Loc.T("Полоска прогресса и время"), "bar"),
        Opt(Loc.T("Альбом"), "album", false),
    };

    public override void ApplyOptions()
    {
        _cover.Visibility = Flag("cover") ? Visibility.Visible : Visibility.Collapsed;
        _backdrop.Visibility = Flag("tint") ? Visibility.Visible : Visibility.Collapsed;
        var bar = Flag("bar") ? Visibility.Visible : Visibility.Collapsed;
        _bar.Visibility = bar;
        _time.Visibility = bar;
        ShowAlbum();
        Relayout();
    }

    private void ShowAlbum() => _album.Visibility = Flag("album", false) && _album.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public MusicView(WidgetInfo info) : base(info)
    {
        // фон — та же обложка, сильно размытая: стекло окрашивается в цвет трека
        // размытие считаем один раз на трек (Blurred), а не живым эффектом: иначе оно пересчитывается каждую секунду
        _backdrop = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.42 };
        _cover = new Border
        {
            Width = 84, Height = 84, CornerRadius = new CornerRadius(14), Margin = new Thickness(12),
            Background = new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF)),
            Child = Glyphs.Make(Glyphs.Note, 36, 1.9), VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 },
        };
        // обложка с круглыми углами в любом размере
        _cover.SizeChanged += (_, _) => { if (_cover.Child is Image) _cover.Clip = new RectangleGeometry(new Rect(_cover.RenderSize), 14, 14); };
        _title = Text(17, FontWeights.SemiBold);
        _artist = Text(13.5, FontWeights.Medium, Soft);
        _artist.Margin = new Thickness(0, 1, 0, 0);
        _album = Text(12, FontWeights.Medium, Soft);
        _album.Margin = new Thickness(0, 1, 0, 0);
        _barFill = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        _bar = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = BarBack, Margin = new Thickness(0, 10, 0, 4), Child = _barFill };
        _bar.SizeChanged += (_, _) => ShowProgress();
        _time = Text(12, FontWeights.SemiBold, Soft);
        _time.VerticalAlignment = VerticalAlignment.Center;

        _playButton = Button(Glyphs.Play, 21, () => Command(s => s.TryTogglePlayPauseAsync()));
        _buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Button(Glyphs.Prev, 18, () => Command(s => s.TrySkipPreviousAsync())),
                _playButton,
                Button(Glyphs.Next, 18, () => Command(s => s.TrySkipNextAsync())),
            },
        };
        ClipToBounds = true;
        SizeChanged += (_, _) =>
        {
            double r = Math.Max(0, Math.Min(App.Current.Config.Defaults.Corner, 40) - 1);
            Clip = new RectangleGeometry(new Rect(RenderSize), r, r);
        };

        _tick.Tick += (_, _) => ShowProgress();
        _title.Text = Loc.T("Ничего не играет");
        _hideLater.Tick += (_, _) => { _hideLater.Stop(); if (_session == null) RequestVisible(false); };
        ApplyOptions();
    }

    protected override void OnShape(WidgetShape s) => Relayout();

    /// <summary>Раскладка под форму окна.</summary>
    private void Relayout()
    {
        foreach (var e in new FrameworkElement[] { _cover, _title, _artist, _album, _bar, _bottom, _time, _buttonRow }) (e.Parent as Panel)?.Children.Remove(e);
        Children.Clear();
        Children.Add(_backdrop);
        bool tall = Shape == WidgetShape.Tall;
        foreach (var t in new[] { _title, _artist, _album }) t.TextAlignment = tall ? TextAlignment.Center : TextAlignment.Left;
        _bottom.Children.Add(_time);
        _bottom.Children.Add(_buttonRow);
        _time.HorizontalAlignment = tall ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        _time.VerticalAlignment = tall ? VerticalAlignment.Top : VerticalAlignment.Center;
        _buttonRow.HorizontalAlignment = tall ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        _buttonRow.Margin = tall ? new Thickness(0, 18, 0, 0) : new Thickness(0, 0, -6, 0);
        _cover.HorizontalAlignment = tall ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        switch (Shape)
        {
            case WidgetShape.Wide:
            {
                _cover.Width = _cover.Height = 84; _cover.Margin = new Thickness(12);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Children = { _title, _artist, _album, _bar, _bottom } };
                var row = new DockPanel();
                DockPanel.SetDock(_cover, Dock.Left);
                row.Children.Add(_cover); row.Children.Add(text);
                Children.Add(row);
                break;
            }
            case WidgetShape.Square:
            {
                _cover.Width = _cover.Height = 78; _cover.Margin = new Thickness(0, 0, 12, 0);
                var head = new DockPanel();
                DockPanel.SetDock(_cover, Dock.Left);
                head.Children.Add(_cover);
                head.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _title, _artist, _album } });
                Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14), Children = { head, _bar, _bottom } });
                break;
            }
            default:
            {
                _cover.Width = _cover.Height = 150; _cover.Margin = new Thickness(0, 0, 0, 12);
                Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14), Children = { _cover, _title, _artist, _album, _bar, _bottom } });
                break;
            }
        }
    }

    public override string Title => Loc.T("Музыка");
    public override string Description => Loc.T("Обложка, трек, альбом и кнопки. Вытяни вверх — большая обложка. Прячется, когда плеер закрыт.");
    public override Size BaseSize => BaseFor(WidgetShape.Wide);

    public override Size BaseFor(WidgetShape s)
    {
        double album = Flag("album", false) ? 18 : 0;
        return s switch
        {
            WidgetShape.Wide => new(396, 124 + album),
            WidgetShape.Square => new(250, 186 + album),
            _ => new(200, 330 + album),
        };
    }

    public override bool InitiallyVisible => false;

    public override bool OwnsMouse(DependencyObject? hit) => _buttons.Any(b => IsInside(hit, b));

    private static bool IsInside(DependencyObject? d, DependencyObject parent)
    {
        while (d != null) { if (d == parent) return true; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d); }
        return false;
    }

    private Border Button(string glyph, double size, Action click)
    {
        var b = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = Brushes.Transparent,
            Child = Glyphs.Make(glyph, size, 2.0), Cursor = Cursors.Hand,
        };
        b.MouseEnter += (_, _) => b.Background = ButtonHover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        _buttons.Add(b);
        return b;
    }

    private async void Command(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> cmd)
    {
        try { if (_session != null) await cmd(_session); }
        catch (Exception ex) { App.Current.Log("music command failed: " + ex.Message); }
    }

    protected override async void OnStart()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => Dispatcher.BeginInvoke(Bind);
            Bind();
        }
        catch (Exception ex) { App.Current.Log("music unavailable: " + ex.Message); }
    }

    protected override void OnStop()
    {
        _tick.Stop();
        Unbind();
        _manager = null;
    }

    public override void Demo()
    {
        _title.Text = "Bohemian Rhapsody";
        _artist.Text = "Queen";
        _album.Text = "A Night at the Opera";
        ShowAlbum();
        _playing = true; _pos = TimeSpan.FromSeconds(78); _len = TimeSpan.FromSeconds(354); _posAt = DateTimeOffset.Now;
        _playButton.Child = Glyphs.Make(Glyphs.Pause, 21, 2.0);
        ShowProgress();
    }

    private void Bind()
    {
        if (!Live) return;
        Unbind();
        _session = _manager?.GetCurrentSession();
        if (_session != null)
        {
            _session.PlaybackInfoChanged += OnPlayback;
            _session.MediaPropertiesChanged += OnMedia;
            _session.TimelinePropertiesChanged += OnTimeline;
        }
        App.Current.Log($"music session {_session?.SourceAppUserModelId ?? "нет"}");
        _coverKey = "";
        ReadPlayback();
        ReadTimeline();
        ReadMedia();
    }

    private void Unbind()
    {
        if (_session == null) return;
        _session.PlaybackInfoChanged -= OnPlayback;
        _session.MediaPropertiesChanged -= OnMedia;
        _session.TimelinePropertiesChanged -= OnTimeline;
        _session = null;
    }

    private void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs a) => Dispatcher.BeginInvoke(ReadPlayback);
    private void OnMedia(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs a) => Dispatcher.BeginInvoke(ReadMedia);
    private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs a) => Dispatcher.BeginInvoke(ReadTimeline);

    private void ReadPlayback()
    {
        try { _playing = _session?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { _playing = false; }
        _playButton.Child = Glyphs.Make(_playing ? Glyphs.Pause : Glyphs.Play, 21, 2.0);
        _posAt = DateTimeOffset.Now;
        bool visible = Music.Visible(_session != null, _playing);
        if (visible && _playing) _tick.Start(); else _tick.Stop();
        if (visible) { _hideLater.Stop(); RequestVisible(true); }
        else _hideLater.Start();
        ShowProgress();
    }

    private void ReadTimeline()
    {
        try
        {
            var t = _session?.GetTimelineProperties();
            if (t == null) return;
            _pos = t.Position;
            _len = t.EndTime - t.StartTime;
            _posAt = t.LastUpdatedTime.Year > 2000 ? t.LastUpdatedTime : DateTimeOffset.Now;
        }
        catch { }
        ShowProgress();
    }

    private async void ReadMedia()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var m = await s.TryGetMediaPropertiesAsync();
            if (s != _session || m == null) return;
            _title.Text = string.IsNullOrWhiteSpace(m.Title) ? Loc.T("Без названия") : m.Title;
            _artist.Text = string.IsNullOrWhiteSpace(m.Artist) ? AppName(s.SourceAppUserModelId) : m.Artist;
            _album.Text = m.AlbumTitle ?? "";
            ShowAlbum();
            // плеер часто присылает обложку отдельным, более поздним событием — ждём именно её
            string key = $"{m.Title}|{m.Artist}|{m.Thumbnail != null}";
            if (key == _coverKey) return;
            _coverKey = key;
            var img = m.Thumbnail != null ? await Cover(m.Thumbnail) : null;
            if (s != _session) return;
            App.Current.Log($"music track {m.Artist} — {m.Title} [{m.AlbumTitle}] cover={(img != null ? $"{img.PixelWidth}x{img.PixelHeight}" : "нет")}");
            _cover.Child = img != null ? new Image { Source = img, Stretch = Stretch.UniformToFill } : Glyphs.Make(Glyphs.Note, 36, 1.9);
            _cover.Clip = img != null ? new RectangleGeometry(new Rect(0, 0, _cover.Width, _cover.Height), 14, 14) : null;
            _backdrop.Source = img != null ? Blurred(img) : null;
        }
        catch (Exception ex) { App.Current.Log("music media failed: " + ex.Message); }
    }

    private static string AppName(string aumid)
    {
        var name = Path.GetFileNameWithoutExtension(aumid.Split('!').Last());
        return name.Length > 0 ? name : Loc.T("Музыка");
    }

    private static async Task<BitmapSource?> Cover(Windows.Storage.Streams.IRandomAccessStreamReference thumb)
    {
        using var ras = await thumb.OpenReadAsync();
        using var src = ras.AsStreamForRead();
        var ms = new MemoryStream();
        await src.CopyToAsync(ms);
        if (ms.Length == 0) return null;
        ms.Position = 0;
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.DecodePixelWidth = 600; // запас под растянутый виджет — обложка остаётся чёткой
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    /// <summary>Обложка, размытая в маленькую готовую картинку: фон окрашен в цвет трека без живого эффекта.</summary>
    private static BitmapSource Blurred(BitmapSource img)
    {
        const int W = 160, H = 160;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawImage(img, new Rect(-20, -20, W + 40, H + 40));
        dv.Effect = new BlurEffect { Radius = 16, RenderingBias = RenderingBias.Quality };
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private void ShowProgress()
    {
        var pos = Music.Position(_pos, _posAt, DateTimeOffset.Now, _playing, _len);
        double w = _bar.ActualWidth > 0 ? _bar.ActualWidth : 236;
        _barFill.Width = Math.Max(0, w * Music.Progress(pos, _len));
        _time.Text = _len > TimeSpan.Zero ? $"{Music.Time(pos)} / {Music.Time(_len)}" : "";
    }
}
