# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 2.x     | ✅ |
| 1.x (NetworkAdapterSelector) | ❌ |

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Instead, open a [private security advisory](https://github.com/codingsecurity/netbindpro/security/advisories/new).

Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

We aim to respond within 7 days and release a patch within 30 days of confirmation.

## Security model

NetBind Pro requires **Administrator** privileges. It operates by injecting a DLL into target processes to intercept Winsock calls. This is a privileged operation and is by design.

The injected code:
- Only intercepts `bind()`, `connect()`, `WSAConnect()`, `closesocket()`, and `CreateProcess()` in `ws2_32.dll` / `kernel32.dll`
- Does **not** read application memory beyond what EasyHook requires for hook setup
- Does **not** capture, log, or transmit any network traffic content
- Does **not** modify data in transit — only which interface the connection is routed through

The profile file (`%APPDATA%\NetBindPro\profile.json`) is stored in the user's app data directory and contains only adapter GUIDs and process names — no credentials or sensitive data.
