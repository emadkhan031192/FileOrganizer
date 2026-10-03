; FileOrganizer.iss — Inno Setup script: builds "File Organizer Setup.exe"
; Requires Inno Setup 6 (https://jrsoftware.org/isinfo.php) on a Windows machine.
; Build:  ISCC.exe installer\FileOrganizer.iss
; CI does this automatically (see .github/workflows/build.yml) after publishing the app.

#define MyAppName "File Organizer — Explorer Companion"
#define MyAppVersion "1.0.0"
#define MyAppExeName "FileOrganizer.exe"
; Publish output expected here (dotnet publish -o installer\publish):
#define PublishDir "publish"

[Setup]
AppId={{8E4B0C5A-3F6D-4C2A-9B1E-FILEORGANIZER001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\FileOrganizer
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=File Organizer Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\scripts\Install-ContextMenu.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\Uninstall-ContextMenu.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\File Organizer"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\File Organizer"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "contextmenu"; Description: "Add 'Move to Organizer' to the Explorer right-click menu"; GroupDescription: "Explorer integration:"
Name: "startup"; Description: "Start File Organizer (Quick Bar) with Windows"; GroupDescription: "Explorer integration:"

[Run]
; Register the context menu with the destinations from the user's config.
; The app creates its config on first run, so launch it once hidden first is NOT done here:
; instead the app's own first-run experience offers Settings -> Install Context Menu.
; If a config already exists (upgrade), refresh the menu entries silently:
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Install-ContextMenu.ps1"" -ExePath ""{app}\{#MyAppExeName}"""; Flags: runhidden; Tasks: contextmenu; Check: ConfigExists
Filename: "{app}\{#MyAppExeName}"; Description: "Launch File Organizer"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FileOrganizer"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Uninstall-ContextMenu.ps1"""; Flags: runhidden; RunOnceId: "RemoveContextMenu"

[Code]
function ConfigExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{userappdata}\FileOrganizer\config.json'));
end;
