using System.Runtime.InteropServices;
using System.Text;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Ярлыки Windows на рабочем столе: где стоят и как их переставить. Через окно рабочего стола
/// проводника (IFolderView), без хуков и без внедрения в explorer.
/// </summary>
internal static class DesktopIcons
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
    private class ShellWindowsClass { }

    // Двойной интерфейс .NET без библиотеки типов не вызывает — описываем таблицу целиком,
    // первые четыре места — методы IDispatch.
    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellWindows
    {
        void GetTypeInfoCount();
        void GetTypeInfo();
        void GetIDsOfNames();
        void Invoke();
        [PreserveSig] int get_Count(out int c);
        [PreserveSig] int Item(object index, [MarshalAs(UnmanagedType.IDispatch)] out object folder);
        [PreserveSig] int _NewEnum([MarshalAs(UnmanagedType.IUnknown)] out object e);
        [PreserveSig] int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int cookie);
        [PreserveSig] int RegisterPending(int threadId, ref object loc, ref object locRoot, int swClass, out int cookie);
        [PreserveSig] int Revoke(int cookie);
        [PreserveSig] int OnNavigate(int cookie, ref object loc);
        [PreserveSig] int OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);
        [PreserveSig] int FindWindowSW(ref object loc, ref object locRoot, int swClass, out int hwnd, int options,
            [MarshalAs(UnmanagedType.IDispatch)] out object disp);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid sid, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out IntPtr h);
        void ContextSensitiveHelp(int enter);
        void InsertMenusSB(IntPtr a, IntPtr b);
        void SetMenuSB(IntPtr a, IntPtr b, IntPtr c);
        void RemoveMenusSB(IntPtr a);
        void SetStatusTextSB(IntPtr a);
        void EnableModelessSB(int enable);
        void TranslateAcceleratorSB(IntPtr a, short b);
        void BrowseObject(IntPtr a, uint b);
        void GetViewStateStream(uint a, out IntPtr b);
        void GetControlWindow(uint a, out IntPtr b);
        void SendControlMsg(uint a, uint b, IntPtr c, IntPtr d, out IntPtr e);
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
    }

    [ComImport, Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint mode);
        [PreserveSig] int SetCurrentViewMode(uint mode);
        [PreserveSig] int GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object folder);
        [PreserveSig] int Item(int index, out IntPtr pidl);
        [PreserveSig] int ItemCount(uint flags, out int count);
        [PreserveSig] int Items(uint flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int index);
        [PreserveSig] int GetFocusedItem(out int index);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT pt);
        [PreserveSig] int GetSpacing(out POINT pt);
        [PreserveSig] int GetDefaultSpacing(out POINT pt);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int index, uint flags);
        [PreserveSig] int SelectAndPositionItems(uint count, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls,
            [In, MarshalAs(UnmanagedType.LPArray)] POINT[] points, uint flags);
    }

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
        void GetUIObjectOf(IntPtr hwnd, uint count, IntPtr pidls, ref Guid riid, IntPtr reserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr strret);
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrRetToBuf(IntPtr strret, IntPtr pidl, StringBuilder buf, int cch);

    public sealed record Icon(string Path, IntPtr Pidl, PointD Pos);

    /// <summary>Вид рабочего стола и снимок ярлыков. Pidl освобождаются в Dispose.</summary>
    public sealed class Snapshot : IDisposable
    {
        private IFolderView _view = null!;
        internal void Attach(object view) => _view = (IFolderView)view;
        private IFolderView View => _view;
        public List<Icon> Icons { get; } = new();
        public double SpacingX, SpacingY;
        public bool AutoArrange;

        public bool MoveTo(Icon icon, PointD p)
        {
            var hr = View.SelectAndPositionItems(1, new[] { icon.Pidl }, new[] { new POINT { X = (int)p.X, Y = (int)p.Y } }, 0x80); // SVSI_POSITIONITEM
            return hr >= 0;
        }

        public void Dispose()
        {
            foreach (var i in Icons) Marshal.FreeCoTaskMem(i.Pidl);
            Icons.Clear();
        }
    }

    /// <summary>Снять ярлыки рабочего стола: путь и место каждого (в координатах вида рабочего стола).</summary>
    public static Snapshot? Read()
    {
        var windows = (IShellWindows)new ShellWindowsClass();
        object loc = 0; // CSIDL_DESKTOP
        object empty = null!; // VT_EMPTY
        if (windows.FindWindowSW(ref loc, ref empty, 8 /* SWC_DESKTOP */, out _, 1 /* SWFO_NEEDDISPATCH */, out var disp) != 0 || disp == null)
            return null;
        var sid = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
        var iidBrowser = typeof(IShellBrowser).GUID;
        if (((IComServiceProvider)disp).QueryService(ref sid, ref iidBrowser, out var b) != 0) return null;
        if (((IShellBrowser)b).QueryActiveShellView(out var viewObj) != 0) return null;
        var view = (IFolderView)viewObj;

        var iidFolder = typeof(IShellFolder).GUID;
        view.GetFolder(ref iidFolder, out var folderObj);
        var folder = (IShellFolder)folderObj;

        var snap = new Snapshot { AutoArrange = view.GetAutoArrange() == 0 };
        snap.Attach(view);
        view.GetSpacing(out var sp);
        snap.SpacingX = sp.X;
        snap.SpacingY = sp.Y;

        view.ItemCount(2 /* SVGIO_ALLVIEW */, out int count);
        IntPtr strret = Marshal.AllocCoTaskMem(272);
        try
        {
            for (int i = 0; i < count; i++)
            {
                if (view.Item(i, out var pidl) != 0) continue;
                view.GetItemPosition(pidl, out var pt);
                string path = "";
                if (folder.GetDisplayNameOf(pidl, 0x8000 /* SHGDN_FORPARSING */, strret) == 0)
                {
                    var sb = new StringBuilder(1024);
                    StrRetToBuf(strret, pidl, sb, sb.Capacity);
                    path = sb.ToString();
                }
                snap.Icons.Add(new Icon(path, pidl, new PointD(pt.X, pt.Y)));
            }
        }
        finally { Marshal.FreeCoTaskMem(strret); }
        return snap;
    }
}
