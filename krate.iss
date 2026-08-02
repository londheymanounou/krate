[Setup]
AppName=Krate Toolkit
AppVersion=1.0.0
AppPublisher=Krate
DefaultDirName={autopf}\Krate
DefaultGroupName=Krate
DisableProgramGroupPage=yes
OutputBaseFilename=KrateSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\KRATE.exe

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "D:\crate\publish2\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{autoprograms}\Krate"; Filename: "{app}\KRATE.exe"
Name: "{autodesktop}\Krate"; Filename: "{app}\KRATE.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\KRATE.exe"; Description: "{cm:LaunchProgram,Krate}"; Flags: nowait postinstall skipifsilent
