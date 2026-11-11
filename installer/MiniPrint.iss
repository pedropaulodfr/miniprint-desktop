#define AppName "MiniPrint Desktop"
; A versão vem de scripts\build-release.ps1 (/DAppVersion=...), que a lê de
; Directory.Build.props. Os valores abaixo são só o padrão para compilar à mão.
#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#ifndef AppVersionQuad
  #define AppVersionQuad "1.0.1.0"
#endif
#define AppPublisher "MiniPrint Desktop"
#define AppExeName "MiniPrint.exe"
#define AppId "{{A7F3C2E1-5B4D-4E8A-9C61-2D7F0B3E8A54}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\MiniPrint Desktop
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=MiniPrint-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0.19041
LicenseFile=..\LICENSE.txt
VersionInfoVersion={#AppVersionQuad}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Instalador do MiniPrint Desktop
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\singlefile\MiniPrint.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\tools\miniprint-diag.exe"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\exemplos\*.json"; DestDir: "{app}\modelos"; Flags: ignoreversion
Source: "..\assets\logo-256.png"; DestDir: "{app}\assets"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Ferramenta de diagnóstico"; Filename: "{app}\tools\miniprint-diag.exe"
Name: "{group}\Documentação"; Filename: "{app}\docs"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tools"
