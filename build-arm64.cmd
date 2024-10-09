@echo off
setlocal

set _BUILD_TARGET="src\ProfileExplorerUI\ProfileExplorerUI.csproj"
set _FRAMEWORK_PATH=net8.0-windows
set _PROFILER_PATH="src\ManagedProfiler"
set _EXTERNALS_PATH="src\external"
set _EXTERNALS_PATH_ARM64="..\..\src\external\arm64"
set _RESOURCES_PATH="..\..\resources"

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

rem Update git submodules for external projects
git submodule update --init --recursive

rem Build main project
dotnet restore %_BUILD_TARGET%
dotnet build -c %_BUILD_CONFIG% %_BUILD_TARGET% /p:Platform=AnyCPU

for /f "delims=" %%i in ('"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -all -prerelease -property installationPath') do set _VS=%%i
set _VS_ENV=%_VS%\VC\Auxiliary\Build\vcvarsamd64_arm64.bat
call "%_VS_ENV%"

rem Build external projects
pushd %_EXTERNALS_PATH%
call build-external-arm64.cmd
popd

rem Build managed profiler
msbuild %_PROFILER_PATH%\ManagedProfiler.vcxproj /t:Rebuild /p:Configuration=Release /p:Platform=arm64
copy %_PROFILER_PATH%\arm64\Release\ManagedProfiler.dll %_OUT_PATH%

rem Copy over native DLLs and other resources
xcopy %_RESOURCES_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_EXTERNALS_PATH_ARM64%\*.dll %_OUT_PATH% /i /c /y
xcopy %_EXTERNALS_PATH_ARM64%\config6 %_OUT_PATH% /i /c /y
xcopy %_EXTERNALS_PATH%\tree-sitter\build_arm64\*.dll %_OUT_PATH% /i /c /y
copy %_EXTERNALS_PATH%\capstone\build_arm64\Release\capstone.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\cmd\dot\Release\dot.exe %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\lib\cdt\Release\cdt.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\lib\cgraph\Release\cgraph.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\lib\gvc\Release\gvc.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\lib\pathplan\Release\pathplan.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\lib\xdot\Release\xdot.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\plugin\core\Release\gvplugin_core.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\plugin\dot_layout\Release\gvplugin_dot_layout.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\windows\dependencies\libraries\vcpkg\installed\x64-windows\bin\zlib1.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\windows\dependencies\libraries\vcpkg\installed\x64-windows\bin\libexpat.dll %_OUT_PATH%

rem Register msdia140.dll
regsvr32 /s %_OUT_PATH%\msdia140.dll