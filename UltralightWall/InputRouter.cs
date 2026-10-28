using System.Runtime.InteropServices;
using System.Text;

namespace UltralightWall;

public sealed class WallpaperWindow
{
    public WallpaperWindow(AppWindow appWindow) => AppWindow = appWindow;
    public AppWindow AppWindow { get; }
    public Rectangle Bounds => AppWindow.Spec.Bounds;
}

public sealed class InputRouter : IDisposable
{
    private readonly Func<IReadOnlyList<WallpaperWindow>> _windows;
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private IntPtr _keyboardTarget;
    private bool _wallpaperFocused;
    private bool _interactingWithDesktop;

    public InputRouter(Func<IReadOnlyList<WallpaperWindow>> windows)
    {
        _windows = windows;
        _mouseProc = MouseHookCallback;
        _keyboardProc = KeyboardHookCallback;

        IntPtr module = NativeMethods.GetModuleHandleW(null);
        _mouseHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);

        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
            Console.Error.WriteLine("[Input] Could not install one or more global input hooks.");
    }

    public void Dispose()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0)
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);

        int message = (int)wParam;
        NativeMethods.MSLLHOOKSTRUCT info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        NativeMethods.POINT point = info.pt;

        if (_interactingWithDesktop)
        {
            if (message is NativeMethods.WM_LBUTTONUP or NativeMethods.WM_RBUTTONUP)
                _interactingWithDesktop = false;
            return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        IntPtr underPointer = NativeMethods.WindowFromPoint(point);
        bool desktopSurface = IsDesktopSurface(underPointer);
        WallpaperWindow? target = FindWallpaperWindow(point);

        if (desktopSurface)
        {
            if (message is NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP)
                return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);

            if (message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONDBLCLK)
            {
                if (IsDesktopIcon(underPointer, point))
                {
                    _interactingWithDesktop = true;
                    return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
                }

                _wallpaperFocused = true;
                NativeMethods.TryClearDesktopSelection(underPointer);
                CloseDesktopMenus();
            }

            if (message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN)
            {
                _wallpaperFocused = true;
                if (target != null)
                    _keyboardTarget = ResolveInputWindow(target, point);
            }

            if (target != null && message != NativeMethods.WM_RBUTTONDOWN && message != NativeMethods.WM_RBUTTONUP)
            {
                ForwardMouse(target, message, point, info.mouseData);
                return message == NativeMethods.WM_MOUSEMOVE
                    ? NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam)
                    : (IntPtr)1;
            }
        }
        else if (target != null && NativeMethods.IsDescendantOrSelf(underPointer, target.AppWindow.Hwnd))
        {
            if (message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN)
            {
                _wallpaperFocused = true;
                _keyboardTarget = ResolveInputWindow(target, point);
            }

            if (message != NativeMethods.WM_RBUTTONDOWN && message != NativeMethods.WM_RBUTTONUP)
            {
                ForwardMouse(target, message, point, info.mouseData);
                return message == NativeMethods.WM_MOUSEMOVE
                    ? NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam)
                    : (IntPtr)1;
            }
        }
        else if (message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN)
        {
            _wallpaperFocused = false;
        }

        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _wallpaperFocused && _keyboardTarget != IntPtr.Zero && NativeMethods.IsWindow(_keyboardTarget))
        {
            int message = (int)wParam;
            if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_KEYUP or
                NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_SYSKEYUP)
            {
                NativeMethods.KBDLLHOOKSTRUCT info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int key = unchecked((int)info.vkCode);
                bool keyDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;

                if (key is 0x5B or 0x5C or 0x2C || key == 0x09 && (NativeMethods.GetKeyState(0x12) & 0x8000) != 0)
                {
                    PostKey(message, key, info);
                    return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
                }

                if (key is 0x10 or 0x11 or 0x12 or 0x14 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)
                {
                    PostKey(message, key, info);
                    return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
                }

                if (keyDown || message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    PostKey(message, key, info);
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void ForwardMouse(WallpaperWindow window, int message, NativeMethods.POINT screenPoint, uint mouseData)
    {
        IntPtr target = ResolveInputWindow(window, screenPoint);
        if (target == IntPtr.Zero)
            return;

        NativeMethods.POINT clientPoint = screenPoint;
        NativeMethods.ScreenToClient(target, ref clientPoint);
        IntPtr lParam = new((clientPoint.Y << 16) | (clientPoint.X & 0xFFFF));
        int keyState = GetMouseKeyState(message);
        IntPtr wParam = message is NativeMethods.WM_MOUSEWHEEL or NativeMethods.WM_MOUSEHWHEEL
            ? new IntPtr((unchecked((short)(mouseData >> 16)) << 16) | keyState)
            : new IntPtr(keyState);
        NativeMethods.PostMessageW(target, message, wParam, lParam);
    }

    private void PostKey(int message, int key, NativeMethods.KBDLLHOOKSTRUCT info)
    {
        int flags = (int)info.flags;
        int lParam = 1 | ((int)info.scanCode << 16);
        if ((flags & 0x80) != 0)
            lParam |= unchecked((int)0xC0000000);
        if ((flags & 0x20) != 0)
            lParam |= 1 << 24;
        NativeMethods.PostMessageW(_keyboardTarget, message, new IntPtr(key), new IntPtr(lParam));
    }

    private WallpaperWindow? FindWallpaperWindow(NativeMethods.POINT point)
    {
        return _windows()
            .Where(window => NativeMethods.IsWindow(window.AppWindow.Hwnd) && window.Bounds.Contains(point.X, point.Y))
            .OrderBy(window => window.Bounds.Width * (long)window.Bounds.Height)
            .FirstOrDefault();
    }

    private static IntPtr ResolveInputWindow(WallpaperWindow window, NativeMethods.POINT screenPoint)
    {
        IntPtr candidate = NativeMethods.WindowFromPoint(screenPoint);
        return candidate != IntPtr.Zero && NativeMethods.IsDescendantOrSelf(candidate, window.AppWindow.Hwnd)
            ? candidate
            : window.AppWindow.Hwnd;
    }

    private static int GetMouseKeyState(int message)
    {
        int state = 0;
        if ((NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0) state |= 0x0001;
        if ((NativeMethods.GetAsyncKeyState(0x02) & 0x8000) != 0) state |= 0x0002;
        if ((NativeMethods.GetAsyncKeyState(0x04) & 0x8000) != 0) state |= 0x0010;
        if ((NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0) state |= 0x0004;
        if ((NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0) state |= 0x0008;

        if (message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP or NativeMethods.WM_LBUTTONDBLCLK) state |= 0x0001;
        if (message is NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP) state |= 0x0002;
        if (message is NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_MBUTTONUP) state |= 0x0010;
        return state;
    }

    private static bool IsDesktopSurface(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        IntPtr root = NativeMethods.GetAncestor(hwnd, 2);
        if (root == IntPtr.Zero)
            root = hwnd;

        var className = new StringBuilder(256);
        NativeMethods.GetClassNameW(root, className, className.Capacity);
        return className.ToString() is "Progman" or "WorkerW" or "SysListView32";
    }

    private static bool IsDesktopIcon(IntPtr hwnd, NativeMethods.POINT point)
    {
        var className = new StringBuilder(256);
        NativeMethods.GetClassNameW(hwnd, className, className.Capacity);
        if (className.ToString() != "SysListView32")
            return false;
        return NativeMethods.IsDesktopIconAt(hwnd, point);
    }

    private static void CloseDesktopMenus()
    {
        NativeMethods.PostMessageW(NativeMethods.GetShellWindow(), NativeMethods.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
        IntPtr menu = NativeMethods.FindWindowW("#32768", null);
        if (menu != IntPtr.Zero && NativeMethods.IsWindowVisible(menu))
        {
            NativeMethods.PostMessageW(menu, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            NativeMethods.PostMessageW(menu, NativeMethods.WM_KEYDOWN, new IntPtr(0x1B), IntPtr.Zero);
        }
    }
}
