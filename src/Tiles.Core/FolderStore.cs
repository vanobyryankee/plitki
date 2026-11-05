namespace Tiles.Core;

/// <summary>
/// Папки плиток — настоящие папки на диске. Файлы только перемещаются, никогда не удаляются.
/// </summary>
public sealed class FolderStore
{
    /// <summary>Служебный файл программы в корне (настройки, их копии, журнал), а не папка-плитка.</summary>
    public static bool IsServiceFile(string root, string path)
    {
        var dir = Path.GetDirectoryName(path)?.TrimEnd('\\');
        if (!string.Equals(dir, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
        return Path.GetFileName(path).StartsWith("tiles.", StringComparison.OrdinalIgnoreCase);
    }

    private readonly string _desktop;
    private readonly string _root;

    public FolderStore(string desktopDir, string tilesRoot)
    {
        _desktop = desktopDir;
        _root = tilesRoot;
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;
    public string Desktop => _desktop;

    public string FolderPath(string folder) => Path.Combine(_root, folder);

    public string CreateFolder(string name)
    {
        name = CleanName(name);
        string candidate = name;
        for (int i = 2; Directory.Exists(FolderPath(candidate)); i++)
            candidate = $"{name} {i}";
        Directory.CreateDirectory(FolderPath(candidate));
        return candidate;
    }

    public string MoveIn(string path, string folder)
    {
        var dir = FolderPath(folder);
        Directory.CreateDirectory(dir);
        return MoveTo(path, dir);
    }

    public string MoveOut(string path) => MoveTo(path, _desktop);

    public void DeleteFolder(string folder)
    {
        var dir = FolderPath(folder);
        if (!Directory.Exists(dir)) return;
        foreach (var item in Items(folder))
            MoveOut(item);
        // desktop.ini и прочие скрытые служебные файлы — единственное, что может остаться
        foreach (var f in Directory.GetFiles(dir))
            File.Delete(f);
        if (!Directory.EnumerateFileSystemEntries(dir).Any())
            Directory.Delete(dir);
    }

    public string RenameFolder(string folder, string newName)
    {
        newName = CleanName(newName);
        if (newName == folder) return folder;
        string candidate = newName;
        for (int i = 2; Directory.Exists(FolderPath(candidate)); i++)
            candidate = $"{newName} {i}";
        Directory.Move(FolderPath(folder), FolderPath(candidate));
        return candidate;
    }

    public IReadOnlyList<string> Items(string folder)
    {
        var dir = FolderPath(folder);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.EnumerateFileSystemEntries(dir)
            .Where(p => !IsService(p))
            .OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static string UniquePath(string dir, string fileName)
    {
        string target = Path.Combine(dir, fileName);
        if (!File.Exists(target) && !Directory.Exists(target)) return target;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int i = 2; ; i++)
        {
            target = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(target) && !Directory.Exists(target)) return target;
        }
    }

    private static string MoveTo(string path, string dir)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (string.Equals(Path.GetDirectoryName(path), dir, StringComparison.OrdinalIgnoreCase))
            return path;
        string target = UniquePath(dir, name);
        if (Directory.Exists(path)) Directory.Move(path, target);
        else File.Move(path, target);
        return target;
    }

    private static bool IsService(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
        try { return (File.GetAttributes(path) & FileAttributes.Hidden) != 0; }
        catch { return false; }
    }

    private static string CleanName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !bad.Contains(c)).ToArray());
        clean = string.Join(' ', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        return clean.Length == 0 ? Loc.T("Папка") : clean;
    }
}
