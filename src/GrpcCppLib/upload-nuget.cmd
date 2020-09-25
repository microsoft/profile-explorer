mkdir package
powershell -ExecutionPolicy bypass -File C:\msvc\infra\nuget\Generate-Package.ps1 -NuGet C:\msvc\src\tools\nuget\NuGet.exe -Who gratilup -Enlistment %cd%\publish -NuSpecTemplate "C:\msvc\infra\nuget\MSVC.Tools.IRExplorer.nuspec.template"  -Version 1.2.0 -Dest %cd%\package
