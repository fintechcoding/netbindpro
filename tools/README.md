# tools — injection & tunnel test harness

Dev-only PowerShell helpers for exercising NetBind Pro's hook and WireGuard paths
without clicking through the UI. They launch a **throwaway Chrome profile**
(`%LOCALAPPDATA%\NetBindPro\chrome-test`), never your real one.

| Script | What it does |
|--------|--------------|
| `test-chrome.ps1 -AdapterGuid '{GUID}'` | Launches a test Chrome through the built Hook, bound to the given adapter; prints which processes got hooked, the egress IP via CDP, and the network-service sockets' local addresses. |
| `cdp-eval.ps1 -Url <url>` | Opens a URL in a `--remote-debugging-port=9333` Chrome tab and prints the page text (used to read the egress IP). |
| `wg-test-up.ps1 -Conf E:\vpn\x.conf [-StripDns]` | Brings a raw WireGuard conf up **safely**: rewrites `[Interface]` to `Table = off`, installs the tunnel service, **verifies the system default route did not change** (RDP-safety gate), then adds a high-metric default route via the tunnel. Needs elevation. |
| `wg-test-down.ps1 -Name <tunnel>` | Removes a tunnel brought up by `wg-test-up.ps1`. |

Notes:
- Installing a WireGuard tunnel service and adding routes require admin; running the
  Hook to inject a browser must **not** be elevated (an elevated browser de-elevates
  itself and loses the hook), which is why the app launches the Hook de-elevated.
- The tunnel service runs as LocalSystem, so configs must live somewhere SYSTEM can
  read (`ProgramData`, not per-user `%APPDATA%`).
