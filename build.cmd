@echo off
setlocal

set _BUILD_TARGET="src\ProfileExplorerUI\ProfileExplorerUI.csproj"
set _FRAMEWORK_PATH=net8.0-windows
set _PROFILER_PATH="src\ManagedProfiler"
set _EXTERNALS_PATH="src\external"
set _RESOURCES_PATH="resources"

if "%1"=="" (
    echo "Usage: build.bat [debug|release]"
	echo "Defaulting to Release mode..."
)

set _CONFIG=%1

if /I "%_CONFIG%"=="debug" (
	set _BUILD_CONFIG=Debug
    echo Building in Debug mode...
) else if /I "%_CONFIG%"=="release" (
	set _BUILD_CONFIG=Release
    echo Building in Release mode...
) else (
    set _BUILD_CONFIG=Release
    echo Building in Release mode...
)

set _OUT_PATH="src\ProfileExplorerUI\bin\%_BUILD_CONFIG%\%_FRAMEWORK_PATH%"
echo %_OUT_PATH%

rem Build main project
dotnet restore %_BUILD_TARGET% || exit /b 1
dotnet build -c %_BUILD_CONFIG% %_BUILD_TARGET% /p:Platform=AnyCPU || exit /b 1

set _VS=
for /f "delims=" %%i in ('"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -all -prerelease -property installationPath') do set _VS=%%i
if not defined _VS (
    echo Visual Studio with C++ build tools was not found.
    exit /b 1
)
set _VS_ENV=%_VS%\VC\Auxiliary\Build\vcvars64.bat
set PATH=C:\Program Files (x86)\Microsoft Visual Studio\Installer;%PATH%
call "%_VS_ENV%" || exit /b 1

rem Build external projects
pushd %_EXTERNALS_PATH% || exit /b 1
call .\build-external.cmd
if errorlevel 1 (
    popd
    exit /b 1
)
popd

rem Build managed profiler
msbuild %_PROFILER_PATH%\ManagedProfiler.vcxproj /t:Rebuild /p:Configuration=%_BUILD_CONFIG% /p:Platform=x64 || exit /b 1

rem Copy over native DLLs and other resources
copy %_PROFILER_PATH%\x64\%_BUILD_CONFIG%\ManagedProfiler.dll %_OUT_PATH% || exit /b 1
xcopy %_RESOURCES_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_EXTERNALS_PATH%\config6 %_OUT_PATH% /i /c /y
xcopy %_EXTERNALS_PATH%\*.dll %_OUT_PATH% /i /c /y
xcopy %_EXTERNALS_PATH%\tree-sitter\build\*.dll %_OUT_PATH% /i /c /y
copy %_EXTERNALS_PATH%\capstone\build\Release\capstone.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\cmd\dot\Release\dot.exe %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\lib\cdt\Release\cdt.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\lib\cgraph\Release\cgraph.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\lib\gvc\Release\gvc.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\lib\pathplan\Release\pathplan.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\lib\xdot\Release\xdot.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\plugin\core\Release\gvplugin_core.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\plugin\dot_layout\Release\gvplugin_dot_layout.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\windows\dependencies\libraries\vcpkg\installed\x64-windows\bin\zlib1.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\windows\dependencies\libraries\vcpkg\installed\x64-windows\bin\libexpat.dll %_OUT_PATH%
