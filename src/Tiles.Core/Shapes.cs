namespace Tiles.Core;

/// <summary>Форма виджета по его окну: длинный, квадратный или высокий.</summary>
public enum WidgetShape { Wide, Square, Tall }

public static class Shapes
{
    /// <summary>
    /// Форма по сторонам стекла. Шире полутора высот — длинный, выше 1,25 ширины — высокий,
    /// между ними — квадрат. Запас 0,1 у границы, чтобы на краю форма не мигала туда-сюда.
    /// </summary>
    public static WidgetShape Of(double w, double h, WidgetShape current = WidgetShape.Wide)
    {
        if (w <= 0 || h <= 0) return current;
        double r = w / h;
        const double wide = 1.5, tall = 0.8, slack = 0.1;
        double wideAt = current == WidgetShape.Wide ? wide - slack : wide + slack;
        double tallAt = current == WidgetShape.Tall ? tall + slack : tall - slack;
        if (r >= wideAt) return WidgetShape.Wide;
        if (r <= tallAt) return WidgetShape.Tall;
        return WidgetShape.Square;
    }
}
