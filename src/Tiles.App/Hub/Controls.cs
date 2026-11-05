using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Tiles.App;

/// <summary>Цвета и шрифты окна приложения — одни на всё.</summary>
internal static class Ui
{
    public static readonly FontFamily Display = WidgetView.Display;
    public static readonly FontFamily Body = WidgetView.Body;
    public static readonly Brush Text = Brushes.White;
    public static readonly Brush Soft = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Faint = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Line = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Card = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
    public static readonly Brush CardHover = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x6C, 0xB8, 0xFF));
    public static readonly Brush AccentSoft = new SolidColorBrush(Color.FromArgb(0x33, 0x6C, 0xB8, 0xFF));
    public static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1C));

    public static TextBlock T(string text, double size, FontWeight weight, Brush? brush = null, FontFamily? font = null) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = brush ?? Text, FontFamily = font ?? Body,
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock H1(string text) => T(text, 34, FontWeights.SemiBold, Text, Display);
    public static TextBlock Sub(string text) => T(text, 14.5, FontWeights.Normal, Soft);

    /// <summary>Кнопка: главная — белая с тёмным текстом, обычная — стеклянная.</summary>
    public static Border Button(string text, Action click, bool primary = false, string? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (glyph != null) content.Children.Add(new Border { Margin = new Thickness(0, 0, 8, 0), Child = Glyphs.Make(glyph, 16, 2, null, primary ? Ink : Brushes.White) });
        content.Children.Add(T(text, 13.5, FontWeights.SemiBold, primary ? Ink : Text));
        Brush idle = primary ? new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)) : Card;
        Brush hover = primary ? Brushes.White : CardHover;
        var b = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 9, 16, 9), Background = idle, Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1), BorderBrush = primary ? Brushes.Transparent : Line, Child = content,
        };
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => b.Background = idle;
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return b;
    }
}

/// <summary>Переключатель как в настройках Windows 11.</summary>
internal sealed class Toggle : Border
{
    private readonly Ellipse _knob = new() { Width = 14, Height = 14, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0) };
    private readonly TranslateTransform _move = new();
    private bool _on;
    public event Action<bool>? Changed;

    public Toggle(bool on)
    {
        Width = 44; Height = 22; CornerRadius = new CornerRadius(11); BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand; VerticalAlignment = VerticalAlignment.Center;
        _knob.RenderTransform = _move;
        Child = new Grid { Children = { _knob } };
        _on = on;
        Paint(false);
        MouseLeftButtonUp += (_, e) => { On = !On; Changed?.Invoke(_on); e.Handled = true; };
    }

    public bool On
    {
        get => _on;
        set { if (_on == value) return; _on = value; Paint(true); }
    }

    private void Paint(bool animate)
    {
        Background = _on ? Ui.Accent : Brushes.Transparent;
        BorderBrush = _on ? Ui.Accent : Ui.Faint;
        _knob.Fill = _on ? Ui.Ink : Ui.Soft;
        double x = _on ? 22 : 0;
        if (animate) _move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase() });
        else _move.X = x;
    }
}

/// <summary>Ползунок: тонкая дорожка, белая заливка, круглая ручка.</summary>
internal sealed class GlassSlider : Grid
{
    private readonly Border _fill = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Ellipse _thumb = new() { Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly double _min, _max;
    private double _value;
    public event Action<double>? Changed;

    public GlassSlider(double min, double max, double value)
    {
        _min = min; _max = max; _value = value;
        Height = 22; Background = Brushes.Transparent; Cursor = Cursors.Hand;
        Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Line });
        Children.Add(_fill);
        Children.Add(_thumb);
        SizeChanged += (_, _) => Layout();
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Set(e.GetPosition(this).X); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured) Set(e.GetPosition(this).X); };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
    }

    public double Value => _value;

    private void Set(double x)
    {
        double w = Math.Max(1, ActualWidth - 16);
        double v = Math.Round(_min + Math.Clamp((x - 8) / w, 0, 1) * (_max - _min));
        if (v == _value) return;
        _value = v;
        Layout();
        Changed?.Invoke(v);
    }

    private void Layout()
    {
        double w = Math.Max(1, ActualWidth - 16), k = (_value - _min) / Math.Max(1, _max - _min);
        _thumb.Margin = new Thickness(k * w, 0, 0, 0);
        _fill.Width = k * w + 8;
    }
}
