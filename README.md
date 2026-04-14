<div align="center">

# NetBind Pro

**Per-process network adapter binding for Windows 10/11**

Force any process through WireGuard, Tailscale, VPN, or any network interface — individually, without affecting the rest of the system.

[![Build](https://img.shields.io/github/actions/workflow/status/codingsecurity/netbindpro/ci.yml?branch=main&style=flat-square&logo=github)](https://github.com/codingsecurity/netbindpro/actions)
[![Release](https://img.shields.io/github/v/release/codingsecurity/netbindpro?style=flat-square&logo=github)](https://github.com/codingsecurity/netbindpro/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?style=flat-square&logo=windows)](https://github.com/codingsecurity/netbindpro/releases)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/8.0)

[**Download Installer**](https://github.com/codingsecurity/netbindpro/releases/latest) · [Report Bug](https://github.com/codingsecurity/netbindpro/issues) · [Request Feature](https://github.com/codingsecurity/netbindpro/discussions)

</div>

---

## What is this?

Windows routes all traffic from every application through the same network interface by default. NetBind Pro lets you break that — routing **individual processes** through whichever adapter you choose, while leaving everything else untouched.

**Common use cases:**

| Scenario | Rule |
|----------|------|
| Route `qbittorrent.exe` through WireGuard for privacy | `qbittorrent.exe → wg0` |
| Keep `Slack.exe` on your fast Ethernet while others use VPN | `Slack.exe → Ethernet` |
| Force `firefox.exe` through Tailscale for split-tunneling | `firefox.exe → Tailscale` |
| Bind a game through a specific ISP connection | `game.exe → Ethernet 2` |

## How it works

NetBind Pro uses **DLL injection via [EasyHook](https://easyhook.github.io/)** to intercept Winsock calls (`bind`, `connect`, `WSAConnect`) inside the target process and redirect them to the IP address of your chosen adapter. This is transparent to the application — it doesn't know it's being rerouted.

Child processes spawned by the bound process also inherit the binding automatically.

```
firefox.exe
  └─ connect("1.2.3.4:443")          ← Winsock call intercepted
       └─ bind to wg0 (10.8.0.2)     ← NetBind Pro rebinds before connect
            └─ packet goes via WireGuard tunnel, not your ISP
```

### Why not just change the routing table?

`route add` affects **all processes** system-wide. NetBind Pro's injection is per-process and non-destructive — removing a rule or killing NetBind Pro returns the process to normal routing instantly.

---

## What's different from NetworkAdapterSelector?

NetBind Pro is a heavily extended fork of [falahati/NetworkAdapterSelector](https://github.com/falahati/NetworkAdapterSelector). Key improvements:

| Feature | NetworkAdapterSelector | **NetBind Pro** |
|---|:---:|:---:|
| WireGuard adapter support | ❌ | ✅ |
| Tailscale adapter support | ❌ | ✅ |
| Modern WPF GUI | ❌ | ✅ |
| Persistent rule profiles (JSON) | ❌ | ✅ |
| Auto-inject on process start | ❌ | ✅ |
| System tray + Windows startup | ❌ | ✅ |
| Proper uninstaller | ❌ (broken) | ✅ |
| Windows 11 tested | ❓ | ✅ |
| License | GPL-2.0 | **MIT** |

**Root cause of WireGuard/Tailscale failure in the original:**
The original code filtered adapters by `nic.SupportsMulticast`. WireGuard and Tailscale tunnel interfaces don't set that flag, so they were silently ignored. NetBind Pro removes this filter entirely.

---

## Requirements

- Windows 10 (build 19041+) or Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (the installer checks for this)
- Administrator privileges (required for DLL injection)

---

## Installation

### Option A — Installer (recommended)

1. Download `NetBindPro-2.0.0-Setup.exe` from [Releases](https://github.com/codingsecurity/netbindpro/releases/latest)
2. Run the installer as Administrator
3. Launch **NetBind Pro** from the Start Menu or Desktop shortcut

### Option B — Portable ZIP

1. Download `NetBindPro-portable-win-x64.zip` from Releases
2. Extract to any folder
3. Run `NetBindPro.exe` as Administrator

---

## Quick start

### GUI

1. Open **NetBind Pro**
2. Go to **Rules → Add Rule**
3. Type the process name (e.g. `firefox.exe`)
4. Select your WireGuard / Tailscale / VPN adapter
5. Click **Add Rule**
6. Either launch the process fresh, or click **▶ Inject** to bind a running process

### CLI

List available adapters:
```
NetBindPro.Hook.exe list
```

Bind a new process:
```
NetBindPro.Hook.exe --network "{ADAPTER-GUID}" --execute "C:\path\to\app.exe"
```

Bind a running process (find PID in Task Manager):
```
NetBindPro.Hook.exe --network "{ADAPTER-GUID}" --attach 1234
```

Get adapter GUIDs:
```
NetBindPro.Hook.exe list
```

Full CLI reference:

| Flag | Short | Description |
|------|-------|-------------|
| `--network` | `-n` | Adapter GUID (required) |
| `--execute` | `-e` | Path to executable to launch |
| `--attach`  | `-a` | PID of running process |
| `--args`    | `-c` | Arguments for launched process |
| `--delay`   | `-t` | Delay before inject in ms (default: 500) |
| `--debug`   | `-d` | Write log to `%TEMP%\NetBindPro-<process>.log` |
| `--no-title`|      | Don't modify window title bar |

---

## Tailscale setup

Tailscale uses the CGNAT range `100.64.0.0/10`. NetBind Pro correctly handles this — just select the **Tailscale** adapter from the list (it appears with a blue `Tailscale` badge).

No extra configuration needed. NetBind Pro does **not** skip the `100.64.0.0/10` range when determining which connections to rebind.

---

## WireGuard setup

1. Make sure your WireGuard tunnel is active (the interface must be `Up`)
2. Open NetBind Pro — WireGuard adapters appear with a `WireGuard` badge and are sorted to the top
3. Add a rule pointing to the WireGuard interface
4. Processes bound to WireGuard will route all TCP/UDP through the tunnel

**Note:** DNS queries still go through the system DNS resolver, not through the VPN DNS. If you need DNS-over-VPN, configure your system DNS server separately (Settings → Network → DNS).

---

## Known limitations

- **DNS leaks** — DNS resolution uses the system-wide resolver, not the bound adapter's DNS. Set VPN DNS in Windows network settings if needed.
- **UDP-only apps** — most sockets work, but apps that use raw sockets or bypass Winsock (e.g. some games using proprietary networking) may not be intercepted.
- **Anti-cheat / anti-debug** — games or software with anti-injection protection (BattleEye, Easy Anti-Cheat) will reject the hook DLL.
- **Existing connections** — injecting into an already-running process only affects new connections, not ones already established.
- **x86 vs x64** — a 32-bit process needs the x86 build of the Hook. The installer includes both; the GUI selects the right one automatically.

---

## Building from source

### Prerequisites

- Visual Studio 2022 (17.8+) with **.NET desktop development** workload
- .NET 8 SDK
- Inno Setup 6.x (for installer only)

### Steps

```bash
git clone https://github.com/codingsecurity/netbindpro.git
cd netbindpro

# Open in Visual Studio
start NetBindPro.sln

# Or build from CLI
dotnet restore NetBindPro.sln
dotnet build NetBindPro.sln -c Release /p:Platform=x64
```

**Important:** The Hook project must be built for both x86 and x64:
```bash
dotnet build src/NetBindPro.Hook -c Release /p:Platform=x64
dotnet build src/NetBindPro.Hook -c Release /p:Platform=x86
```

To build the installer (requires Inno Setup):
```bash
ISCC.exe installer/NetBindPro.Setup.iss
```

---

## Project structure

```
NetBindPro/
├── src/
│   ├── NetBindPro.Core/          # Models, services, adapter discovery
│   │   ├── Models/Models.cs      # BindingRule, AppProfile, AdapterInfo
│   │   ├── Services/
│   │   │   ├── AdapterDiscovery.cs   # Network interface enumeration
│   │   │   └── ProfileService.cs     # JSON profile persistence
│   ├── NetBindPro.Hook/          # CLI injection engine
│   │   ├── Native/NativeInterop.cs   # P/Invoke: Winsock, kernel32
│   │   ├── Guest.cs              # Injected payload (EasyHook IEntryPoint)
│   │   └── Program.cs            # CLI entry point (System.CommandLine)
│   └── NetBindPro.UI/            # WPF application
│       ├── Themes/               # Colors, typography, control styles
│       ├── Views/                # Pages and windows (XAML + code-behind)
│       ├── ViewModels/           # MVVM view models (CommunityToolkit.Mvvm)
│       └── Services/             # InjectionService, ProcessMonitorService
├── installer/
│   └── NetBindPro.Setup.iss      # Inno Setup script
├── .github/
│   └── workflows/ci.yml          # GitHub Actions: build → test → release
├── Directory.Build.props         # Shared MSBuild properties
├── .editorconfig                 # Code style
├── CHANGELOG.md
├── CONTRIBUTING.md
└── LICENSE                       # MIT
```

---

## Security

NetBind Pro requires **Administrator** privileges because DLL injection into another process is a privileged operation. The injected code only intercepts Winsock socket calls — it does not read memory, capture credentials, or modify any data in transit.

The Hook executable is signed with a self-signed certificate in debug builds. For production releases, sign with a trusted code-signing certificate to avoid SmartScreen warnings.

If you discover a security vulnerability, please open a [private advisory](https://github.com/codingsecurity/netbindpro/security/advisories/new) rather than a public issue.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup instructions, architecture rules, and PR checklist.

---

## Acknowledgements

- [**falahati/NetworkAdapterSelector**](https://github.com/falahati/NetworkAdapterSelector) — the original project this is forked from
- [**EasyHook**](https://github.com/EasyHook/EasyHook) — the DLL injection library that makes this possible
- [**CommunityToolkit.Mvvm**](https://github.com/CommunityToolkit/dotnet) — MVVM source generators for the WPF UI

---

## License

MIT — see [LICENSE](LICENSE) for full text.

> Based on [NetworkAdapterSelector](https://github.com/falahati/NetworkAdapterSelector) by Soroush Falahati (GPL-2.0). The hook technique has been substantially rewritten; original GPL-2.0 code is not present in this repository.
