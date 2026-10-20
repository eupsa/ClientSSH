; Inno Setup script. Build with installer\build.ps1 (it publishes the app and makes the wizard images first).
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "ClientSSH"
#define PublishDir "..\artifacts\publish"

[Setup]
AppId={{6B0E5C1A-3D52-4F0B-9C55-1F7A2E8D4C10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ClientSSH
AppPublisherURL=https://github.com/eupsa/client-ssh
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
; Per-user install: no admin prompt, and the user's data (%LOCALAPPDATA%\ClientSSH) is never touched.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=ClientSSH-Setup-{#AppVersion}
SetupIconFile=..\brand\ico\ClientSSH.ico
UninstallDisplayIcon={app}\{#AppName}.exe
WizardImageFile=..\artifacts\wizard\side.bmp
WizardSmallImageFile=..\artifacts\wizard\small.bmp
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
LicenseFile=..\LICENSE
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppName}.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppName}.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppName}.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The "start with Windows" entry of the unpackaged build lives in the user's Run key.
Filename: "reg.exe"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v {#AppName} /f"; Flags: runhidden
