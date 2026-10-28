namespace UltralightWall;

public sealed class WindowRegistry
{
    private readonly Dictionary<string, AppWindow> _byLogicalId = new(StringComparer.Ordinal);

    public IReadOnlyCollection<AppWindow> Items => _byLogicalId.Values;

    public void Add(AppWindow appWindow)
    {
        if (!_byLogicalId.TryAdd(appWindow.LogicalId, appWindow))
            throw new InvalidOperationException($"Logical window ID already registered: {appWindow.LogicalId}");
    }

    public bool IsHwndAssignedToAnother(IntPtr hwnd, AppWindow appWindow) =>
        hwnd != IntPtr.Zero && _byLogicalId.Values.Any(existing =>
            !ReferenceEquals(existing, appWindow) &&
            existing.State == AppWindowState.Embedded &&
            existing.Hwnd == hwnd);

    public void MarkEmbedded(AppWindow appWindow)
    {
        if (appWindow.State is AppWindowState.Pending or AppWindowState.Embedded)
            appWindow.State = AppWindowState.Embedded;
    }

    public void MarkClosed(AppWindow appWindow)
    {
        if (appWindow.State is AppWindowState.Pending or AppWindowState.Embedded)
        {
            appWindow.State = AppWindowState.Closed;
            if (appWindow.CloseIntent == WindowCloseIntent.None)
                appWindow.CloseIntent = WindowCloseIntent.Unexpected;
        }
    }

    public void MarkIntentionalClose(AppWindow appWindow)
    {
        if (appWindow.State is AppWindowState.Pending or AppWindowState.Embedded)
            appWindow.CloseIntent = WindowCloseIntent.Intentional;
    }

    public void MarkUnexpectedClose(AppWindow appWindow)
    {
        if (appWindow.State is AppWindowState.Pending or AppWindowState.Embedded)
        {
            appWindow.CloseIntent = WindowCloseIntent.Unexpected;
            appWindow.State = AppWindowState.Closed;
        }
    }

    public void MarkFailed(AppWindow appWindow, string reason)
    {
        if (appWindow.State is AppWindowState.Pending or AppWindowState.Embedded)
        {
            appWindow.State = AppWindowState.Failed;
            appWindow.FailureReason = reason;
        }
    }
}
