; Simon Flow installer (Inno Setup 6)
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{8F3C2B1A-5D4E-4C7A-9B2F-1E6D7A9C4B31}
AppName=Simon Flow
AppVersion={#MyAppVersion}
AppVerName=Simon Flow {#MyAppVersion}
AppPublisher=Simon Flow
AppPublisherURL=https://simon-margalit.github.io/simon-flow/
AppSupportURL=https://github.com/Simon-Margalit/simon-flow
DefaultDirName={localappdata}\Programs\Simon Flow
DefaultGroupName=Simon Flow
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=SimonFlow-Setup
SetupIconFile=..\AppIcon.ico
UninstallDisplayIcon={app}\SimonFlow.exe
UninstallDisplayName=Simon Flow
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Add a Simon Flow shortcut to the desktop"

[Files]
Source: "..\SimonFlow.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "setup-whisper.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Simon Flow"; Filename: "{app}\SimonFlow.exe"
Name: "{autodesktop}\Simon Flow"; Filename: "{app}\SimonFlow.exe"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\setup-whisper.ps1"""; StatusMsg: "Downloading the speech engine (about 150 MB). This can take a few minutes..."; Flags: runhidden waituntilterminated
Filename: "{app}\SimonFlow.exe"; Description: "Open Simon Flow"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/im SimonFlow.exe /f"; Flags: runhidden; RunOnceId: "CloseSimonFlow"
