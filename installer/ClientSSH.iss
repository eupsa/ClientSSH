; Inno Setup script. Build with installer\build.ps1 (it publishes the app and makes the wizard images first).
#ifndef AppVersion
  #define AppVersion "0.6.1"
#endif
#define AppName "ClientSSH"
#define PublishDir "..\artifacts\publish"

[Setup]
AppId={{6B0E5C1A-3D52-4F0B-9C55-1F7A2E8D4C10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=ClientSSH
AppPublisherURL=https://github.com/eupsa/ClientSSH
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
; The wizard follows the Windows display language; English is the fallback. Inno Setup ships no
; Chinese or Hindi translation, so those users get English.
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppName}.exe"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppName}.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppName}.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall

[UninstallRun]
; The "start with Windows" entry of the unpackaged build lives in the user's Run key.
Filename: "reg.exe"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v {#AppName} /f"; RunOnceId: "RemoveStartupEntry"; Flags: runhidden
