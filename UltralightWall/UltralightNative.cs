using System.Runtime.InteropServices;

namespace UltralightWall;

internal static class UltralightNative
{
    private const string AppCore = "AppCore.dll";
    private const string Core = "Ultralight.dll";

    internal const uint WindowFlagsBorderless = 1u << 0;
    internal const uint WindowFlagsHidden = 1u << 4;

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ulCreateSettings();

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulDestroySettings(IntPtr settings);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulSettingsSetDeveloperName(IntPtr settings, IntPtr name);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulSettingsSetAppName(IntPtr settings, IntPtr name);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulSettingsSetFileSystemPath(IntPtr settings, IntPtr path);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ulCreateApp(IntPtr settings, IntPtr config);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulDestroyApp(IntPtr app);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ulAppRunOnce(IntPtr app, double maxWaitSeconds);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ulAppGetMainMonitor(IntPtr app);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ulAppGetMonitor(IntPtr app, uint index);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint ulAppGetMonitorCount(IntPtr app);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double ulMonitorGetScale(IntPtr monitor);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ulCreateWindow(
        IntPtr monitor,
        double width,
        double height,
        [MarshalAs(UnmanagedType.I1)] bool fullscreen,
        uint windowFlags);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulDestroyWindow(IntPtr window);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulWindowClose(IntPtr window);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulWindowShowWhenReady(IntPtr window, double timeoutSeconds);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ulWindowGetNativeHandle(IntPtr window);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulWindowSetTitle(IntPtr window, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ulWindowAddPanel(IntPtr window, IntPtr description, IntPtr viewConfig);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ulPanelGetView(IntPtr panel);

    [DllImport(AppCore, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulDestroyPanel(IntPtr panel);

    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ulCreateString([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulDestroyString(IntPtr value);

    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulViewLoadURL(IntPtr view, IntPtr url);

    [DllImport(Core, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ulDestroyView(IntPtr view);
}
