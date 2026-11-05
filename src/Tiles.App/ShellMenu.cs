using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Tiles.App;

/// <summary>Обычное меню проводника для файла или ярлыка: «Открыть», «Расположение файла», «Свойства» и т.д.</summary>
internal static class ShellMenu
{
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eaten, out IntPtr pidl, IntPtr attrs);
        void EnumObjects(IntPtr hwnd, int flags, out IntPtr e);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lparam, IntPtr a, IntPtr b);
        void CreateViewObject(IntPtr hwnd, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint count, IntPtr pidls, ref uint attrs);
        [PreserveSig] int GetUIObjectOf(IntPtr hwnd, uint count, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls,
            ref Guid riid, IntPtr reserved, out IntPtr ppv);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFO
    {
        public int cbSize, fMask;
        public IntPtr hwnd, lpVerb, lpParameters, lpDirectory;
        public int nShow, dwHotKey;
        public IntPtr hIcon;
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
        [PreserveSig] int GetCommandString(UIntPtr cmd, uint type, IntPtr reserved, IntPtr name, uint cch);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
        [PreserveSig] int GetCommandString(UIntPtr cmd, uint type, IntPtr reserved, IntPtr name, uint cch);
        [PreserveSig] int HandleMenuMsg(int msg, IntPtr w, IntPtr l);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
        [PreserveSig] int GetCommandString(UIntPtr cmd, uint type, IntPtr reserved, IntPtr name, uint cch);
        [PreserveSig] int HandleMenuMsg(int msg, IntPtr w, IntPtr l);
        [PreserveSig] int HandleMenuMsg2(int msg, IntPtr w, IntPtr l, out IntPtr result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr pidlLast);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Native.POINT pt);

    private static IContextMenu2? _cm2;
    private static IContextMenu3? _cm3;
    private static readonly HashSet<IntPtr> Hooked = new();

    /// <summary>Показать меню проводника для файла под курсором. Вернёт true, если что-то выбрали.</summary>
    public static bool Show(Window owner, string path)
    {
        var hwnd = new WindowInteropHelper(owner).Handle;
        if (Hooked.Add(hwnd)) HwndSource.FromHwnd(hwnd)?.AddHook(Hook);

        IntPtr pidl = IntPtr.Zero, folderPtr = IntPtr.Zero, cmPtr = IntPtr.Zero, menu = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0) return false;
            var iidFolder = typeof(IShellFolder).GUID;
            if (SHBindToParent(pidl, ref iidFolder, out folderPtr, out var child) != 0) return false;
            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            var iidMenu = typeof(IContextMenu).GUID;
            if (folder.GetUIObjectOf(hwnd, 1, new[] { child }, ref iidMenu, IntPtr.Zero, out cmPtr) != 0) return false;
            var cm = (IContextMenu)Marshal.GetObjectForIUnknown(cmPtr);
            _cm2 = cm as IContextMenu2;
            _cm3 = cm as IContextMenu3;

            menu = CreatePopupMenu();
            const uint first = 1;
            cm.QueryContextMenu(menu, 0, first, 0x7FFF, 0); // CMF_NORMAL
            GetCursorPos(out var pt);
            SetForegroundWindow(hwnd);
            int cmd = TrackPopupMenuEx(menu, 0x0100 | 0x0002, pt.X, pt.Y, hwnd, IntPtr.Zero); // TPM_RETURNCMD | TPM_RIGHTBUTTON
            PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero); // чтобы меню закрывалось кликом мимо
            if (cmd <= 0) return false;

            var info = new CMINVOKECOMMANDINFO
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                hwnd = hwnd,
                lpVerb = new IntPtr(cmd - first),
                nShow = 1, // SW_SHOWNORMAL
            };
            cm.InvokeCommand(ref info);
            return true;
        }
        catch (Exception ex)
        {
            App.Current.Log($"shell menu failed {path}: {ex.Message}");
            return false;
        }
        finally
        {
            _cm2 = null;
            _cm3 = null;
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (cmPtr != IntPtr.Zero) Marshal.Release(cmPtr);
            if (folderPtr != IntPtr.Zero) Marshal.Release(folderPtr);
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>Подменю «Отправить», «Открыть с помощью» рисует сам проводник — передаём ему сообщения.</summary>
    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg is not (0x117 or 0x2B or 0x2C or 0x120)) return IntPtr.Zero; // INITMENUPOPUP, DRAWITEM, MEASUREITEM, MENUCHAR
        if (_cm3 != null && _cm3.HandleMenuMsg2(msg, w, l, out var res) == 0) { handled = true; return res; }
        if (_cm2 != null && _cm2.HandleMenuMsg(msg, w, l) == 0) { handled = true; return IntPtr.Zero; }
        return IntPtr.Zero;
    }
}
