namespace Tiles.Core;

/// <summary>Настройки вида одной папки. Хранятся за каждой папкой отдельно.</summary>
public sealed class TileStyle
{
    public const int MinIcon = 24, MaxIcon = 96, MaxBlur = 80, MaxCorner = 40;

    /// <summary>Прилипать к клеткам ярлыков Windows. Выкл — плитка встаёт куда отпустили.</summary>
    public bool SnapToGrid { get; set; } = true;
    public bool ShowName { get; set; } = true;
    /// <summary>Имена под значками на самой плитке.</summary>
    public bool TileCaptions { get; set; }
    /// <summary>Имена под значками в открытой панели.</summary>
    public bool PanelCaptions { get; set; } = true;
    /// <summary>Насколько густой оттенок поверх размытия, 0–100 %.</summary>
    public int GlassOpacity { get; set; } = 18;
    public int Blur { get; set; } = 36;
    /// <summary>Цвет оттенка стекла, #RRGGBB.</summary>
    public string Tint { get; set; } = "#FFFFFF";
    public int IconSize { get; set; } = 48;
    /// <summary>Тень под плиткой, 0–100 %.</summary>
    public int Shadow { get; set; } = 30;
    public int Corner { get; set; } = 20;
    /// <summary>Яркость светлой кромки, 0–100 %.</summary>
    public int Edge { get; set; } = 33;

    /// <summary>Клик по значку на свёрнутой плитке запускает программу; панель — по пустому месту.</summary>
    public bool ClickLaunch { get; set; }
    /// <summary>Правая кнопка по значку — меню проводника.</summary>
    public bool ShellMenu { get; set; } = true;
    /// <summary>Стеклянная подложка. Выкл — на столе только значки и подписи, как у обычных ярлыков.</summary>
    public bool Glass { get; set; } = true;

    public TileStyle Clone() => (TileStyle)MemberwiseClone();

    /// <summary>Заливка стекла AARRGGBB: цвет оттенка с прозрачностью GlassOpacity.</summary>
    public uint TintArgb()
    {
        uint a = (uint)Math.Round(Math.Clamp(GlassOpacity, 0, 100) * 255 / 100.0, MidpointRounding.AwayFromZero);
        return (a << 24) | (ParseRgb(Tint) ?? 0xFFFFFF);
    }

    /// <summary>Приводит значения к допустимым (после ручной правки tiles.json).</summary>
    public void Normalize()
    {
        GlassOpacity = Math.Clamp(GlassOpacity, 0, 100);
        Blur = Math.Clamp(Blur, 0, MaxBlur);
        IconSize = Math.Clamp(IconSize, MinIcon, MaxIcon);
        Shadow = Math.Clamp(Shadow, 0, 100);
        Corner = Math.Clamp(Corner, 0, MaxCorner);
        Edge = Math.Clamp(Edge, 0, 100);
        if (ParseRgb(Tint) == null) Tint = "#FFFFFF";
    }

    private static uint? ParseRgb(string? s)
    {
        if (s is not { Length: 7 } || s[0] != '#') return null;
        return uint.TryParse(s.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
    }
}
