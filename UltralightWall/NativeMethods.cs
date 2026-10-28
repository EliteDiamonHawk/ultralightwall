using System.Runtime.InteropServices;
using System.Text;

namespace UltralightWall;

internal static class NativeMethods
{
    internal const int WM_CLOSE = 0x0010;
    internal const int WM_ERASEBKGND = 0x0014;
    internal const int WM_CANCELMODE = 0x001F;
    internal const int WM_MOUSEMOVE = 0x0200;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_LBUTTONDBLCLK = 0x0203;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_RBUTTONUP = 0x0205;
    internal const int WM_MBUTTONDOWN = 0x0207;
    internal const int WM_MBUTTONUP = 0x0208;
    internal const int WM_MOUSEWHEEL = 0x020A;
    internal const int WM_MOUSEHWHEEL = 0x020E;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;
    internal const int WM_SPAWN_WORKERW = 0x052C;

    internal const int WH_KEYBOARD_LL = 13;
    internal const int WH_MOUSE_LL = 14;
    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const long WS_CAPTION = 0x00C00000L;
    internal const long WS_THICKFRAME = 0x00040000L;
    internal const long WS_MINIMIZEBOX = 0x00020000L;
    internal const long WS_MAXIMIZEBOX = 0x00010000L;
    internal const long WS_SYSMENU = 0x00080000L;
    internal const long WS_CHILD = 0x40000000L;
    internal const long WS_VISIBLE = 0x10000000L;
    internal const long WS_CLIPSIBLINGS = 0x04000000L;
    internal const long WS_CLIPCHILDREN = 0x02000000L;
    internal const long WS_POPUP = unchecked((long)0x80000000);
    internal const long WS_EX_APPWINDOW = 0x00040000L;
    internal const long WS_EX_TOOLWINDOW = 0x00000080L;
    internal const long WS_EX_NOACTIVATE = 0x08000000L;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_FRAMECHANGED = 0x0020;
    internal const uint SWP_NOSENDCHANGING = 0x0400;
    internal const uint SWP_NOOWNERZORDER = 0x0200;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    internal const uint CWP_SKIPINVISIBLE = 0x0001;
    internal const int GW_OWNER = 4;
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_SYSTEM_MOVESIZEEND = 0x000A;
    internal const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const int OBJID_WINDOW = 0;
    internal const int CHILDID_SELF = 0;
    internal static readonly IntPtr HWND_BOTTOM = new(1);
    internal static readonly IntPtr HWND_TOP = IntPtr.Zero;

    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    internal delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int GetWindowTextLengthW(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr processHandle, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr PostMessageW(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr SendMessageW(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowExW(
        uint extendedStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int MapWindowPoints(IntPtr from, IntPtr to, [In, Out] POINT[] points, uint count);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

    [DllImport("user32.dll")]
    internal static extern IntPtr ChildWindowFromPointEx(IntPtr parent, POINT point, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookExW(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    internal static extern short GetKeyState(int key);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, uint size, uint allocationType, uint protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, int size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, int size, out int read);

    internal static int GetWindowTextLength(IntPtr hwnd) => GetWindowTextLengthW(hwnd);
    internal static void GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount) => GetWindowTextW(hwnd, text, maxCount);

    internal static string GetWindowTitle(IntPtr hwnd)
    {
        int length = GetWindowTextLengthW(hwnd);
        if (length <= 0)
            return string.Empty;

        var title = new StringBuilder(Math.Min(length + 1, 4096));
        GetWindowTextW(hwnd, title, title.Capacity);
        return title.ToString();
    }

    internal static bool TryGetProcessImagePath(IntPtr hwnd, out string path)
    {
        path = string.Empty;
        GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == 0)
            return false;

        const uint ProcessQueryLimitedInformation = 0x1000;
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
            return false;

        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            if (!QueryFullProcessImageNameW(process, 0, buffer, ref size))
                return false;
            path = buffer.ToString();
            return path.Length > 0;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    internal static bool TryGetWindowRect(IntPtr hwnd, out Rectangle bounds)
    {
        if (GetWindowRect(hwnd, out RECT rect))
        {
            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return true;
        }
        bounds = Rectangle.Empty;
        return false;
    }

    internal static bool IsDescendantOrSelf(IntPtr hwnd, IntPtr ancestor) =>
        hwnd == ancestor || GetAncestor(hwnd, 2) == ancestor;

    internal static bool TryClearDesktopSelection(IntPtr desktopList)
    {
        GetWindowThreadProcessId(desktopList, out uint processId);
        if (processId == 0)
            return false;

        const uint access = 0x0008 | 0x0010 | 0x0020;
        IntPtr process = OpenProcess(access, false, processId);
        if (process == IntPtr.Zero)
            return false;

        IntPtr remote = IntPtr.Zero;
        try
        {
            int size = Marshal.SizeOf<LVITEM>();
            remote = VirtualAllocEx(process, IntPtr.Zero, (uint)size, 0x1000, 0x04);
            if (remote == IntPtr.Zero)
                return false;

            var item = new LVITEM { mask = 0x0008, state = 0, stateMask = 0x0002 | 0x0001 };
            IntPtr local = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(item, local, false);
                var buffer = new byte[size];
                Marshal.Copy(local, buffer, 0, size);
                WriteProcessMemory(process, remote, buffer, size, out _);
            }
            finally
            {
                Marshal.FreeHGlobal(local);
            }

            SendMessageW(desktopList, 0x1000 + 43, new IntPtr(-1), remote);
            return true;
        }
        finally
        {
            if (remote != IntPtr.Zero)
                VirtualFreeEx(process, remote, 0, 0x8000);
            CloseHandle(process);
        }
    }

    internal static bool IsDesktopIconAt(IntPtr list, POINT screenPoint)
    {
        GetWindowThreadProcessId(list, out uint processId);
        if (processId == 0)
            return false;

        const uint access = 0x0008 | 0x0010 | 0x0020;
        IntPtr process = OpenProcess(access, false, processId);
        if (process == IntPtr.Zero)
            return false;

        IntPtr remote = IntPtr.Zero;
        try
        {
            POINT client = screenPoint;
            ScreenToClient(list, ref client);
            int size = Marshal.SizeOf<LVHITTESTINFO>();
            remote = VirtualAllocEx(process, IntPtr.Zero, (uint)size, 0x1000, 0x04);
            if (remote == IntPtr.Zero)
                return false;

            var hit = new LVHITTESTINFO { pt = client };
            IntPtr local = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(hit, local, false);
                var buffer = new byte[size];
                Marshal.Copy(local, buffer, 0, size);
                WriteProcessMemory(process, remote, buffer, size, out _);
                SendMessageW(list, 0x1000 + 18, IntPtr.Zero, remote);
                ReadProcessMemory(process, remote, buffer, size, out _);
                Marshal.Copy(buffer, 0, local, size);
                hit = Marshal.PtrToStructure<LVHITTESTINFO>(local);
            }
            finally
            {
                Marshal.FreeHGlobal(local);
            }

            return hit.iItem != -1 || (hit.flags & 0x000E) != 0;
        }
        finally
        {
            if (remote != IntPtr.Zero)
                VirtualFreeEx(process, remote, 0, 0x8000);
            CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint Size;
        public uint Style;
        public WndProc WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO { public int Size; public RECT Monitor; public RECT Work; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr extraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr extraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVITEM
    {
        public uint mask; public int iItem; public int iSubItem; public uint state; public uint stateMask;
        public IntPtr pszText; public int cchTextMax; public int iImage; public IntPtr lParam; public int iIndent;
        public int iGroupId; public uint cColumns; public IntPtr puColumns; public IntPtr piColFmt; public int iGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVHITTESTINFO { public POINT pt; public uint flags; public int iItem; public int iSubItem; public int iGroup; }
}
