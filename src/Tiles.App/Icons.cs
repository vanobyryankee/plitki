using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tiles.App;

/// <summary>Кэш значков: путь + время изменения + размер в пикселях экрана.</summary>
internal static class Icons
{
    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Значок в точном размере экрана: size — в точках интерфейса, умножается на масштаб.</summary>
    public static BitmapSource? Get(string path, double size)
    {
        // Просим ближайший стандартный размер: нестандартный (72) Windows для ярлыков Steam отдаёт глобусом.
        int need = (int)Math.Round(size * App.Current.DpiScale);
        int px = new[] { 32, 48, 64, 96, 128, 256 }.FirstOrDefault(s => s >= need, 256);
        string key;
        try { key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{px}"; }
        catch { key = $"{path}|{px}"; }
        if (!Cache.TryGetValue(key, out var img))
            Cache[key] = img = Native.ShellIcon(UrlIconFile(path) ?? path, px);
        return img;
    }

    /// <summary>
    /// У интернет-ярлыков (игры Steam) Windows часто отдаёт глобус, хотя файл значка есть.
    /// Берём значок прямо из IconFile.
    /// </summary>
    private static string? UrlIconFile(string path)
    {
        if (!path.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var line = File.ReadLines(path).FirstOrDefault(l => l.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase));
            var file = line?["IconFile=".Length..].Trim();
            return file != null && File.Exists(file) ? file : null;
        }
        catch { return null; }
    }

    /// <summary>Картинка значка, которую WPF не будет пересэмплировать.</summary>
    public static Image Image(string path, double size)
    {
        var img = new Image { Source = Get(path, size), Width = size, Height = size, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    /// <summary>Имя без .lnk/.url, как подписывает проводник.</summary>
    public static string DisplayName(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(name);
        return ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }
}
