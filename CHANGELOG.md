# Changelog

All notable changes to NetBind Pro are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.3.3] — 2026-10-14 — The real launch fix

### Fixed
- **The actual cause of the launch crash.** The net48 Hook's output carries old
  framework-shim DLLs (`System.Memory` 4.0.1.1, `System.Buffers`, `System.Numerics.Vectors`,
  `System.Runtime.CompilerServices.Unsafe`). It was being copied into the same folder as the
  self-contained UI and **overwrote the UI's .NET 10 runtime assemblies**, so WPF crashed on
  launch. The Hook now lives in an isolated `hook\` subfolder and `InjectionService` runs it
  from there — no collision. Verified by launching the packaged build. (This also explains
  why the issue appeared on both .NET 8 and .NET 10 — it was never an SDK/runtime problem.)

### Added (tunnel correctness)
- **WireGuard `Table = off` on import** + a high-metric default route added when the tunnel
  starts, so a `0.0.0.0/0` tunnel no longer hijacks the whole PC's internet or blocks other
  apps — only processes bound to it egress through it.
- **MTU auto-probe** (`ProbeMtuAsync`) and `SetMtu` for tunnels nested inside another VPN.
- **Per-process kill-switch** flag on rules (`BindingRule.KillSwitch`).
- **Update service** (`UpdateService`) that checks GitHub Releases for a newer version.

---

## [2.3.2] — 2026-10-14 — App launches

### Fixed
- **App crashed on launch with no window** (`System.TypeInitializationException` →
  `MS.Internal.MimeTypeMapper` → `System.Memory, Version=8.0.0.0 not found`). The .NET 8
  self-contained publish shipped an old out-of-band `System.Memory.dll` (assembly 4.0.1.1)
  that shadowed the framework's 8.0.0.0, so WPF failed to initialize. Retargeted the UI and
  Core to **.NET 10** (`net10.0-windows`), whose self-contained output carries a consistent
  `System.Memory` 10.0.0.0 — the app now starts. Verified by launching the self-contained
  build. The Hook stays on .NET Framework 4.8 for EasyHook.

### Build
- CI now uses the .NET 10 SDK.

---

## [2.3.1] — 2026-10-14

### Fixed
- **"Installed but won't start"** — upgrading while a previous version was still running
  (it stays in the system tray) left files locked, so the installer could only half-replace
  them and the result failed to launch. The installer now force-closes any running NetBind
  Pro and kills lingering `NetBindPro.exe`/`NetBindPro.Hook.exe` before copying files.
- **Startup is now self-diagnosing** — a global crash handler writes
  `%LOCALAPPDATA%\NetBindPro\crash.log` and shows the error instead of the process silently
  dying with no window.

### Build
- Rewrote the GitHub Actions workflow for the current layout (self-contained .NET 8 UI +
  .NET Framework 4.8 Hook) so a pushed `v*` tag builds the installer and publishes the
  release on GitHub's runners — avoiding flaky large-file uploads.

---

## [2.3.0] — 2026-10-14 — Injection fixed

### Fixed
- **Injection now works** — the Hook (`Hook.exe` + the injected `Guest` payload) was
  built for .NET 8, but EasyHook can only inject a **.NET Framework** payload (it hosts
  the Framework CLR inside the target). That is why every injection failed with
  "Hook process exited with code 1". The Hook project is now **.NET Framework 4.8**, and
  EasyHook's native loaders (`EasyHook64.dll`, `EasyLoad64.dll`, the `*Svc.exe` helpers)
  ship alongside it. net48 is present on all supported Windows 10/11 installs, so nothing
  extra is bundled for it; the UI stays self-contained .NET 8.
- **Tunnel Stop button** — clicking Stop did nothing because the busy-state flip happened
  before the running-state was read, so the code always took the Start branch. Fixed.

### Internal
- Hook no longer references `NetBindPro.Core` (its `list` command enumerates adapters
  directly); `LibraryImport` P/Invokes converted to `DllImport` for net48.

---

## [2.2.1] — 2026-10-14

### Fixed
- **Post-install launch** — the installer's "Launch NetBind Pro" step failed with
  `CreateProcess failed; code 740 (requires elevation)` because the app requires admin
  and Inno Setup ran the launch as the original non-elevated user. Added the `shellexec`
  flag so it launches via ShellExecute and elevates correctly.

---

## [2.2.0] — 2026-10-14

### Added
- **Tunnel auto-start** — mark a tunnel "Auto" on the Tunnels page and NetBind Pro brings
  it up automatically on launch.
- **Name-based rules** — a rule bound to a managed tunnel now resolves the tunnel's
  *current* adapter by name at injection time, so it keeps working even if the tunnel is
  re-created with a new adapter GUID. Applied to both manual and auto-inject paths.

---

## [2.1.1] — 2026-10-14

### Changed
- **Self-contained installer** — the .NET 8 Desktop Runtime is now bundled inside the
  installer. No separate runtime download is required, and the installer no longer shows
  a ".NET 8 required" prompt (which could misfire even when a runtime was installed).

---

## [2.1.0] — 2026-10-14 — Multi-tunnel manager

### Added
- **WireGuard tunnel manager** — a new *Tunnels* page to import `.conf` files, bring
  tunnels up/down as Windows tunnel services (each becomes its own wintun adapter),
  see live status, and check each tunnel's public exit IP. (`WireGuardService`,
  `TunnelConfig`, `TunnelsPage`)
- **Auto-download WireGuard** — if WireGuard for Windows is missing, the app can
  download and run the official installer from the Tunnels page.
- `BindingRule.TunnelName` — groundwork for resolving a rule's adapter from a tunnel
  by name (stable across tunnel re-creation); UI wiring lands in a later build.

### Notes
- Tunnels appear in the *Add Rule* adapter list once running, so a process can already
  be assigned to a specific tunnel today.

---

## [2.0.2] — 2026-10-14

### Added
- **Leak kill-switch** — if the target adapter is down or has no usable IP, the hook now
  refuses outbound `connect`/`WSAConnect` (returns `WSAENETUNREACH`) instead of letting the
  connection fall back to the default route. A bound app stays on its tunnel or goes nowhere.
- **Tunnel exit-IP check** — the Add Rule dialog can verify a selected adapter's public exit
  IP (and country) from inside the app, by binding the request to the adapter's local IP and
  querying an IP-echo endpoint. No browser or external site needed. (`EgressCheckService`)

### Known limitations (tracked for a kernel-level pass)
- The kill-switch covers sockets created after injection. Sockets already open before
  injection, and DNS resolved by the system service, are not yet guarded — that needs a
  WFP per-app filter (planned).

---

## [2.0.1] — 2026-10-14

### Added
- **Running-process picker** in the Add Binding Rule dialog — search and select a live
  process (e.g. firefox.exe, chrome.exe) instead of typing the executable name by hand;
  the full path is captured automatically for precise matching.

### Changed
- Add Rule dialog surfaces VPN / tunnel adapters first, since routing an app through a
  tunnel is the primary use case.

### Fixed
- Installer script: corrected relative source/output paths (`..\` instead of `..\..\`),
  fixed the icon path (`app.ico`), bundled the x86 Hook executable, fixed a `ShellExec`
  compile error in the `[Code]` section, and pointed project URLs at the correct repository.

---

## [2.0.0] — 2024-12-01

### Added
- **WireGuard support** — removed `SupportsMulticast` filter that blocked WireGuard/Tailscale adapters
- **Tailscale support** — correctly detects and binds to Tailscale virtual interfaces
- **Modern WPF GUI** — full MVVM application with dark theme, system tray, and per-monitor DPI awareness
- **JSON profile system** — rules and settings persist to `%APPDATA%\NetBindPro\profile.json`
- **Auto-inject** — WMI process monitor automatically binds new processes matching configured rules
- **Adapter discovery** — classifies adapters as WireGuard / Tailscale / VPN / Ethernet / Wi-Fi
- **Child process inheritance** — injected processes pass the binding to child processes they spawn
- **Inno Setup installer** — proper install/uninstall with registry cleanup, .NET 8 check, startup option
- **GitHub Actions CI/CD** — automated build, test, publish, and release pipeline
- **MIT License** — changed from GPL-2.0 (original project) to MIT

### Changed
- Rewritten hook engine in modern C# 12 / .NET 8 with `LibraryImport` source generators
- Hook CLI now uses `System.CommandLine` with structured argument parsing
- Profile writes are atomic (write-to-temp → rename) to prevent corruption
- Status bar shows live WireGuard and Tailscale IP addresses
- Window chrome is custom (no native title bar) with DPI-aware rendering

### Fixed
- **Critical**: WireGuard and Tailscale adapters were silently ignored due to `SupportsMulticast` filter
- **Critical**: Uninstaller failed to remove shell extension DLL registration (original project issue)
- IPv6 binding now correctly uses the scope ID from the network interface

### Removed
- Shell extension (right-click context menu) — replaced by the GUI's "Add Rule" flow
- Dependency on `SharpShell` (was GPL-licensed)

---

## [1.x] — NetworkAdapterSelector (original)

Original project by Soroush Falahati. See:
https://github.com/falahati/NetworkAdapterSelector
