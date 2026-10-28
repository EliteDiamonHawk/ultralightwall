namespace UltralightWall;

public sealed class UltralightRuntime : IDisposable
{
    private string _assetsPath = "";
    private IntPtr _app;

    public UltralightRuntime()
    {
        string stage = "runtime file validation";
        try
        {
            Initialize(out stage);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Ultralight initialization failed at {stage}: {ex.Message}", ex);
        }
    }

    private void Initialize(out string stage)
    {
        stage = "runtime file validation";
        string basePath = AppContext.BaseDirectory;
        foreach (string library in new[] { "AppCore.dll", "Ultralight.dll", "UltralightCore.dll", "WebCore.dll" })
        {
            string path = Path.Combine(basePath, library);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Ultralight runtime file is missing: {path}", path);
        }

        _assetsPath = Path.Combine(basePath, "assets");
        if (!Directory.Exists(_assetsPath))
            throw new DirectoryNotFoundException($"Ultralight assets directory is missing: {_assetsPath}");

        stage = "AppCore creation";
        _app = UltralightNative.ulCreateApp(IntPtr.Zero, IntPtr.Zero);

        if (_app == IntPtr.Zero)
            throw new InvalidOperationException("Ultralight AppCore could not be initialized.");
    }

    public AppWindow CreateWindow(WallpaperWindowSpec spec, TimeSpan timeout)
    {
        IntPtr monitor = ResolveMonitor(spec.LogicalId);
        double scale = Math.Max(0.01, UltralightNative.ulMonitorGetScale(monitor));
        double width = Math.Max(1, Math.Ceiling(spec.Bounds.Width / scale));
        double height = Math.Max(1, Math.Ceiling(spec.Bounds.Height / scale));
        IntPtr window = UltralightNative.ulCreateWindow(
            monitor,
            width,
            height,
            false,
            UltralightNative.WindowFlagsBorderless | UltralightNative.WindowFlagsHidden);

        if (window == IntPtr.Zero)
            throw new InvalidOperationException($"Ultralight window could not be created: {spec.LogicalId}");

        IntPtr panel = IntPtr.Zero;
        IntPtr view = IntPtr.Zero;
        try
        {
            IntPtr hwnd = UltralightNative.ulWindowGetNativeHandle(window);
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException($"Ultralight window has no native handle: {spec.LogicalId}");

            UltralightNative.ulWindowSetTitle(window, $"UltralightWall - {spec.LogicalId}");
            IntPtr viewConfig = UltralightNative.ulCreateViewConfig();
            if (viewConfig == IntPtr.Zero)
                throw new InvalidOperationException($"Ultralight view configuration could not be created: {spec.LogicalId}");

            try
            {
                UltralightNative.ulViewConfigSetEnableHiddenTimerThrottling(viewConfig, true);
                panel = UltralightNative.ulWindowAddPanel(window, IntPtr.Zero, viewConfig);
            }
            finally
            {
                UltralightNative.ulDestroyViewConfig(viewConfig);
            }

            if (panel == IntPtr.Zero)
                throw new InvalidOperationException($"Ultralight panel could not be created: {spec.LogicalId}");

            view = UltralightNative.ulPanelGetView(panel);
            if (view == IntPtr.Zero)
                throw new InvalidOperationException($"Ultralight view could not be created: {spec.LogicalId}");

            IntPtr url = CreateString(spec.Url);
            try
            {
                UltralightNative.ulViewLoadURL(view, url);
            }
            finally
            {
                DestroyString(url);
            }

            return new AppWindow(spec, timeout, window, panel, view, hwnd);
        }
        catch
        {
            DestroyWindow(window, panel, view);
            throw;
        }
    }

    public void ShowWhenReady(AppWindow appWindow, TimeSpan timeout) =>
        UltralightNative.ulWindowShowWhenReady(appWindow.UlWindow, Math.Max(0, timeout.TotalSeconds));

    public bool Pump() => _app != IntPtr.Zero && UltralightNative.ulAppRunOnce(_app, 0);

    public bool TrySetSuspended(AppWindow appWindow, bool suspended)
    {
        if (_app == IntPtr.Zero || appWindow.UlView == IntPtr.Zero ||
            appWindow.State is AppWindowState.Closed or AppWindowState.Failed)
        {
            return false;
        }

        try
        {
            UltralightNative.ulViewSetVisible(appWindow.UlView, !suspended);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Suspension] transition failed for {appWindow.LogicalId}: {ex.Message}");
            return false;
        }
    }

    public void DestroyWindow(AppWindow appWindow)
    {
        DestroyWindow(appWindow.UlWindow, appWindow.UlPanel, appWindow.UlView);
        appWindow.UlWindow = IntPtr.Zero;
        appWindow.UlPanel = IntPtr.Zero;
        appWindow.UlView = IntPtr.Zero;
        appWindow.Hwnd = IntPtr.Zero;
        appWindow.RendererSuspended = false;
        appWindow.State = AppWindowState.Closed;
    }

    public void Dispose()
    {
        if (_app == IntPtr.Zero)
            return;

        UltralightNative.ulDestroyApp(_app);
        _app = IntPtr.Zero;
    }

    private IntPtr ResolveMonitor(string logicalId)
    {
        if (logicalId == "virtual-screen")
            return UltralightNative.ulAppGetMainMonitor(_app);

        if (logicalId.StartsWith("monitor-", StringComparison.Ordinal) &&
            uint.TryParse(logicalId[8..], out uint oneBased))
        {
            IntPtr monitor = UltralightNative.ulAppGetMonitor(_app, oneBased - 1);
            if (monitor != IntPtr.Zero)
                return monitor;
        }

        return UltralightNative.ulAppGetMainMonitor(_app);
    }

    private static IntPtr CreateString(string value) => UltralightNative.ulCreateString(value);

    private static void DestroyString(IntPtr value)
    {
        if (value != IntPtr.Zero)
            UltralightNative.ulDestroyString(value);
    }

    private static void DestroyWindow(IntPtr window, IntPtr panel, IntPtr view)
    {
        if (window == IntPtr.Zero)
            return;

        UltralightNative.ulWindowClose(window);
        if (view != IntPtr.Zero)
            UltralightNative.ulDestroyView(view);
        if (panel != IntPtr.Zero)
            UltralightNative.ulDestroyPanel(panel);
        UltralightNative.ulDestroyWindow(window);
    }
}

public sealed class AppWindow
{
    public AppWindow(WallpaperWindowSpec spec, TimeSpan timeout)
        : this(spec, timeout, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero)
    {
    }

    internal AppWindow(
        WallpaperWindowSpec spec,
        TimeSpan timeout,
        IntPtr ulWindow,
        IntPtr ulPanel,
        IntPtr ulView,
        IntPtr hwnd)
    {
        LogicalId = spec.LogicalId;
        Url = spec.Url;
        Spec = spec;
        StartedAt = DateTimeOffset.UtcNow;
        DiscoveryTimeout = timeout;
        Deadline = StartedAt + timeout;
        UlWindow = ulWindow;
        UlPanel = ulPanel;
        UlView = ulView;
        Hwnd = hwnd;
    }

    public string LogicalId { get; }
    public string Url { get; }
    public WallpaperWindowSpec Spec { get; }
    public DateTimeOffset StartedAt { get; }
    public TimeSpan DiscoveryTimeout { get; }
    public DateTimeOffset Deadline { get; internal set; }
    public IntPtr Hwnd { get; internal set; }
    public IntPtr MonitorHostHwnd { get; internal set; }
    public IntPtr MonitorHandle { get; internal set; }
    public bool FullscreenOccluded { get; internal set; }
    public bool DesiredSuspended { get; internal set; }
    public bool RendererSuspended { get; internal set; }
    public AppWindowState State { get; internal set; } = AppWindowState.Pending;
    public WindowCloseIntent CloseIntent { get; internal set; } = WindowCloseIntent.None;
    public string? FailureReason { get; internal set; }
    internal IntPtr UlWindow { get; set; }
    internal IntPtr UlPanel { get; set; }
    internal IntPtr UlView { get; set; }

    public bool IsExpired(DateTimeOffset now) => now >= Deadline;
}

public enum AppWindowState
{
    Pending,
    Embedded,
    Closed,
    Failed
}

public enum WindowCloseIntent
{
    None,
    Intentional,
    Unexpected
}

public static class CloseCheck
{
    public static bool AllClosed(IEnumerable<AppWindow> windows) =>
        windows.Any() && windows.All(window => window.State is AppWindowState.Closed or AppWindowState.Failed);

    public static bool IsIntentional(IEnumerable<AppWindow> windows) =>
        windows.Any() && windows.All(window => window.CloseIntent == WindowCloseIntent.Intentional);
}
