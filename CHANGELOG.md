# Changelog

All notable changes to NetBind Pro are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

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
