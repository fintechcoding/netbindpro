# Building NetBind Pro

## Prerequisites

| Requirement | Version | Notes |
|-------------|---------|-------|
| Visual Studio 2022 | 17.8+ | Community edition works |
| .NET SDK | 8.0+ | Installed with VS or separately |
| Inno Setup | 6.x | Installer only — optional |

Install the **.NET desktop development** workload in Visual Studio Setup.

---

## Quick build (Visual Studio)

1. Open `NetBindPro.sln`
2. Set **Configuration** to `Release`, **Platform** to `x64`
3. Right-click the solution → **Restore NuGet Packages**
4. **Build → Build Solution** (`Ctrl+Shift+B`)
5. Set **NetBindPro.UI** as startup project
6. **Run as Administrator** (F5)

---

## Build from command line

```powershell
# Clone
git clone https://github.com/codingsecurity/netbindpro.git
cd netbindpro

# Quick build (Debug, no publish)
.\build.ps1 -Configuration Debug

# Full release build + publish output
.\build.ps1 -Publish

# Full release build + publish + installer (needs Inno Setup in PATH)
.\build.ps1 -Publish -Installer
```

---

## Building both Hook architectures

The Hook project must be built for **both x86 and x64** because:
- A **64-bit process** needs the x64 Hook DLL
- A **32-bit process** (common for older apps and games) needs the x86 Hook DLL

The GUI automatically selects the right Hook build when injecting.

```powershell
dotnet build src/NetBindPro.Hook -c Release /p:Platform=x64
dotnet build src/NetBindPro.Hook -c Release /p:Platform=x86
```

The x86 build output is copied to `publish/win-x64/NetBindPro.Hook.x86.exe` by `build.ps1`.

---

## Output structure after publish

```
publish/win-x64/
  NetBindPro.exe              ← WPF GUI (run as Administrator)
  NetBindPro.Hook.exe         ← Hook CLI, x64
  NetBindPro.Hook.x86.exe     ← Hook CLI, x86 (for 32-bit processes)
  NetBindPro.Core.dll
  *.dll                       ← NuGet dependencies
  *.json                      ← Runtime config

publish/installer/
  NetBindPro-2.0.0-Setup.exe  ← Inno Setup installer
```

---

## Building the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) installed and `ISCC.exe` in your PATH.

```powershell
# Build publish output first
.\build.ps1 -Publish

# Then build installer
ISCC.exe installer\NetBindPro.Setup.iss /O"publish\installer"

# Or all-in-one:
.\build.ps1 -Publish -Installer
```

The resulting `.exe` is a single self-extracting installer that:
- Checks for .NET 8 runtime and prompts to download if missing
- Installs to `%ProgramFiles%\NetBind Pro\`
- Creates Start Menu and optional Desktop shortcuts
- Registers a startup entry (optional during install)
- Provides a clean uninstaller via Add/Remove Programs

---

## Troubleshooting

**"Cannot load EasyHook" at runtime**
EasyHook requires the Visual C++ 2019 Redistributable. It's installed by NuGet at build time.
Download manually from: https://aka.ms/vs/17/release/vc_redist.x64.exe

**"Access denied" when injecting**
The Hook process must run as Administrator. The GUI prompts for UAC elevation automatically.
In debug, right-click Visual Studio → "Run as administrator".

**Hook DLL rejected by target process**
Some software (anti-cheat engines, DRM) blocks DLL injection at the kernel level.
NetBind Pro cannot work with these applications. See Known Limitations in README.

**WireGuard adapter not showing**
Make sure the WireGuard tunnel is **connected** (not just configured). Adapters must be
`OperationalStatus.Up` to appear in the list. Check WireGuard client shows a green indicator.
