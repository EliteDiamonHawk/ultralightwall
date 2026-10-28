using System.Runtime.InteropServices;

namespace UltralightWall;

public sealed class WallpaperHostContext : ApplicationContext
{
    private readonly HostOptions _options;
    private readonly UltralightRuntime _runtime;
    private readonly DesktopEmbedder _embedder;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _fullscreenDebounceTimer;
    private readonly System.Windows.Forms.Timer _fullscreenFallbackTimer;
    private readonly Control _eventDispatcher = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly InputRouter _input;
    private readonly List<ToolStripMenuItem> _monitorItems = new();
    private readonly List<ToolStripMenuItem> _reloadItems = new();
    private readonly List<IntPtr> _winEventHooks = new();

    private WindowRegistry _registry = new();
    private List<WallpaperWindow> _windows = new();
    private ReplacementBatch? _pending;
    private string _baseUrl;
    private readonly Dictionary<int, string> _monitorUrls;
    private WallpaperMode _mode;
    private int _selectedMonitor;
    private bool _exiting;
    private bool _closeCheckLogged;
    private bool _allWindowsMissingLogged;
    private string _topologySignature;
    private DateTimeOffset _nextReconcile = DateTimeOffset.MinValue;
    private ToolStripMenuItem? _spanItem;
    private ToolStripMenuItem? _allItem;
    private ToolStripMenuItem? _pauseWhenFullscreenItem;
    private ToolStripMenuItem? _suspendWallpaperItem;
    private bool _manualSuspendEnabled;
    private bool _pauseWhenFullscreenEnabled = true;
    private bool _fullscreenMonitoringStarted;
    private bool _fullscreenMonitoringDisposed;
    private int _fullscreenReevaluationPosted;
    private bool _suspensionUpdateRunning;
    private bool _suspensionUpdatePending;
    private IntPtr _fullscreenMonitor;
    private NativeMethods.WinEventDelegate? _winEventProc;
    private bool _timerStarted;

    private const int ActivePumpIntervalMs = 16;
    private const int SuspendedPumpIntervalMs = 1000;

    public WallpaperHostContext(HostOptions options)
    {
        _options = options;
        _runtime = new UltralightRuntime();
        _embedder = new DesktopEmbedder(options.Verbose);
        _baseUrl = options.Url;
        _monitorUrls = new Dictionary<int, string>(options.MonitorUrls);
        _mode = options.Mode;
        _selectedMonitor = options.SelectedMonitor;
        _topologySignature = GetTopologySignature();

        _menu = new ContextMenuStrip();
        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "UltralightWall",
            Visible = true,
            ContextMenuStrip = _menu
        };
        BuildMenu();

        _eventDispatcher.CreateControl();
        _fullscreenDebounceTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _fullscreenDebounceTimer.Tick += (_, _) =>
        {
            _fullscreenDebounceTimer.Stop();
            ReconcileFullscreenState();
        };
        _fullscreenFallbackTimer = new System.Windows.Forms.Timer { Interval = 7500 };
        _fullscreenFallbackTimer.Tick += (_, _) => QueueFullscreenReevaluation();
        _winEventProc = WinEventCallback;
        InstallWinEventHooks();
        _fullscreenMonitoringStarted = true;
        _fullscreenFallbackTimer.Start();
        ReconcileFullscreenState();

        _input = new InputRouter(() => _windows);
        _timer = new System.Windows.Forms.Timer { Interval = ActivePumpIntervalMs };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _timerStarted = true;

        BeginReplacement(options.ResolveWindowSpecs(_baseUrl, _mode, _selectedMonitor, _monitorUrls), Array.Empty<WallpaperWindow>());
    }

    private void Tick()
    {
        if (_exiting)
            return;

        try
        {
            _runtime.Pump();
            PollWindows();
            UpdatePumpCadence();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Runtime] tick failed: {ex.Message}");
        }
    }

    private void BuildMenu()
    {
        _menu.Items.Clear();
        _monitorItems.Clear();
        _reloadItems.Clear();

        _menu.Items.Add(new ToolStripMenuItem("Set base URL...", null, (_, _) => AskForBaseUrl()));

        var monitorUrlMenu = new ToolStripMenuItem("Set monitor URL...");
        for (int index = 0; index < Screen.AllScreens.Length; index++)
        {
            int monitor = index;
            monitorUrlMenu.DropDownItems.Add(new ToolStripMenuItem($"Monitor {index + 1}", null, (_, _) => AskForMonitorUrl(monitor)));
        }
        _menu.Items.Add(monitorUrlMenu);
        _menu.Items.Add(new ToolStripSeparator());

        _spanItem = new ToolStripMenuItem("Span (virtual screen)", null, (_, _) => SetMode(WallpaperMode.Span));
        _allItem = new ToolStripMenuItem("All monitors", null, (_, _) => SetMode(WallpaperMode.All));
        _menu.Items.Add(_spanItem);
        _menu.Items.Add(_allItem);

        var singleMenu = new ToolStripMenuItem("Single monitor");
        for (int index = 0; index < Screen.AllScreens.Length; index++)
        {
            int monitor = index;
            var item = new ToolStripMenuItem($"Monitor {index + 1}", null, (_, _) => SetSingleMonitor(monitor));
            _monitorItems.Add(item);
            singleMenu.DropDownItems.Add(item);
        }
        _menu.Items.Add(singleMenu);
        _menu.Items.Add(new ToolStripSeparator());

        _pauseWhenFullscreenItem = new ToolStripMenuItem("Pause when fullscreen")
        {
            CheckOnClick = true,
            Checked = _pauseWhenFullscreenEnabled
        };
        _pauseWhenFullscreenItem.Click += (_, _) =>
        {
            _pauseWhenFullscreenEnabled = _pauseWhenFullscreenItem.Checked;
            if (_pauseWhenFullscreenEnabled)
                QueueFullscreenReevaluation();
            else
            {
                _fullscreenMonitor = IntPtr.Zero;
                ClearFullscreenOcclusion();
                RequestWallpaperSuspensionUpdate();
            }
        };
        _menu.Items.Add(_pauseWhenFullscreenItem);

        _suspendWallpaperItem = new ToolStripMenuItem("Suspend wallpaper")
        {
            CheckOnClick = true,
            Checked = _manualSuspendEnabled
        };
        _suspendWallpaperItem.Click += (_, _) =>
        {
            _manualSuspendEnabled = _suspendWallpaperItem.Checked;
            RequestWallpaperSuspensionUpdate();
        };
        _menu.Items.Add(_suspendWallpaperItem);

        var reloadMenu = new ToolStripMenuItem("Reload");
        reloadMenu.DropDownItems.Add(new ToolStripMenuItem("All windows", null, (_, _) => ReloadAll()));
        foreach (WallpaperWindow window in _windows)
        {
            WallpaperWindow selected = window;
            var item = new ToolStripMenuItem(selected.AppWindow.LogicalId, null, (_, _) => ReloadOne(selected));
            _reloadItems.Add(item);
            reloadMenu.DropDownItems.Add(item);
        }
        _menu.Items.Add(reloadMenu);
        _menu.Items.Add(new ToolStripMenuItem("Print status", null, (_, _) => PrintStatus()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitHost()));
        UpdateChecks();
    }

    private void PollWindows()
    {
        if (_pending != null)
        {
            PollPending();
            return;
        }

        if (GetTopologySignature() != _topologySignature)
        {
            _topologySignature = GetTopologySignature();
            BeginReplacement(_options.ResolveWindowSpecs(_baseUrl, _mode, _selectedMonitor, _monitorUrls), _windows);
            return;
        }

        if (DateTimeOffset.UtcNow < _nextReconcile)
            return;
        _nextReconcile = DateTimeOffset.UtcNow.AddMilliseconds(_options.ReconcileIntervalMs);

        foreach (WallpaperWindow window in _windows.ToArray())
        {
            AppWindow appWindow = window.AppWindow;
            if (appWindow.State != AppWindowState.Embedded)
                continue;

            if (!NativeMethods.IsWindow(appWindow.Hwnd))
            {
                _registry.MarkUnexpectedClose(appWindow);
                Console.Error.WriteLine($"[Lifecycle] Ultralight window lost: {appWindow.LogicalId}");
                BeginReplacement(_options.ResolveWindowSpecs(_baseUrl, _mode, _selectedMonitor, _monitorUrls), _windows);
                return;
            }

            try
            {
                _embedder.Reconcile(appWindow);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Embed] reconciliation failed for {appWindow.LogicalId}: {ex.Message}");
            }
        }

        LogAllWindowsMissingIfNeeded();
        CheckForAllWindowsClosed();
    }

    private void PollPending()
    {
        ReplacementBatch pending = _pending!;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (AppWindow appWindow in pending.Registry.Items.ToArray())
        {
            if (appWindow.State != AppWindowState.Pending)
                continue;

            if (appWindow.IsExpired(now))
            {
                pending.Registry.MarkFailed(appWindow, "window did not embed before startup timeout");
                Console.Error.WriteLine($"[Embed] timeout: {appWindow.LogicalId}");
                continue;
            }

            if (!NativeMethods.IsWindow(appWindow.Hwnd))
            {
                pending.Registry.MarkFailed(appWindow, "native window handle became invalid");
                continue;
            }

            try
            {
                _embedder.Embed(appWindow);
                pending.Registry.MarkEmbedded(appWindow);
                RequestWallpaperSuspensionUpdate();
                _runtime.ShowWhenReady(appWindow, _options.StartupTimeout);
                Console.WriteLine($"[Ultralight] embedded {appWindow.LogicalId} -> {FormatHwnd(appWindow.Hwnd)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Embed] failed for {appWindow.LogicalId}: {ex.Message}");
            }
        }

        if (pending.Registry.Items.Any(window => window.State == AppWindowState.Failed))
        {
            AbortPending();
            return;
        }

        if (pending.Registry.Items.All(window => window.State == AppWindowState.Embedded))
            CommitPending();
    }

    private void BeginReplacement(IReadOnlyList<WallpaperWindowSpec> specs, IReadOnlyList<WallpaperWindow> targets)
    {
        if (_pending != null)
        {
            Console.WriteLine("[Replace] request ignored while another replacement is pending.");
            return;
        }

        ReconcileFullscreenState();

        var registry = new WindowRegistry();
        var windows = new List<WallpaperWindow>();
        try
        {
            foreach (WallpaperWindowSpec spec in specs)
            {
                AppWindow appWindow = _runtime.CreateWindow(spec, _options.StartupTimeout);
                appWindow.MonitorHandle = ResolveWallpaperMonitor(spec);
                registry.Add(appWindow);
                windows.Add(new WallpaperWindow(appWindow));
                Console.WriteLine($"[Ultralight] created {spec.LogicalId}: {spec.Url}");
            }
        }
        catch (Exception ex)
        {
            foreach (WallpaperWindow window in windows)
                _runtime.DestroyWindow(window.AppWindow);
            Console.Error.WriteLine($"[Replace] creation failed: {ex.Message}");
            return;
        }

        _pending = new ReplacementBatch(registry, windows, targets.ToArray());
        RequestWallpaperSuspensionUpdate();
    }

    private void CommitPending()
    {
        ReplacementBatch pending = _pending!;
        foreach (WallpaperWindow oldWindow in pending.Targets)
            CloseTracked(oldWindow);

        var remaining = _windows.Where(window => !pending.Targets.Contains(window)).ToList();
        var mergedRegistry = new WindowRegistry();
        foreach (AppWindow oldWindow in _registry.Items.Where(window => pending.Targets.All(target => !ReferenceEquals(target.AppWindow, window))))
            mergedRegistry.Add(oldWindow);
        foreach (AppWindow newWindow in pending.Registry.Items)
            mergedRegistry.Add(newWindow);

        _windows = remaining.Concat(pending.Windows).ToList();
        _registry = mergedRegistry;
        _pending = null;
        _closeCheckLogged = false;
        _allWindowsMissingLogged = false;
        BuildMenu();
        Console.WriteLine("[Replace] committed new Ultralight windows.");
    }

    private void AbortPending()
    {
        ReplacementBatch pending = _pending!;
        foreach (AppWindow appWindow in pending.Registry.Items)
        {
            pending.Registry.MarkFailed(appWindow, appWindow.FailureReason ?? "replacement aborted");
            _runtime.DestroyWindow(appWindow);
        }

        _pending = null;
        Console.Error.WriteLine("[Replace] aborted; existing windows retained; host remains running.");
    }

    private void SetMode(WallpaperMode mode)
    {
        if (_mode == mode)
            return;
        _mode = mode;
        BeginReplacement(_options.ResolveWindowSpecs(_baseUrl, _mode, _selectedMonitor, _monitorUrls), _windows);
        UpdateChecks();
    }

    private void SetSingleMonitor(int monitor)
    {
        _selectedMonitor = monitor;
        _mode = WallpaperMode.Single;
        BeginReplacement(_options.ResolveWindowSpecs(_baseUrl, _mode, _selectedMonitor, _monitorUrls), _windows);
        UpdateChecks();
    }

    private void AskForBaseUrl()
    {
        using var input = new InputBox("Set base URL", "Enter URL:", _baseUrl);
        if (input.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            _baseUrl = UrlBuilder.Normalize(input.InputText);
            BeginReplacement(_options.ResolveWindowSpecs(_baseUrl, _mode, _selectedMonitor, _monitorUrls), _windows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "UltralightWall - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AskForMonitorUrl(int monitor)
    {
        string current = _monitorUrls.TryGetValue(monitor, out string? url) ? url : _baseUrl;
        using var input = new InputBox($"Set monitor {monitor + 1} URL", "Enter URL:", current);
        if (input.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            _monitorUrls[monitor] = UrlBuilder.Normalize(input.InputText);
            BeginReplacement(_options.ResolveWindowSpecs(_baseUrl, _mode, _selectedMonitor, _monitorUrls), _windows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "UltralightWall - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReloadAll() =>
        BeginReplacement(_options.ResolveWindowSpecs(_baseUrl, _mode, _selectedMonitor, _monitorUrls), _windows);

    private void ReloadOne(WallpaperWindow window)
    {
        var spec = new WallpaperWindowSpec(
            window.AppWindow.LogicalId,
            window.AppWindow.Spec.Url,
            window.AppWindow.Spec.Bounds,
            window.AppWindow.Spec.VirtualBounds);
        BeginReplacement(new[] { spec }, new[] { window });
    }

    private void PrintStatus()
    {
        foreach (AppWindow appWindow in _registry.Items)
        {
            Console.WriteLine($"[Status] {appWindow.LogicalId}: {appWindow.State}, " +
                $"HWND={FormatHwnd(appWindow.Hwnd)}, host={FormatHwnd(appWindow.MonitorHostHwnd)}, " +
                $"monitor={FormatHwnd(appWindow.MonitorHandle)}, fullscreen={appWindow.FullscreenOccluded}, " +
                $"suspended={appWindow.RendererSuspended}, url={appWindow.Spec.Url}");
        }
    }

    private void UpdateChecks()
    {
        if (_spanItem != null) _spanItem.Checked = _mode == WallpaperMode.Span;
        if (_allItem != null) _allItem.Checked = _mode == WallpaperMode.All;
        if (_pauseWhenFullscreenItem != null) _pauseWhenFullscreenItem.Checked = _pauseWhenFullscreenEnabled;
        if (_suspendWallpaperItem != null) _suspendWallpaperItem.Checked = _manualSuspendEnabled;
        for (int index = 0; index < _monitorItems.Count; index++)
            _monitorItems[index].Checked = _mode == WallpaperMode.Single && index == _selectedMonitor;
    }

    private IEnumerable<AppWindow> KnownWindows()
    {
        foreach (WallpaperWindow window in _windows)
            yield return window.AppWindow;

        if (_pending == null)
            yield break;

        foreach (WallpaperWindow window in _pending.Windows)
        {
            if (_windows.All(existing => !ReferenceEquals(existing.AppWindow, window.AppWindow)))
                yield return window.AppWindow;
        }
    }

    private void RequestWallpaperSuspensionUpdate()
    {
        if (_exiting)
            return;

        if (_suspensionUpdateRunning)
        {
            _suspensionUpdatePending = true;
            return;
        }

        _suspensionUpdateRunning = true;
        try
        {
            do
            {
                _suspensionUpdatePending = false;

                foreach (AppWindow appWindow in KnownWindows().ToArray())
                {
                    if (appWindow.State is AppWindowState.Closed or AppWindowState.Failed)
                        continue;

                    appWindow.FullscreenOccluded =
                        _pauseWhenFullscreenEnabled &&
                        appWindow.MonitorHandle != IntPtr.Zero &&
                        appWindow.MonitorHandle == _fullscreenMonitor;

                    bool shouldSuspend = WallpaperSuspensionPolicy.ShouldSuspend(
                        _manualSuspendEnabled,
                        _pauseWhenFullscreenEnabled,
                        appWindow.FullscreenOccluded);
                    appWindow.DesiredSuspended = shouldSuspend;

                    if (shouldSuspend == appWindow.RendererSuspended)
                        continue;

                    if (_runtime.TrySetSuspended(appWindow, shouldSuspend))
                        appWindow.RendererSuspended = shouldSuspend;
                }
            }
            while (_suspensionUpdatePending && !_exiting);
        }
        finally
        {
            _suspensionUpdateRunning = false;
            UpdatePumpCadence();
            if (_suspensionUpdatePending && !_exiting)
                RequestWallpaperSuspensionUpdate();
        }
    }

    private void UpdatePumpCadence()
    {
        bool activeWindowExists = KnownWindows().Any(window =>
            window.State is not (AppWindowState.Closed or AppWindowState.Failed) &&
            !window.RendererSuspended);
        int desiredInterval = activeWindowExists ? ActivePumpIntervalMs : SuspendedPumpIntervalMs;
        if (!_timerStarted || _timer.Interval == desiredInterval)
            return;

        _timer.Interval = desiredInterval;
        _timer.Stop();
        _timer.Start();
    }

    private void ReconcileFullscreenState()
    {
        if (_exiting || _fullscreenMonitoringDisposed)
            return;

        if (!_pauseWhenFullscreenEnabled)
        {
            _fullscreenMonitor = IntPtr.Zero;
            ClearFullscreenOcclusion();
            RequestWallpaperSuspensionUpdate();
            return;
        }

        _fullscreenMonitor = TryGetFullscreenMonitor(out IntPtr monitor)
            ? monitor
            : IntPtr.Zero;
        RequestWallpaperSuspensionUpdate();
    }

    private void ClearFullscreenOcclusion()
    {
        foreach (AppWindow appWindow in KnownWindows())
            appWindow.FullscreenOccluded = false;
    }

    private void InstallWinEventHooks()
    {
        if (_winEventProc == null)
            return;

        foreach (uint eventId in new[]
        {
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            NativeMethods.EVENT_SYSTEM_MOVESIZEEND
        })
        {
            IntPtr hook = NativeMethods.SetWinEventHook(
                eventId,
                eventId,
                IntPtr.Zero,
                _winEventProc,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);
            if (hook != IntPtr.Zero)
                _winEventHooks.Add(hook);
        }
    }

    private void WinEventCallback(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (_exiting || _fullscreenMonitoringDisposed)
            return;

        if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE &&
            (hwnd == IntPtr.Zero || idObject != NativeMethods.OBJID_WINDOW ||
             idChild != NativeMethods.CHILDID_SELF))
        {
            return;
        }

        QueueFullscreenReevaluation();
    }

    private void QueueFullscreenReevaluation()
    {
        if (_exiting || _fullscreenMonitoringDisposed || !_fullscreenMonitoringStarted)
            return;

        if (Interlocked.Exchange(ref _fullscreenReevaluationPosted, 1) != 0)
            return;

        try
        {
            _eventDispatcher.BeginInvoke((Action)(() =>
            {
                Interlocked.Exchange(ref _fullscreenReevaluationPosted, 0);
                if (_exiting || _fullscreenMonitoringDisposed)
                    return;

                _fullscreenDebounceTimer.Stop();
                _fullscreenDebounceTimer.Start();
            }));
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _fullscreenReevaluationPosted, 0);
        }
    }

    private void DisposeFullscreenMonitoring()
    {
        if (_fullscreenMonitoringDisposed)
            return;

        _fullscreenMonitoringDisposed = true;
        _fullscreenMonitoringStarted = false;
        _fullscreenDebounceTimer.Stop();
        _fullscreenFallbackTimer.Stop();
        _fullscreenDebounceTimer.Dispose();
        _fullscreenFallbackTimer.Dispose();

        foreach (IntPtr hook in _winEventHooks)
            NativeMethods.UnhookWinEvent(hook);
        _winEventHooks.Clear();
        _eventDispatcher.Dispose();
    }

    private bool TryGetFullscreenMonitor(out IntPtr fullscreenMonitor)
    {
        fullscreenMonitor = IntPtr.Zero;
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || !NativeMethods.IsWindow(foreground) ||
            !NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground))
        {
            return false;
        }

        if (IsExcludedForegroundWindow(foreground) ||
            !NativeMethods.GetWindowRect(foreground, out NativeMethods.RECT windowRect))
        {
            return false;
        }

        IntPtr monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            Size = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfoW(monitor, ref monitorInfo))
            return false;

        const int tolerance = 2;
        bool coversMonitor = windowRect.Left <= monitorInfo.Monitor.Left + tolerance &&
            windowRect.Top <= monitorInfo.Monitor.Top + tolerance &&
            windowRect.Right >= monitorInfo.Monitor.Right - tolerance &&
            windowRect.Bottom >= monitorInfo.Monitor.Bottom - tolerance;
        if (!coversMonitor)
            return false;

        fullscreenMonitor = monitor;
        return true;
    }

    private static bool IsExcludedForegroundWindow(IntPtr hwnd)
    {
        if (hwnd == NativeMethods.GetShellWindow())
            return true;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        if (processId == (uint)Environment.ProcessId)
            return true;

        var className = new System.Text.StringBuilder(256);
        NativeMethods.GetClassNameW(hwnd, className, className.Capacity);
        return className.ToString() is "Progman" or "WorkerW";
    }

    private static IntPtr ResolveWallpaperMonitor(WallpaperWindowSpec spec)
    {
        if (spec.LogicalId == "virtual-screen" ||
            !spec.LogicalId.StartsWith("monitor-", StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        NativeMethods.RECT rect = new()
        {
            Left = spec.Bounds.Left,
            Top = spec.Bounds.Top,
            Right = spec.Bounds.Right,
            Bottom = spec.Bounds.Bottom
        };
        return NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }

    private void ExitHost()
    {
        if (_exiting)
            return;

        _exiting = true;
        DisposeFullscreenMonitoring();
        if (_pending != null)
        {
            foreach (AppWindow appWindow in _pending.Registry.Items)
                _runtime.DestroyWindow(appWindow);
            _pending = null;
        }

        foreach (WallpaperWindow window in _windows.ToArray())
            CloseTracked(window);

        _timer.Stop();
        _input.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _timer.Dispose();
        _runtime.Dispose();
        ExitThread();
    }

    private void CloseTracked(WallpaperWindow window)
    {
        _registry.MarkIntentionalClose(window.AppWindow);
        _embedder.Close(window.AppWindow);
        _runtime.DestroyWindow(window.AppWindow);
    }

    private void CheckForAllWindowsClosed()
    {
        if (_closeCheckLogged || !CloseCheck.AllClosed(_windows.Select(window => window.AppWindow)))
            return;

        bool intentional = CloseCheck.IsIntentional(_windows.Select(window => window.AppWindow));
        Console.WriteLine($"[CloseCheck] all tracked windows closed; intentional={intentional.ToString().ToLowerInvariant()}");
        _closeCheckLogged = true;
    }

    private void LogAllWindowsMissingIfNeeded()
    {
        bool allMissing = _windows.Count > 0 && _windows.All(window => window.AppWindow.State == AppWindowState.Pending);
        if (!allMissing)
        {
            _allWindowsMissingLogged = false;
            return;
        }

        if (!_allWindowsMissingLogged)
        {
            Console.WriteLine("[Lifecycle] all tracked windows missing; waiting for startup timeout.");
            _allWindowsMissingLogged = true;
        }
    }

    private static string GetTopologySignature() =>
        string.Join("|", Screen.AllScreens.Select(screen => $"{screen.DeviceName}:{screen.Bounds}"));

    private static string FormatHwnd(IntPtr hwnd) => hwnd == IntPtr.Zero ? "<none>" : $"0x{hwnd.ToInt64():X}";

    private sealed record ReplacementBatch(WindowRegistry Registry, List<WallpaperWindow> Windows, WallpaperWindow[] Targets);
}

public sealed class InputBox : Form
{
    private readonly TextBox _textBox;

    public InputBox(string title, string prompt, string defaultText)
    {
        Text = title;
        Size = new Size(420, 170);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var label = new Label { Left = 10, Top = 10, Width = 385, Text = prompt };
        _textBox = new TextBox { Left = 10, Top = 38, Width = 385, Text = defaultText };
        var ok = new Button { Text = "OK", Left = 230, Top = 85, Width = 78, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 317, Top = 85, Width = 78, DialogResult = DialogResult.Cancel };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(label);
        Controls.Add(_textBox);
        Controls.Add(ok);
        Controls.Add(cancel);
    }

    public string InputText => _textBox.Text;
}
