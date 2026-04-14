# Changelog

All notable changes to NetBind Pro are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
