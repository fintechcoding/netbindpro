; NetBind Pro - Inno Setup Installer Script
; Build with Inno Setup 6.x: https://jrsoftware.org/isinfo.php
; Run: ISCC.exe NetBindPro.Setup.iss

#define AppName        "NetBind Pro"
#define AppVersion     "2.3.7"
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
; Make sure a running instance (incl. the system-tray process) can't lock files
; during the upgrade — otherwise a half-replaced install fails to launch.
CloseApplications=force
RestartApplications=no

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
; shellexec so the requireAdministrator app launches via ShellExecute (handles the
; UAC elevation). Without it, Inno runs the post-install launch as the original,
; non-elevated user and CreateProcess fails with code 740 (elevation required).
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; \
  Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Kill running instances before uninstalling
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM {#AppExeName} 2>NUL"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
// Forcibly terminate any running NetBind Pro processes before copying files,
// so a tray instance from a previous version can't leave a half-replaced,
// non-launchable install. Runs just before the [Files] step.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/C taskkill /F /IM {#AppExeName} /IM {#HookExeName} /IM {#HookExeName86} 2>NUL',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
