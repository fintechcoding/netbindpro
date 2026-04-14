# NetBindPro v2.0.0

Per-process network adapter binding tool for Windows. Forces specific processes to use a chosen network adapter (WireGuard, Tailscale, VPN, etc.) via Winsock hook injection.

## Architecture

- **NetBindPro.Core** - Models, services, adapter discovery (class library)
- **NetBindPro.Hook** - EasyHook-based injection engine (console exe, runs elevated)
- **NetBindPro.UI** - WPF application with system tray support (WinExe)

## Build

```bash
dotnet build NetBindPro.sln --configuration Debug
```

Target: `net8.0-windows` | Platforms: x64, x86 (UI is x64 only)

## Key Dependencies

- **EasyHook 2.7.7097** - .NET Framework package, works on .NET 8 with NU1701 compat warning
- **System.CommandLine 2.0.0-beta4** - CLI parsing for Hook project
- **CommunityToolkit.Mvvm 8.3.2** - MVVM framework for UI
- **System.Management 8.0.0** - WMI process monitoring

## Build Fixes Applied

| Issue | Fix |
|-------|-----|
| EasyHook 2.27.0.0 not found | Changed to `2.7.7097` (actual NuGet version) |
| LibraryImport can't marshal structs | Switched bind/bind6 to `DllImport` with `EntryPoint="bind"` |
| `out` in lambda (C# 13 preview) | Extracted to named methods `CreateProcessW_Hook`/`CreateProcessA_Hook` |
| Missing WinForms ref (NotifyIcon) | Added `FrameworkReference Microsoft.WindowsDesktop.App.WindowsForms` |
| Missing usings in ViewModels/OtherPages | Added `Microsoft.Extensions.Logging`, `NetBindPro.UI.Services`, `NetBindPro.UI.ViewModels` |
| `System.IO` not in implicit usings | Added explicit `using System.IO` to InjectionService.cs |
| Pages not registered in DI | Added `services.AddTransient<RulesPage>()` etc. in App.xaml.cs |
| `ProcessesViewModel` doesn't exist | Removed from DI registration |
| bind(IntPtr) overload missing | Added `DllImport` overload taking `IntPtr name` |

## UI Theme

- **Light corporate theme** - White backgrounds, dark text, orange accent (#FF6633)
- **Icon** - Generated via Gemini AI, stored as `src/NetBindPro.UI/app.ico` (7 sizes: 16-256px)
- **TextBox style** - Uses default WPF template (no custom ControlTemplate) with `Foreground="Black"` to avoid text visibility issues
- **Window chrome** - Custom borderless with Windows-style min/max/close buttons (Segoe MDL2 Assets)
- **Nav icons** - Segoe MDL2 Assets font glyphs

## Notes

- `System.Text.Json 8.0.4` has NU1903 vulnerability warning - can be updated if needed
- EasyHook NU1701 compat warning is expected and harmless
- App uses `ShutdownMode="OnExplicitShutdown"` with system tray - must exit via tray menu
