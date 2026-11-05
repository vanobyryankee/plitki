using System.IO;

namespace Tiles.App;

/// <summary>Куда ведёт ярлык — нужно, чтобы подобрать папке название.</summary>
internal static class Shortcuts
{
    private static readonly Lazy<dynamic?> Shell = new(() =>
    {
        var t = Type.GetTypeFromProgID("WScript.Shell");
        return t == null ? null : Activator.CreateInstance(t);
    });

    public static string Target(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".lnk" && Shell.Value is { } sh)
            {
                var link = sh.CreateShortcut(path);
                return $"{link.TargetPath} {link.Arguments}";
            }
            if (ext == ".url")
                return File.ReadLines(path).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..] ?? "";
        }
        catch { }
        return path;
    }
}
