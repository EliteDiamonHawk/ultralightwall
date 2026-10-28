using System.ComponentModel;
using System.Runtime.InteropServices;

namespace UltralightWall;

public sealed class DesktopEmbedder
{
    private const string ShellViewClass = "SHELLDLL_DefView";
    private const string WorkerWClass = "WorkerW";
    private const long FrameStyleMask = NativeMethods.WS_CAPTION |
                                         NativeMethods.WS_THICKFRAME |
                                         NativeMethods.WS_MINIMIZEBOX |
                                         NativeMethods.WS_MAXIMIZEBOX |
                                         NativeMethods.WS_SYSMENU;

    private readonly bool _verbose;
    public DesktopEmbedder(bool verbose = false) => _verbose = verbose;

    public static long GetBorderlessChildStyle(long style) =>
        (style & ~(FrameStyleMask | NativeMethods.WS_POPUP)) | NativeMethods.WS_CHILD;

    public static long GetEmbeddedExtendedStyle(long exStyle) =>
        (exStyle & ~NativeMethods.WS_EX_APPWINDOW) |
        NativeMethods.WS_EX_TOOLWINDOW |
        NativeMethods.WS_EX_NOACTIVATE;

    public static long GetMonitorHostStyle() => MonitorHostWindow.Style;

    public static bool HasNativeFrame(long style) => (style & FrameStyleMask) != 0;

    public static bool TryCalculateNonClientInsets(
        Rectangle windowScreenBounds,
        Rectangle clientScreenBounds,
        out NonClientInsets insets)
    {
        insets = default;
        if (windowScreenBounds.Width <= 0 || windowScreenBounds.Height <= 0 ||
            clientScreenBounds.Width <= 0 || clientScreenBounds.Height <= 0 ||
            clientScreenBounds.Left < windowScreenBounds.Left ||
            clientScreenBounds.Top < windowScreenBounds.Top ||
            clientScreenBounds.Right > windowScreenBounds.Right ||
            clientScreenBounds.Bottom > windowScreenBounds.Bottom)
        {
            return false;
        }

        insets = new NonClientInsets(
            clientScreenBounds.Left - windowScreenBounds.Left,
            clientScreenBounds.Top - windowScreenBounds.Top,
            windowScreenBounds.Right - clientScreenBounds.Right,
            windowScreenBounds.Bottom - clientScreenBounds.Bottom);
        return true;
    }

    public static Rectangle CalculateOuterBounds(Rectangle targetClientBounds, NonClientInsets insets) =>
        new(
            targetClientBounds.Left - insets.Left,
            targetClientBounds.Top - insets.Top,
            checked(targetClientBounds.Width + insets.Left + insets.Right),
            checked(targetClientBounds.Height + insets.Top + insets.Bottom));

    public static Rectangle ApplyTopOverscan(Rectangle bounds, int offset)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        return new Rectangle(
            bounds.X,
            checked(bounds.Y - offset),
            bounds.Width,
            checked(bounds.Height + offset));
    }

    public static Rectangle GetVisibleClientBounds(Rectangle clientBounds, Rectangle monitorBounds) =>
        Rectangle.Intersect(clientBounds, monitorBounds);

    public static Rectangle ToParentClient(Rectangle screenBounds, Point parentClientOrigin) =>
        new(screenBounds.Left - parentClientOrigin.X, screenBounds.Top - parentClientOrigin.Y,
            screenBounds.Width, screenBounds.Height);

    public void Embed(AppWindow appWindow) => Reconcile(appWindow);

    public void Reconcile(AppWindow appWindow)
    {
        if (!NativeMethods.IsWindow(appWindow.Hwnd))
            throw new InvalidOperationException($"Window is no longer valid: {appWindow.LogicalId}");

        IntPtr worker = FindDesktopHost();
        if (worker == IntPtr.Zero)
            throw new InvalidOperationException("Could not find the Windows desktop host window.");

        MonitorGeometry geometry = ResolveGeometry(appWindow);
        Rectangle hostBounds = MapRectangleToParent(geometry.Bounds, worker);
        IntPtr previousMonitorHost = appWindow.MonitorHostHwnd;
        IntPtr monitorHost = MonitorHostWindow.Ensure(previousMonitorHost, worker, hostBounds);
        appWindow.MonitorHostHwnd = monitorHost;
        if (!NativeMethods.TryGetWindowRect(monitorHost, out Rectangle actualHostBounds) ||
            actualHostBounds != geometry.Bounds)
        {
            throw new InvalidOperationException(
                $"Monitor host geometry mismatch for {appWindow.LogicalId}: " +
                $"requested {geometry.Bounds}, actual {actualHostBounds}.");
        }

        long currentStyle = GetWindowLongPtrChecked(appWindow.Hwnd, NativeMethods.GWL_STYLE, "read style").ToInt64();
        long currentExStyle = GetWindowLongPtrChecked(appWindow.Hwnd, NativeMethods.GWL_EXSTYLE, "read extended style").ToInt64();
        long desiredStyle = GetBorderlessChildStyle(currentStyle);
        long desiredExStyle = GetEmbeddedExtendedStyle(currentExStyle);
        bool styleChanged = currentStyle != desiredStyle;
        bool exStyleChanged = currentExStyle != desiredExStyle;

        if (styleChanged)
            SetWindowLongPtrChecked(appWindow.Hwnd, NativeMethods.GWL_STYLE, new IntPtr(desiredStyle), "set style");
        if (exStyleChanged)
            SetWindowLongPtrChecked(appWindow.Hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(desiredExStyle), "set extended style");

        if (styleChanged || exStyleChanged)
        {
            if (!NativeMethods.SetWindowPos(appWindow.Hwnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_FRAMECHANGED))
            {
                throw new InvalidOperationException($"Could not recalculate window frame: {FormatWin32Error(Marshal.GetLastPInvokeError())}");
            }
        }

        bool parentChanged = NativeMethods.GetParent(appWindow.Hwnd) != monitorHost;
        if (parentChanged)
        {
            Marshal.SetLastPInvokeError(0);
            IntPtr previousParent = NativeMethods.SetParent(appWindow.Hwnd, monitorHost);
            int parentError = Marshal.GetLastPInvokeError();
            if (previousParent == IntPtr.Zero && parentError != 0)
                throw new InvalidOperationException($"SetParent failed: {FormatWin32Error(parentError)}");
        }

        if (NativeMethods.GetParent(appWindow.Hwnd) != monitorHost)
            throw new InvalidOperationException($"SetParent verification failed for {FormatHwnd(appWindow.Hwnd)}.");

        Rectangle targetClientLocal = new(0, 0, geometry.Bounds.Width, geometry.Bounds.Height);
        if (!TryMeasureWindowGeometry(appWindow.Hwnd, out WindowGeometry actualBeforeGeometry))
            throw new InvalidOperationException(
                $"Could not measure wallpaper client geometry before placement for {appWindow.LogicalId}.");

        NonClientInsets insets = actualBeforeGeometry.Insets;
        Rectangle requestedOuterBounds = CalculateOuterBounds(targetClientLocal, insets);
        Rectangle expectedClientLocalBounds = targetClientLocal;
        Rectangle expectedClientScreenBounds = MapRectangleToScreen(expectedClientLocalBounds, monitorHost);
        bool needsPosition = parentChanged || actualBeforeGeometry.ClientScreenBounds != expectedClientScreenBounds;

        LogGeometry(appWindow, worker, monitorHost, geometry, targetClientLocal, insets,
            requestedOuterBounds, expectedClientScreenBounds, actualBeforeGeometry, null);

        if (needsPosition && !NativeMethods.SetWindowPos(appWindow.Hwnd, NativeMethods.HWND_TOP,
                requestedOuterBounds.X, requestedOuterBounds.Y,
                requestedOuterBounds.Width, requestedOuterBounds.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_NOSENDCHANGING))
        {
            throw new InvalidOperationException(
                $"Could not position wallpaper window: {appWindow.LogicalId}; {FormatWin32Error(Marshal.GetLastPInvokeError())}");
        }

        if (NativeMethods.GetParent(appWindow.Hwnd) != monitorHost)
            throw new InvalidOperationException($"Embedded parent changed unexpectedly for {appWindow.LogicalId}.");
        if (!TryMeasureWindowGeometry(appWindow.Hwnd, out WindowGeometry actualAfterGeometry))
            throw new InvalidOperationException($"Could not verify wallpaper geometry for {appWindow.LogicalId}.");
        Rectangle visibleClientBounds = GetVisibleClientBounds(actualAfterGeometry.ClientScreenBounds, geometry.Bounds);
        if (actualAfterGeometry.ClientScreenBounds != expectedClientScreenBounds || visibleClientBounds != geometry.Bounds)
            throw new InvalidOperationException(
                $"Wallpaper client geometry mismatch for {appWindow.LogicalId}: " +
                $"requested client {expectedClientScreenBounds}, visible client {visibleClientBounds}, " +
                $"actual client {actualAfterGeometry.ClientScreenBounds}, " +
                $"outer {actualAfterGeometry.WindowScreenBounds}.");

        LogGeometry(appWindow, worker, monitorHost, geometry, targetClientLocal, insets,
            requestedOuterBounds, expectedClientScreenBounds, actualBeforeGeometry, actualAfterGeometry);

        if (previousMonitorHost != IntPtr.Zero && previousMonitorHost != monitorHost)
            MonitorHostWindow.Destroy(previousMonitorHost);
    }

    public void Close(AppWindow appWindow)
    {
        MonitorHostWindow.Destroy(appWindow.MonitorHostHwnd);
        appWindow.MonitorHostHwnd = IntPtr.Zero;
    }

    public static IntPtr FindDesktopHost()
    {
        IntPtr progman = NativeMethods.FindWindowW("Progman", null);
        if (progman == IntPtr.Zero)
            return IntPtr.Zero;

        NativeMethods.SendMessageTimeoutW(progman, NativeMethods.WM_SPAWN_WORKERW,
            IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
        IntPtr shellViewInProgman = NativeMethods.FindWindowExW(progman, IntPtr.Zero, ShellViewClass, null);
        if (shellViewInProgman != IntPtr.Zero)
        {
            IntPtr childWorker = NativeMethods.FindWindowExW(progman, IntPtr.Zero, WorkerWClass, null);
            return childWorker != IntPtr.Zero ? childWorker : progman;
        }

        IntPtr worker = IntPtr.Zero;
        NativeMethods.EnumWindows((top, _) =>
        {
            IntPtr shellView = NativeMethods.FindWindowExW(top, IntPtr.Zero, ShellViewClass, null);
            if (shellView == IntPtr.Zero)
                return true;

            worker = NativeMethods.FindWindowExW(IntPtr.Zero, top, WorkerWClass, null);
            return false;
        }, IntPtr.Zero);

        return worker != IntPtr.Zero ? worker : progman;
    }

    private MonitorGeometry ResolveGeometry(AppWindow appWindow)
    {
        if (appWindow.LogicalId == "virtual-screen")
            return new MonitorGeometry(IntPtr.Zero, appWindow.Spec.VirtualBounds, Rectangle.Empty);

        Rectangle requested = appWindow.Spec.Bounds;
        NativeMethods.POINT center = new()
        {
            X = requested.Left + requested.Width / 2,
            Y = requested.Top + requested.Height / 2
        };
        IntPtr monitor = NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            throw new InvalidOperationException($"Could not resolve monitor for {appWindow.LogicalId}.");

        var info = new NativeMethods.MONITORINFO { Size = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(monitor, ref info))
            throw new InvalidOperationException($"Could not read monitor geometry: {FormatWin32Error(Marshal.GetLastPInvokeError())}");

        return new MonitorGeometry(monitor,
            Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
            Rectangle.FromLTRB(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));
    }

    private void LogGeometry(AppWindow appWindow, IntPtr worker, IntPtr monitorHost,
        MonitorGeometry geometry, Rectangle targetClientLocal, NonClientInsets insets,
        Rectangle requestedOuterBounds, Rectangle expectedClientScreenBounds,
        WindowGeometry actualBefore, WindowGeometry? actualAfter)
    {
        if (!_verbose)
            return;

        Rectangle workerRect = NativeMethods.TryGetWindowRect(worker, out Rectangle readWorkerRect)
            ? readWorkerRect : Rectangle.Empty;
        Rectangle hostRect = NativeMethods.TryGetWindowRect(monitorHost, out Rectangle readHostRect)
            ? readHostRect : Rectangle.Empty;
        Rectangle hostClient = GetClientRect(monitorHost);
        long style = GetWindowLongPtrChecked(appWindow.Hwnd, NativeMethods.GWL_STYLE, "read diagnostic style").ToInt64();
        long exStyle = GetWindowLongPtrChecked(appWindow.Hwnd, NativeMethods.GWL_EXSTYLE, "read diagnostic extended style").ToInt64();
        Rectangle wallpaperOuter = actualAfter?.WindowScreenBounds ?? Rectangle.Empty;
        int hostOverscanLeft = wallpaperOuter.IsEmpty || hostRect.IsEmpty ? 0 : hostRect.Left - wallpaperOuter.Left;
        int hostOverscanTop = wallpaperOuter.IsEmpty || hostRect.IsEmpty ? 0 : hostRect.Top - wallpaperOuter.Top;
        int hostOverscanRight = wallpaperOuter.IsEmpty || hostRect.IsEmpty ? 0 : wallpaperOuter.Right - hostRect.Right;
        int hostOverscanBottom = wallpaperOuter.IsEmpty || hostRect.IsEmpty ? 0 : wallpaperOuter.Bottom - hostRect.Bottom;
        Console.WriteLine(
            $"[EmbedCheck] logicalId={appWindow.LogicalId}, hwnd={FormatHwnd(appWindow.Hwnd)}, " +
            $"monitor={FormatHwnd(geometry.Monitor)}, rcMonitor={geometry.Bounds}, rcWork={geometry.Work}, " +
            $"worker={FormatHwnd(worker)}, workerRect={workerRect}, workerClient={GetClientRect(worker)}, " +
            $"monitorHost={FormatHwnd(monitorHost)}, hostParent={FormatHwnd(NativeMethods.GetParent(monitorHost))}, " +
            $"hostRect={hostRect}, hostClient={hostClient}, targetClientLocal={targetClientLocal}, " +
            $"expectedClientScreen={expectedClientScreenBounds}, " +
            $"windowBefore={actualBefore.WindowScreenBounds}, clientLocalBefore={actualBefore.ClientLocalBounds}, " +
            $"clientScreenBefore={actualBefore.ClientScreenBounds}, nonClient={insets}, " +
            $"requestedOuter={requestedOuterBounds}, " +
            $"windowAfter={actualAfter?.WindowScreenBounds}, clientLocalAfter={actualAfter?.ClientLocalBounds}, " +
            $"clientScreenAfter={actualAfter?.ClientScreenBounds}, " +
            $"hostOverscanLeft={hostOverscanLeft}, hostOverscanTop={hostOverscanTop}, " +
            $"hostOverscanRight={hostOverscanRight}, hostOverscanBottom={hostOverscanBottom}, " +
            $"wallpaperParent={FormatHwnd(NativeMethods.GetParent(appWindow.Hwnd))}, " +
            $"style=0x{style:X}, exStyle=0x{exStyle:X}, wallpaperDpi={NativeMethods.GetDpiForWindow(appWindow.Hwnd)}, " +
            $"hostDpi={NativeMethods.GetDpiForWindow(monitorHost)}, workerDpi={NativeMethods.GetDpiForWindow(worker)}");
    }

    private static bool TryMeasureWindowGeometry(IntPtr hwnd, out WindowGeometry geometry)
    {
        geometry = default!;
        if (!NativeMethods.TryGetWindowRect(hwnd, out Rectangle windowScreenBounds) ||
            !NativeMethods.GetClientRect(hwnd, out NativeMethods.RECT clientRect))
        {
            return false;
        }

        NativeMethods.POINT[] clientCorners =
        {
            new() { X = clientRect.Left, Y = clientRect.Top },
            new() { X = clientRect.Right, Y = clientRect.Bottom }
        };
        Marshal.SetLastPInvokeError(0);
        int mapResult = NativeMethods.MapWindowPoints(hwnd, IntPtr.Zero, clientCorners, 2);
        int mapError = Marshal.GetLastPInvokeError();
        if (mapResult == 0 && mapError != 0)
            return false;

        Rectangle clientLocalBounds = Rectangle.FromLTRB(
            clientRect.Left, clientRect.Top, clientRect.Right, clientRect.Bottom);
        Rectangle clientScreenBounds = Rectangle.FromLTRB(
            clientCorners[0].X, clientCorners[0].Y,
            clientCorners[1].X, clientCorners[1].Y);
        if (!TryCalculateNonClientInsets(windowScreenBounds, clientScreenBounds, out NonClientInsets insets))
            return false;

        geometry = new WindowGeometry(windowScreenBounds, clientLocalBounds, clientScreenBounds, insets);
        return true;
    }

    private static Rectangle GetClientRect(IntPtr hwnd)
    {
        if (!NativeMethods.GetClientRect(hwnd, out NativeMethods.RECT rect))
            return Rectangle.Empty;
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    private static Rectangle MapRectangleToParent(Rectangle screenBounds, IntPtr parent)
    {
        try
        {
            return MapRectangle(screenBounds, IntPtr.Zero, parent);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"Could not map monitor bounds into {FormatHwnd(parent)} coordinates.", ex);
        }
    }

    private static Rectangle MapRectangleToScreen(Rectangle localBounds, IntPtr window)
    {
        try
        {
            return MapRectangle(localBounds, window, IntPtr.Zero);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"Could not map client bounds from {FormatHwnd(window)} to screen coordinates.", ex);
        }
    }

    private static Rectangle MapRectangle(Rectangle bounds, IntPtr from, IntPtr to)
    {
        NativeMethods.POINT[] points =
        {
            new() { X = bounds.Left, Y = bounds.Top },
            new() { X = bounds.Right, Y = bounds.Bottom }
        };

        Marshal.SetLastPInvokeError(0);
        int mapResult = NativeMethods.MapWindowPoints(from, to, points, 2);
        int mapError = Marshal.GetLastPInvokeError();
        if (mapResult == 0 && mapError != 0)
            throw new InvalidOperationException(FormatWin32Error(mapError));

        return Rectangle.FromLTRB(
            points[0].X,
            points[0].Y,
            points[1].X,
            points[1].Y);
    }

    private static string FormatHwnd(IntPtr hwnd) => hwnd == IntPtr.Zero ? "0x0" : $"0x{hwnd.ToInt64():X}";

    private static IntPtr GetWindowLongPtrChecked(IntPtr hwnd, int index, string operation)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr value = NativeMethods.GetWindowLongPtrW(hwnd, index);
        int error = Marshal.GetLastPInvokeError();
        if (value == IntPtr.Zero && error != 0)
            throw new InvalidOperationException($"Could not {operation} for {FormatHwnd(hwnd)}: {FormatWin32Error(error)}");
        return value;
    }

    private static void SetWindowLongPtrChecked(IntPtr hwnd, int index, IntPtr value, string operation)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr previous = NativeMethods.SetWindowLongPtrW(hwnd, index, value);
        int error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0)
            throw new InvalidOperationException($"Could not {operation} for {FormatHwnd(hwnd)}: {FormatWin32Error(error)}");

        IntPtr actual = GetWindowLongPtrChecked(hwnd, index, $"verify {operation}");
        if (actual != value)
            throw new InvalidOperationException(
                $"{operation} verification failed for {FormatHwnd(hwnd)}: requested 0x{value.ToInt64():X}, got 0x{actual.ToInt64():X}.");
    }

    private static string FormatWin32Error(int error) =>
        error == 0 ? "no Win32 error" : $"Win32 error {error}: {new Win32Exception(error).Message}";

    private sealed record WindowGeometry(
        Rectangle WindowScreenBounds,
        Rectangle ClientLocalBounds,
        Rectangle ClientScreenBounds,
        NonClientInsets Insets);

    private sealed record MonitorGeometry(IntPtr Monitor, Rectangle Bounds, Rectangle Work);
}

public readonly record struct NonClientInsets(int Left, int Top, int Right, int Bottom);
