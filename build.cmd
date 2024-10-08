set _BUILD_TARGET="src\ProfileExplorerUI\ProfileExplorerUI.csproj"
set _FRAMEWORK_PATH=net8.0-windows
set _PROFILER_PATH="src\ManagedProfiler"
set _EXTERNALS_PATH="src\external"
set _RESOURCES_PATH="..\..\resources"

if "%1"=="" (
    echo Usage: build.bat [debug|release]
    exit /b 1
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
set _VS_ENV=%_VS%\VC\Auxiliary\Build\vcvars64.bat
call "%_VS_ENV%"

rem Build external projects
pushd %_EXTERNALS_PATH%
call build-external.cmd
popd

rem Build managed profiler
msbuild %_PROFILER_PATH%\ManagedProfiler.vcxproj /t:Rebuild /p:_CONFIG=Release /p:Platform=x64

rem Copy over native DLLs and other resources
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

rem Register msdia140.dll
regsvr32 /s %_OUT_PATH%\msdia140.dll