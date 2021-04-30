; -- Example1.iss --
; Demonstrates copying 3 files and creating an icon.

; SEE THE DOCUMENTATION FOR DETAILS ON CREATING .ISS SCRIPT FILES!
                                              
#include "environment.iss"

#define MyAppName "IR Explorer"
#define MyAppExeName "irexplorer.exe"

[Setup]
AppName=IR Explorer
AppVersion=0.7.9
WizardStyle=modern
DefaultDirName={autopf}\IR Explorer
DefaultGroupName=IR Explorer
UninstallDisplayIcon={app}\irexplorer.exe
Compression=lzma2
SolidCompression=yes
OutputDir=userdocs:IRExplorer
ChangesAssociations = yes
ChangesEnvironment = yes

[Registry]
Root: HKCR; Subkey: ".irx";                             ValueData: "{#MyAppName}";          Flags: uninsdeletevalue; ValueType: string;  ValueName: ""
Root: HKCR; Subkey: "{#MyAppName}";                     ValueData: "Program {#MyAppName}";  Flags: uninsdeletekey;   ValueType: string;  ValueName: ""
Root: HKCR; Subkey: "{#MyAppName}\DefaultIcon";             ValueData: "{app}\{#MyAppExeName},0";               ValueType: string;  ValueName: ""
Root: HKCR; Subkey: "{#MyAppName}\shell\open\command";  ValueData: """{app}\{#MyAppExeName}"" ""%1""";  ValueType: string;  ValueName: ""

[Files]
Source: "d:\ir-explorer\publish\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\IR Explorer"; Filename: "{app}\irexplorer.exe"

[Tasks]
Name: envPath; Description: "Add to PATH env. variable as irexplorer.exe" 

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
    if (CurStep = ssPostInstall) and IsTaskSelected('envPath')
    then EnvAddPath(ExpandConstant('{app}'));
end;
