; NetBind Pro - Inno Setup Installer Script
; Build with Inno Setup 6.x: https://jrsoftware.org/isinfo.php
; Run: ISCC.exe NetBindPro.Setup.iss

#define AppName        "NetBind Pro"
#define AppVersion     "2.0.1"
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
; Main UI executable
Source: "{#SourceDir}\{#AppExeName}";         DestDir: "{app}"; Flags: ignoreversion
; Hook engine (x64)
Source: "{#SourceDir}\{#HookExeName}";        DestDir: "{app}"; Flags: ignoreversion
; Hook engine (x86) — required for injecting into 32-bit target processes
Source: "{#SourceDir}\{#HookExeName86}";      DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; .NET runtime dependencies
Source: "{#SourceDir}\*.dll";                  DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "{#SourceDir}\*.json";                 DestDir: "{app}"; Flags: ignoreversion

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

[Code]
// Check for .NET 8 runtime before installing
function IsDotNet8Installed(): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(HKLM,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
    '8.0.0', Version);
  if not Result then
    Result := RegQueryStringValue(HKLM,
      'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
      '8.0.0', Version);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNet8Installed() then
  begin
    if MsgBox('.NET 8 Desktop Runtime is required but not installed.' + #13#10 +
              'Do you want to open the download page?',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open',
        'https://dotnet.microsoft.com/download/dotnet/8.0',
        '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end;
end;
