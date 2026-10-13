; NetBind Pro - Inno Setup Installer Script
; Build with Inno Setup 6.x: https://jrsoftware.org/isinfo.php
; Run: ISCC.exe NetBindPro.Setup.iss

#define AppName        "NetBind Pro"
#define AppVersion     "2.2.0"
#define AppPublisher   "NetBind Pro Contributors"
#define AppURL         "https://github.com/fintechcoding/netbindpro"
#define AppExeName     "NetBindPro.exe"
#define HookExeName    "NetBindPro.Hook.exe"
#define HookExeName86  "NetBindPro.Hook.x86.exe"
#define OutputDir      "..\publish\installer"
#define SourceDir      "..\publish\win-x64"

[Setup]
AppId={{8B7C1A2D-3E4F-5A6B-7C8D-9E0F1A2B3C4D}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\NetBind Pro
DefaultGroupName={#AppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=NetBindPro-{#AppVersion}-Setup
SetupIconFile=..\src\NetBindPro.UI\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
ChangesAssociations=no

; Registry key for Hook.exe path (used by Shell Extension)
[Registry]
Root: HKLM; Subkey: "SOFTWARE\NetBind Pro"; ValueType: string; \
  ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\NetBind Pro"; ValueType: string; \
  ValueName: "HookExecutable"; ValueData: "{app}\{#HookExeName}"; Flags: uninsdeletevalue

; Windows startup (optional, controlled by app settings)
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "NetBindPro"; \
  ValueData: """{app}\{#AppExeName}"" --minimized"; \
  Flags: uninsdeletevalue; Tasks: startup

[Tasks]
Name: "startup";    Description: "Start NetBind Pro with Windows"; GroupDescription: "Startup:"; Flags: unchecked
Name: "desktopicon"; Description: "Create a &desktop shortcut";   GroupDescription: "Additional icons:"

[Files]
; Self-contained build: ship the entire publish folder, including the bundled
; .NET 8 Desktop Runtime, the x64/x86 Hook engines and all dependencies. No
; separate .NET install is required on the target machine.
Source: "{#SourceDir}\*"; DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";                FileName: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}";      FileName: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}";        FileName: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}";          FileName: "{app}\{#AppExeName}"; Parameters: "--minimized"; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill running instances before uninstalling
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM {#AppExeName} 2>NUL"; Flags: runhidden; RunOnceId: "KillApp"

; No [Code] .NET runtime check needed — the runtime is bundled (self-contained).
