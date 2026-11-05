using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tiles.Core;

namespace Tiles.App;

/// <summary>Стеклянный стикер: пишешь прямо в нём, текст сохраняется сам.</summary>
internal sealed class NoteView : WidgetView
{
    private readonly TextBox _box;

    public NoteView(WidgetInfo info) : base(info)
    {
        var head = Text(12.5, FontWeights.SemiBold, Soft);
        head.Text = Loc.T("Заметка");
        head.Margin = new Thickness(16, 12, 16, 4);
        _box = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White,
            CaretBrush = Brushes.White, SelectionBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x9C, 0xD4, 0xFF)),
            FontFamily = Body, FontSize = 15, FontWeight = FontWeights.Medium, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(12, 0, 12, 12),
            Text = info.Props.GetValueOrDefault("text", ""), Effect = Shadow,
        };
        _box.TextChanged += (_, _) => { Info.Props["text"] = _box.Text; SaveProps(); };
        // окно на рабочем столе по умолчанию не берёт клавиатуру — берём при клике в поле
        _box.PreviewMouseLeftButtonDown += (_, _) => { Window.GetWindow(this)?.Activate(); _box.Focus(); };
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(_box);
        Children.Add(dock);
    }

    public override string Title => Loc.T("Заметка");
    public override string Description => Loc.T("Стеклянный стикер: пишешь прямо на столе, текст сохраняется сам.");
    public override Size BaseSize => new(250, 190);

    public override bool OwnsMouse(DependencyObject? hit)
    {
        for (var d = hit; d != null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d == _box) return true;
        return false;
    }

    public override void Demo() { _box.Text = Loc.T("Купить молоко\nПозвонить маме\nДоделать плитки ✓"); _box.IsReadOnly = true; }
}
