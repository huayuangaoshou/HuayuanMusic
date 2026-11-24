; Inno Setup script for HuayuanMusic
; Build with Inno Setup 6 (ISCC.exe), or via app-m223/bundle.ps1.

#define MyAppName "HuayuanMusic"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "HuayuanMusic"
#define MyAppExeName "HuayuanMusic.P17.Nightflight.exe"

[Setup]
AppId={{7F2A5E1D-4C8B-4A3D-9B1E-2D6C0F8E3A51}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\HuayuanMusic
DefaultGroupName=HuayuanMusic
DisableProgramGroupPage=yes
; User music data (downloads, playlists, history) lives under the profile and is
; never touched by uninstall, so upgrades and removals preserve the user's library.
PrivilegesRequired=admin
OutputDir=artifacts\release-build
OutputBaseFilename=HuayuanMusic-Setup-v{#MyAppVersion}
SetupIconFile=src\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Smoothly close any running instance during upgrade/uninstall so files are never locked.
CloseApplications=yes
CloseApplicationsFilter=*.exe
; Single-instance mutex matches Program.cs ("Local\HuayuanMusic.M223.Desktop").
AppMutex=Local\HuayuanMusic.M223.Desktop
; Simplified Chinese installer wizard (single language, no language dialog)
ShowLanguageDialog=no
[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "artifacts\release-build\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\HuayuanMusic"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall HuayuanMusic"; Filename: "{uninstallexe}"
Name: "{autodesktop}\HuayuanMusic"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,HuayuanMusic}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
