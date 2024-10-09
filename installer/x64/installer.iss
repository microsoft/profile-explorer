; -- Example1.iss --
; Demonstrates copying 3 files and creating an icon.

; SEE THE DOCUMENTATION FOR DETAILS ON CREATING .ISS SCRIPT FILES!
                                              
#include "../environment.iss"

#define MyAppName "Profile Explorer"
#define MyAppExeName "ProfileExplorer.exe"

[Setup]
AppName=Profile Explorer
AppVersion={#APP_VERSION}
WizardStyle=modern
DisableDirPage=no
DefaultDirName={autopf}\Profile Explorer
DefaultGroupName=Profile Explorer
UninstallDisplayIcon={app}\ProfileExplorer.exe
Compression=lzma2
SolidCompression=yes
OutputDir=userdocs:ProfileExplorer
ChangesAssociations = yes
ChangesEnvironment = yes
OutputBaseFilename=profile_explorer_installer_{#APP_VERSION}_x64

[Registry]
Root: HKCR; Subkey: "{#MyAppName}";                     ValueData: "Program {#MyAppName}";  Flags: uninsdeletekey;   ValueType: string;  ValueName: ""
Root: HKCR; Subkey: "{#MyAppName}\DefaultIcon";             ValueData: "{app}\{#MyAppExeName},0";               ValueType: string;  ValueName: ""
Root: HKCR; Subkey: "{#MyAppName}\shell\open\command";  ValueData: """{app}\{#MyAppExeName}"" ""%1""";  ValueType: string;  ValueName: ""

[Files]
Source: ".\out\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Profile Explorer"; Filename: "{app}\ProfileExplorer.exe"

[Tasks]
Name: envPath; Description: "Add to PATH env. variable as ProfileExplorer.exe" 

[Run]
Filename: "{sys}\Regsvr32.exe"; Parameters: "/s msdia140.dll"; WorkingDir: "{app}"; Flags: shellexec runhidden; 

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
    if (CurStep = ssPostInstall) and IsTaskSelected('envPath')
    then EnvAddPath(ExpandConstant('{app}'));
end;
