using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Общее для всего стеклянного на рабочем столе: стекло как у плиток, вид из общих настроек,
/// окно прибито к рабочему столу и не мешает обычным окнам, подсветка выделения как у Windows.
/// </summary>
internal abstract class GlassWidget : Window
{
    private static readonly Brush NoGlass = new SolidColorBrush(Color.FromArgb(0x99, 0x30, 0x30, 0x30));
    public static readonly Brush SelectFill = new SolidColorBrush(Color.FromArgb(0x40, 0x7C, 0xC4, 0xFF));
    public static readonly Brush SelectEdge = new SolidColorBrush(Color.FromArgb(0xE6, 0x9C, 0xD4, 0xFF));
    protected static readonly Effect TextShadow = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.75 };

    protected readonly Border Glass;
    protected readonly Border Frame;
    protected Brush Idle = Brushes.Transparent, Hover = Brushes.Transparent, EdgeBrush = Brushes.Transparent;
    private bool _selected;

    protected GlassWidget(string title)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        Frame = new Border { BorderThickness = new Thickness(1) };
        Glass = new Border { Background = NoGlass, Child = Frame };
        SourceInitialized += (_, _) => Native.PinToDesktop(this);
    }

    /// <summary>Вид: у виджета может быть свой, иначе общий.</summary>
    protected virtual TileStyle Look => App.Current.Config.Defaults;

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Paint(false); }
    }

    /// <summary>Заливка рамки: выделение сильнее наведения.</summary>
    protected void Paint(bool hover)
    {
        Frame.Background = _selected ? SelectFill : hover ? Hover : Idle;
        Frame.BorderBrush = _selected ? SelectEdge : EdgeBrush;
    }

    /// <summary>Вид из общих настроек: оттенок, кромка, скругление, тень.</summary>
    public virtual void ApplyLook()
    {
        var s = Look;
        uint tint = s.TintArgb();
        byte a = (byte)(tint >> 24);
        // без стекла: почти прозрачная заливка (совсем прозрачное окно не ловит мышь)
        Idle = s.Glass ? new SolidColorBrush(Color.FromArgb(a, (byte)(tint >> 16), (byte)(tint >> 8), (byte)tint)) : HitOnly;
        Hover = s.Glass ? new SolidColorBrush(Color.FromArgb((byte)Math.Min(255, a + 26), (byte)(tint >> 16), (byte)(tint >> 8), (byte)tint)) : BareHover;
        EdgeBrush = s.Glass ? new SolidColorBrush(Color.FromArgb((byte)(s.Edge * 255 / 100), 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
        Paint(false);
        Frame.CornerRadius = Glass.CornerRadius = new CornerRadius(Math.Min(s.Corner, CornerLimit));
        Glass.Effect = s.Glass && s.Shadow > 0
            ? new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = s.Shadow / 100.0 }
            : null;
        UpdateGlass();
    }

    protected virtual double CornerLimit => 40;

    /// <summary>Перерисовать размытый фон ровно под стеклом (где бы оно ни было внутри окна и в каком масштабе).</summary>
    public void UpdateGlass()
    {
        if (!IsVisible || Glass.ActualWidth <= 0) { Dispatcher.BeginInvoke(UpdateGlassLater, System.Windows.Threading.DispatcherPriority.Loaded); return; }
        UpdateGlassLater();
    }

    private static readonly Brush HitOnly = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
    private static readonly Brush BareHover = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

    private void UpdateGlassLater() => ShowGlass(false);

    /// <summary>Кадр переноса: стекло показывает то, что сейчас под ним (как линза), без нового снимка стола.</summary>
    public void FollowGlass() => ShowGlass(true);

    /// <summary>Где стекло на экране (физические пиксели). Виджет на «сцене» говорит сам.</summary>
    protected virtual Int32Rect? GlassOnScreen()
    {
        if (!IsVisible || Glass.ActualWidth <= 0) return null;
        // окно могли сдвинуть в обход WPF: берём место окна у Windows, а сдвиг стекла внутри — у WPF
        var wr = Native.WindowRect(this);
        double k = App.Current.DpiScale;
        var inner = Glass.TransformToAncestor(this).TransformBounds(new Rect(0, 0, Glass.ActualWidth, Glass.ActualHeight));
        return new Int32Rect(wr.Left + (int)Math.Round(inner.X * k), wr.Top + (int)Math.Round(inner.Y * k),
            Math.Max(1, (int)Math.Round(inner.Width * k)), Math.Max(1, (int)Math.Round(inner.Height * k)));
    }

    private void ShowGlass(bool live)
    {
        if (!Look.Glass) { Glass.Background = HitOnly; return; }
        if (GlassOnScreen() is not { } rect) return;
        var brush = Wallpaper.Lens(rect, Look.Blur, Glass.Background as ImageBrush, live);
        if (!ReferenceEquals(brush, Glass.Background)) Glass.Background = (Brush?)brush ?? NoGlass;
    }
}
