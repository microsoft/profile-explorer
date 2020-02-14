; -- Example1.iss --
; Demonstrates copying 3 files and creating an icon.

; SEE THE DOCUMENTATION FOR DETAILS ON CREATING .ISS SCRIPT FILES!
                                              
#include "environment.iss"

#define MyAppName "IR Explorer"
#define MyAppExeName "irexplorer.exe"

[Setup]
AppName=IR Explorer
AppVersion=0.4.2
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
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\config6"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\ann.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\cdt.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\cgraph.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\fontconfig.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\fontconfig_fix.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\freetype6.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\glut32.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\gvc.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\gvplugin_core.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\gvplugin_dot_layout.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\gvplugin_gd.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\gvplugin_gdiplus.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\gvplugin_neato_layout.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\gvplugin_pango.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\iconv.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\intl.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\jpeg62.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libatk-1.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libcairo-2.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libexpat.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libexpat-1.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libfontconfig-1.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libfreetype-6.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libgdk_pixbuf-2.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libgdkglext-win32-1.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libgdk-win32-2.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libgio-2.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libglade-2.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libglib-2.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libgmodule-2.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libgobject-2.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libgthread-2.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libgtkglext-win32-1.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libgtk-win32-2.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libltdl-3.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libpango-1.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libpangocairo-1.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libpangoft2-1.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libpangowin32-1.0-0.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libpng12.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libpng14-14.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\libxml2.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\ltdl.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\Pathplan.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\QtCore4.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\QtGui4.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\vmalloc.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\zlib1.dll"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\irexplorer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\dot.exe"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\dotty.exe"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\irexplorer.pdb"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\utc.xshd"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\ir.xshd"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\documentStyles.xml"; DestDir: "{app}"
Source: "C:\personal\projects\compiler_studio\Client\bin\publish\themes\Selentic (UTC).xshd"; DestDir: "{app}\themes"; Flags: ignoreversion recursesubdirs createallsubdirs

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
