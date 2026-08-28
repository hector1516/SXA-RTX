; SXA RTX Sync - Instalador Inno Setup
; Se genera con: .\publish.ps1  (busca ISCC.exe y compila si esta instalado)
; Instalacion por usuario en %LOCALAPPDATA% para que el auto-update desde
; GitHub funcione sin UAC (el updater remplaza archivos en esa carpeta).

#define MyAppName "SXA RTX Sync"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "SXA"
#define MyAppURL "https://github.com/hector1516/SXA-RTX"
#define MyAppExeName "SXA.RTX.Sync.Tray.exe"

[Setup]
AppId={{3B9A6F2E-7C4D-4E8A-9F1A-SXA-RTX-SYNC}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=artifacts\pkg
OutputBaseFilename=Setup_SXA_RTX_Sync_v{#MyAppVersion}
SetupIconFile=SXA-RTX-Sync\src\SXA.RTX.Sync.Tray\Assets\app.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
DisableDirPage=no
DisableProgramGroupPage=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Iniciar con Windows"; GroupDescription: "{cm:AdditionalIcons}";

[Files]
Source: "artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; No se incluye appsettings.json ni device.config a proposito: se conservan al actualizar

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
