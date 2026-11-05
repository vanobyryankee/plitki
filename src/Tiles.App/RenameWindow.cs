using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tiles.Core;

namespace Tiles.App;

/// <summary>Окошко «Переименовать папку».</summary>
internal sealed class RenameWindow : Window
{
    private readonly TextBox _box;
    public string Value => _box.Text;

    public RenameWindow(string current, string? caption = null)
    {
        caption ??= Loc.T("Название папки");
        Title = Loc.T("Переименовать");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        _box = new TextBox
        {
            Text = current, FontSize = 15, Width = 280, Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0),
        };
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { DialogResult = !string.IsNullOrWhiteSpace(_box.Text); }
            if (e.Key == Key.Escape) { DialogResult = false; }
        };

        Content = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x26, 0x26, 0x2A)),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = caption, Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) },
                    _box,
                    new TextBlock
                    {
                        Text = Loc.T("Enter — сохранить, Esc — отмена"),
                        Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                        FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0),
                    },
                },
            },
        };

        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        Deactivated += (_, _) => { if (IsVisible && DialogResult == null) try { DialogResult = false; } catch { } };
    }
}
