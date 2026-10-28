# UltralightWall

UltralightWall hosts web pages as Windows desktop wallpapers using Ultralight AppCore. It creates native Ultralight windows in-process, embeds them behind desktop icons, and keeps tray controls plus low-level desktop input routing.

## Requirements

- Windows 10/11
- .NET 9 SDK
- Windows x64
- Ultralight SDK `2.0.0-beta.2`

Install the pinned SDK into `third_party\ultralight`:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\setup-ultralight.ps1
```

The SDK is ignored by Git. Builds also accept `ULTRALIGHT_SDK_ROOT`.

## Build and test

```powershell
dotnet build UltralightWall\UltralightWall.sln -c Release
dotnet build UltralightWall.Tests\UltralightWall.Tests.csproj -c Release
dotnet run --project UltralightWall.Tests\UltralightWall.Tests.csproj -c Release
```

## Run

Without `--url`, UltralightWall loads its copied `assets\probe.html` page.

```powershell
UltralightWall\bin\Release\net9.0-windows\win-x64\UltralightWall.exe `
  --mode all
```

Options:

- `--url <url>`: HTTP, HTTPS, or file URL.
- `--monitor-url N=<url>`: per-monitor URL override; repeatable.
- `--mode span|all|single`: one virtual-screen window, one per monitor, or one selected monitor.
- `--monitor <number>`: 1-based monitor for `single` mode.
- `--window <id>`: explicit logical window ID; repeatable.
- `--reconcile-ms <n>`: desktop geometry/lifecycle reconciliation interval.
- `--startup-timeout-ms <n>`: replacement startup timeout.
- `--verbose`: embedding diagnostics.
- `--help`, `-h`: show help.

## Behavior

- WorkerW/Progman desktop embedding.
- Span, all-monitor, and selected-monitor modes.
- Borderless, taskbar-hidden wallpaper windows.
- Runtime URL changes and per-monitor links.
- Fresh-window reload and transactional replacement.
- Tray controls and CLI help.
- Mouse, button, double-click, movement, wheel, keyboard, modifier, and web-shortcut forwarding.
- Desktop icon selection, desktop context menu, critical system keys, and Alt+Tab pass-through.
- Initialization retries, startup timeout, lifecycle tracking, topology reconciliation, and graceful shutdown.

Desktop embedding depends on Windows Shell behavior. Explorer restart recovery, display hotplug/reconfiguration, and platform-specific focus/DPI quirks remain outside current scope.

## License

MIT. See [LICENSE](LICENSE).
