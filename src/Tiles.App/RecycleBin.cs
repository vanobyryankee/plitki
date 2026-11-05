using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace Tiles.App;

/// <summary>Корзина Windows: сколько в ней, очистить, выбросить файлы, спрятать её значок со стола.</summary>
internal static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd; public uint wFunc;
        public string pFrom; public string? pTo;
        public ushort fFlags; public int fAnyOperationsAborted;
        public IntPtr hNameMappings; public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr a, IntPtr b);

    public static (long Count, long Bytes) Query()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64NumItems, info.i64Size) : (0, 0);
    }

    /// <summary>Очистить. Windows сама спросит «Вы уверены?».</summary>
    public static void Empty(IntPtr owner) => SHEmptyRecycleBin(owner, null, 0);

    /// <summary>Выбросить в корзину (с возможностью вернуть), как перетаскивание на корзину Windows.</summary>
    public static bool Delete(IntPtr owner, IEnumerable<string> paths)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (list.Count == 0) return false;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner, wFunc = 3, // FO_DELETE
            pFrom = string.Join("\0", list) + "\0\0",
            fFlags = 0x0040 | 0x4000, // FOF_ALLOWUNDO | FOF_WANTNUKEWARNING
        };
        return SHFileOperation(ref op) == 0 && op.fAnyOperationsAborted == 0;
    }

    public static void Open()
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });

    /// <summary>Папки корзины на всех дисках (только своя, по SID) — следить за изменениями.</summary>
    public static IEnumerable<string> Folders()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid == null) yield break;
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
            var dir = Path.Combine(d.RootDirectory.FullName, "$Recycle.Bin", sid);
            if (Directory.Exists(dir)) yield return dir;
        }
    }

    private const string HideKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    private const string BinGuid = "{645FF040-5081-101B-9F08-00AA002F954E}";

    /// <summary>Спрятать или вернуть обычную корзину Windows на рабочем столе.</summary>
    public static void HideWindowsIcon(bool hide)
    {
        using var key = Registry.CurrentUser.CreateSubKey(HideKey);
        if (key.GetValue(BinGuid) is int v && v == (hide ? 1 : 0)) return;
        key.SetValue(BinGuid, hide ? 1 : 0, RegistryValueKind.DWord);
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED — стол перечитает значки
        App.Current.Log($"windows bin icon hidden={hide}");
    }
}
