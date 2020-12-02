set MSVC_ROOT=%1
set OWNER=%2
mkdir package
powershell -ExecutionPolicy bypass -File %MSVC_ROOT%\infra\nuget\Generate-Package.ps1 -NuGet %MSVC_ROOT%\src\tools\nuget\NuGet.exe -Who %OWNER% -Enlistment %cd%\publish -NuSpecTemplate "%MSVC_ROOT%\infra\nuget\MSVC.Tools.IRExplorer.nuspec.template"  -Version 1.2.5 -Dest %cd%\package
