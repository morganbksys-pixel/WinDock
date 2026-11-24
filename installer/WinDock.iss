; Compile with ISCC /DAppVersion=0.0.20 installer\WinDock.iss after publishing the portable folder.
#ifndef AppVersion
  #error Supply the published AppVersion
#endif
#define Payload "..\artifacts\WinDock-" + AppVersion + "-win-x64"

[Setup]
AppId=WinDock
AppName=WinDock
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}.0
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
DefaultDirName={localappdata}\Programs\WinDock
DisableDirPage=yes
DisableProgramGroupPage=yes
AllowNoIcons=yes
UninstallDisplayIcon={app}\WinDock.exe
OutputDir=..\artifacts
OutputBaseFilename=WinDock-{#AppVersion}-Setup-win-x64
SetupIconFile=..\src\WinDock\Assets\WinDock.ico
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
RestartApplications=no

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Files]
Source: "{#Payload}\*"; Excludes: "\README.md,\QUICKSTART.txt"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\QUICKSTART.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\WinDock"; Filename: "{app}\WinDock.exe"; WorkingDir: "{app}"; Check: not WizardNoIcons
Name: "{userstartup}\WinDock"; Filename: "{app}\WinDock.exe"; WorkingDir: "{app}"; Check: (not WizardNoIcons) and FileExists(ExpandConstant('{userstartup}\WinDock.lnk'))

[Run]
Filename: "{app}\WinDock.exe"; Description: "Lancer WinDock"; Flags: nowait postinstall skipifsilent
