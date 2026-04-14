# Contributing to NetBind Pro

Thank you for your interest in contributing! This document covers how to get set up and what we look for in pull requests.

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| Visual Studio 2022 | 17.8+ | Primary IDE |
| .NET SDK | 8.0+ | Build & run |
| Windows 10/11 | 19041+ | Target platform |
| Inno Setup | 6.x | Build installer (optional) |

## Getting started

```bash
git clone https://github.com/codingsecurity/netbindpro.git
cd netbindpro
# Open NetBindPro.sln in Visual Studio 2022
# Set NetBindPro.UI as startup project
# Run as Administrator (required for injection)
```

## Project layout

```
src/
  NetBindPro.Core/          # Models, services, adapter discovery — no UI deps
  NetBindPro.Hook/          # CLI injection engine — must build x86 AND x64
  NetBindPro.UI/            # WPF application — MVVM with CommunityToolkit
installer/
  NetBindPro.Setup.iss      # Inno Setup script
.github/
  workflows/ci.yml          # Build + release pipeline
```

## Architecture rules

- **NetBindPro.Core** must stay free of WPF/WinForms/System.Windows references.
- **NetBindPro.Hook** must build for both x86 and x64 — 32-bit processes need the x86 build.
- ViewModels use `CommunityToolkit.Mvvm` — use `[ObservableProperty]` and `[RelayCommand]`.
- All file writes use atomic pattern (write temp → rename).
- Hook code must handle exceptions silently — injected code crashing the host process is the worst outcome.

## Pull request checklist

- [ ] Code follows `.editorconfig` style (4-space indent, CRLF)
- [ ] No new public API without XML doc comments
- [ ] Hook changes tested against both 32-bit and 64-bit target processes
- [ ] WireGuard and Tailscale adapter binding verified (or noted as untested)
- [ ] `CHANGELOG.md` updated under `[Unreleased]`

## Reporting bugs

Please open an issue with:
1. Windows version and architecture
2. Target process name and architecture (32/64-bit)
3. Adapter type (WireGuard / Tailscale / OpenVPN / etc.)
4. Debug log from `%TEMP%\NetBindPro-<process>.log` (run with `--debug`)
5. Steps to reproduce

## License

By contributing you agree that your code is released under the MIT License.
