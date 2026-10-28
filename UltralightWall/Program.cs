using System.Globalization;

namespace UltralightWall;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            HostOptions options = HostOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(HostOptions.HelpText);
                return 0;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new WallpaperHostContext(options));
            return 0;
        }
        catch (OptionException ex)
        {
            Console.Error.WriteLine($"[Error] {ex.Message}");
            Console.Error.WriteLine("Use --help for usage.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Error] {ex.Message}");
            return 1;
        }
    }
}

public enum WallpaperMode
{
    Span,
    All,
    Single
}

public sealed record WallpaperWindowSpec(string LogicalId, string Url, Rectangle Bounds, Rectangle VirtualBounds);

public sealed class HostOptions
{
    private HostOptions() { }

    public string Url { get; private init; } = "";
    public IReadOnlyList<string> ExplicitWindowIds { get; private init; } = Array.Empty<string>();
    public IReadOnlyDictionary<int, string> MonitorUrls { get; private init; } = new Dictionary<int, string>();
    public WallpaperMode Mode { get; private init; } = WallpaperMode.Span;
    public int SelectedMonitor { get; private init; }
    public int ReconcileIntervalMs { get; private init; } = 250;
    public TimeSpan StartupTimeout { get; private init; } = TimeSpan.FromSeconds(30);
    public bool Verbose { get; private init; }
    public bool ShowHelp { get; private init; }

    public static HostOptions Parse(string[] args)
    {
        string url = DefaultProbeUrl();
        var windowIds = new List<string>();
        var monitorUrls = new Dictionary<int, string>();
        WallpaperMode mode = WallpaperMode.Span;
        int selectedMonitor = 0;
        int reconcileMs = 250;
        int startupTimeoutMs = 30_000;
        bool verbose = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (argument is "--help" or "-h")
                return new HostOptions { ShowHelp = true };

            if (argument == "--verbose")
            {
                verbose = true;
                continue;
            }

            string option = argument;
            string? inlineValue = null;
            int equalsIndex = argument.IndexOf('=');
            if (equalsIndex > 0)
            {
                option = argument[..equalsIndex];
                inlineValue = argument[(equalsIndex + 1)..];
            }

            switch (option)
            {
                case "--url":
                    url = UrlBuilder.Normalize(NextValue(args, ref index, option, inlineValue));
                    break;
                case "--window":
                {
                    string id = NextValue(args, ref index, option, inlineValue).Trim();
                    if (id.Length == 0)
                        throw new OptionException("--window requires a non-empty logical ID.");
                    windowIds.Add(id);
                    break;
                }
                case "--monitor-url":
                {
                    string value = NextValue(args, ref index, option, inlineValue);
                    int separator = value.IndexOf('=');
                    if (separator <= 0)
                        throw new OptionException("--monitor-url requires <1-based-monitor>=<url>.");

                    int monitor = ParsePositiveInt(value[..separator], option) - 1;
                    monitorUrls[monitor] = UrlBuilder.Normalize(value[(separator + 1)..]);
                    break;
                }
                case "--mode":
                    mode = NextValue(args, ref index, option, inlineValue).ToLowerInvariant() switch
                    {
                        "span" => WallpaperMode.Span,
                        "all" => WallpaperMode.All,
                        "single" => WallpaperMode.Single,
                        _ => throw new OptionException("--mode must be span, all, or single.")
                    };
                    break;
                case "--monitor":
                    selectedMonitor = ParsePositiveInt(NextValue(args, ref index, option, inlineValue), option) - 1;
                    break;
                case "--reconcile-ms":
                    reconcileMs = ParsePositiveInt(NextValue(args, ref index, option, inlineValue), option);
                    break;
                case "--startup-timeout-ms":
                    startupTimeoutMs = ParsePositiveInt(NextValue(args, ref index, option, inlineValue), option);
                    break;
                default:
                    throw new OptionException($"Unknown argument: {argument}");
            }
        }

        return new HostOptions
        {
            Url = url,
            ExplicitWindowIds = windowIds,
            MonitorUrls = monitorUrls,
            Mode = mode,
            SelectedMonitor = selectedMonitor,
            ReconcileIntervalMs = reconcileMs,
            StartupTimeout = TimeSpan.FromMilliseconds(startupTimeoutMs),
            Verbose = verbose
        };
    }

    public IReadOnlyList<WallpaperWindowSpec> ResolveWindowSpecs(
        string? baseUrl = null,
        WallpaperMode? mode = null,
        int? selectedMonitor = null,
        IReadOnlyDictionary<int, string>? monitorUrls = null)
    {
        string resolvedUrl = baseUrl ?? Url;
        WallpaperMode resolvedMode = mode ?? Mode;
        int resolvedMonitor = selectedMonitor ?? SelectedMonitor;
        IReadOnlyDictionary<int, string> resolvedOverrides = monitorUrls ?? MonitorUrls;
        Screen[] screens = Screen.AllScreens;
        Rectangle virtualBounds = SystemInformation.VirtualScreen;

        if (screens.Length == 0)
            return new[] { new WallpaperWindowSpec("virtual-screen", resolvedUrl, virtualBounds, virtualBounds) };

        if (ExplicitWindowIds.Count > 0)
        {
            return ExplicitWindowIds
                .Select(id => new WallpaperWindowSpec(id, resolvedUrl, virtualBounds, virtualBounds))
                .ToArray();
        }

        return resolvedMode switch
        {
            WallpaperMode.Span => new[] { new WallpaperWindowSpec("virtual-screen", resolvedUrl, virtualBounds, virtualBounds) },
            WallpaperMode.All => screens.Select((screen, index) => new WallpaperWindowSpec(
                $"monitor-{index + 1}",
                resolvedOverrides.TryGetValue(index, out string? overrideUrl) ? overrideUrl : resolvedUrl,
                screen.Bounds,
                virtualBounds)).ToArray(),
            WallpaperMode.Single => new[]
            {
                CreateSingleSpec(screens, Math.Clamp(resolvedMonitor, 0, screens.Length - 1), resolvedUrl, resolvedOverrides, virtualBounds)
            },
            _ => throw new InvalidOperationException("Unknown wallpaper mode.")
        };
    }

    private static WallpaperWindowSpec CreateSingleSpec(
        Screen[] screens,
        int index,
        string baseUrl,
        IReadOnlyDictionary<int, string> overrides,
        Rectangle virtualBounds)
    {
        string url = overrides.TryGetValue(index, out string? overrideUrl) ? overrideUrl : baseUrl;
        return new WallpaperWindowSpec($"monitor-{index + 1}", url, screens[index].Bounds, virtualBounds);
    }

    private static string NextValue(string[] args, ref int index, string option, string? inlineValue)
    {
        if (inlineValue != null)
            return inlineValue;
        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new OptionException($"{option} requires a value.");
        return args[index];
    }

    private static int ParsePositiveInt(string value, string option)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
            throw new OptionException($"{option} requires a positive integer.");
        return parsed;
    }

    private static string DefaultProbeUrl() => "file:///probe.html";

    public const string HelpText = """
        UltralightWall - Ultralight live wallpaper host

        Usage:
          UltralightWall.exe [options]

        Options:
          --url <url>                    HTTP, HTTPS, or file URL. Defaults to probe.html.
          --monitor-url N=<url>           URL override for 1-based monitor N. Repeatable.
          --window <id>                   Explicit logical window ID. Repeatable.
          --mode <mode>                   span, all, or single. Default: span.
          --monitor <number>              1-based monitor for single mode. Default: 1.
          --reconcile-ms <n>              Desktop reconciliation interval. Default: 250.
          --startup-timeout-ms <n>        Replacement startup timeout. Default: 30000.
          --verbose                       Log embedding diagnostics.
          --help, -h                     Show this help.
        """;
}

public sealed class OptionException : Exception
{
    public OptionException(string message) : base(message) { }
}

public static class UrlBuilder
{
    public static string Normalize(string value)
    {
        string url = value.Trim();
        if (!url.Contains("://", StringComparison.Ordinal) &&
            !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            url = $"https://{url}";

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps &&
             uri.Scheme != Uri.UriSchemeFile))
        {
            throw new OptionException($"Invalid URL: {url}");
        }

        return uri.AbsoluteUri;
    }
}
