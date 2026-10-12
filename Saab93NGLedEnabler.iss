#define MyAppName "Saab 93 NG Led Enabler"
#define MyAppVersion "1.0.5"
#define MyAppPublisher "BCR Auto"
#define MyAppExeName "Saab93NGLedEnabler.exe"
#define MyAppId "BCRAuto.Saab93NGLedEnabler"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf32}\BCR Auto\Saab 93 NG Led Enabler
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=InstallerOutput
OutputBaseFilename=Saab93NGLedEnabler-{#MyAppVersion}-Setup
SetupIconFile=Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x86 x64
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "publish\win-x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
