using System.Runtime.InteropServices;

namespace Macrofy.Core.Macros;

// Opens a file, app or link as the signed-in user even when Macrofy runs as administrator,
// by asking the desktop's Explorer (which runs un-elevated) to do the ShellExecute. Without
// this, every app a macro launched from an elevated Macrofy would run as admin too.
// (Raymond Chen, "How can I launch an unelevated process from my elevated process?")
internal static class ShellLauncher
{
    private const int CSIDL_DESKTOP = 0;
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const int SW_SHOWNORMAL = 1;

    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    // Returns false if Explorer isn't available (e.g. it crashed); the caller then falls back
    // to launching directly.
    public static bool TryOpen(string file, string arguments, string? workingDirectory)
    {
        bool ok = false;
        var thread = new Thread(() =>
        {
            try
            {
                var windows = (IShellWindows)new ShellWindowsClass();
                object loc = CSIDL_DESKTOP;
                object root = new();
                var provider = (IServiceProvider)windows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
                Guid sid = SID_STopLevelBrowser, iid = IID_IShellBrowser;
                var browser = (IShellBrowser)provider.QueryService(ref sid, ref iid);
                var view = browser.QueryActiveShellView();
                Guid dispatch = IID_IDispatch;
                dynamic folderView = view.GetItemObject(0 /* SVGIO_BACKGROUND */, ref dispatch);
                dynamic shell = folderView.Application;
                shell.ShellExecute(file, arguments, workingDirectory ?? string.Empty, "open", SW_SHOWNORMAL);
                ok = true;
            }
            catch
            {
                ok = false;
            }
        })
        { IsBackground = true, Name = "Macrofy.ShellLaunch" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10_000);
        return ok;
    }

    [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
    private class ShellWindowsClass { }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] object Item([MarshalAs(UnmanagedType.Struct)] object index);
        [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
        int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass);
        int RegisterPending(int threadId, [MarshalAs(UnmanagedType.Struct)] ref object loc, [MarshalAs(UnmanagedType.Struct)] ref object locRoot, int swClass);
        void Revoke(int cookie);
        void OnNavigate(int cookie, [MarshalAs(UnmanagedType.Struct)] ref object loc);
        void OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object loc, [MarshalAs(UnmanagedType.Struct)] ref object locRoot,
            int swClass, out int hwnd, int swfwOptions);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid service, ref Guid riid);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out nint hwnd);
        void ContextSensitiveHelp(int enterMode);
        void InsertMenusSB(nint menuShared, nint menuWidths);
        void SetMenuSB(nint menuShared, nint oleMenuRes, nint activeObject);
        void RemoveMenusSB(nint menuShared);
        void SetStatusTextSB(nint statusText);
        void EnableModelessSB(int enable);
        void TranslateAcceleratorSB(nint msg, ushort id);
        void BrowseObject(nint pidl, uint flags);
        void GetViewStateStream(uint mode, out nint stream);
        void GetControlWindow(uint id, out nint hwnd);
        void SendControlMsg(uint id, uint msg, nint wParam, nint lParam, out nint result);
        IShellView QueryActiveShellView();
        void OnViewWindowActive(IShellView view);
        void SetToolbarItems(nint buttons, uint count, uint flags);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow(out nint hwnd);
        void ContextSensitiveHelp(int enterMode);
        void TranslateAccelerator(nint msg);
        void EnableModeless(int enable);
        void UIActivate(uint state);
        void Refresh();
        void CreateViewWindow(nint previous, nint folderSettings, nint browser, nint rect, out nint hwnd);
        void DestroyViewWindow();
        void GetCurrentInfo(nint folderSettings);
        void AddPropertySheetPages(uint reserved, nint callback, nint lParam);
        void SaveViewState();
        void SelectItem(nint pidl, uint flags);
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object GetItemObject(uint item, ref Guid riid);
    }
}
