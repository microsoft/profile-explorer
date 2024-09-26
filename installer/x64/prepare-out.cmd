set _PUBLISH_PATH="publish"
set _BUILD_TARGET="..\..\src\ProfileExplorerUI\ProfileExplorerUI.csproj"
set _PROFILER_PATH="..\..\src\ManagedProfiler"
set _EXTERNALS_PATH="..\..\src\external"
set _RESOURCES_PATH="..\..\resources"
set _REPO_PATH="..\.."
set _OUT_PATH="out"

rd %_OUT_PATH% /s /q
rd %_PUBLISH_PATH% /s /q
mkdir %_OUT_PATH%

dotnet publish -c "Release" -r win-x64 --self-contained --verbosity diagnostic --output %_PUBLISH_PATH% %_BUILD_TARGET%

for /f "delims=" %%i in ('"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -all -prerelease -property installationPath') do set _VS=%%i
set _VS_ENV=%_VS%\VC\Auxiliary\Build\vcvars64.bat
call "%_VS_ENV%"

pushd %_EXTERNALS_PATH%
call build-external.cmd
popd

msbuild %_PROFILER_PATH%\ManagedProfiler.vcxproj /t:Rebuild /p:Configuration=Release /p:Platform=x64
copy %_PROFILER_PATH%\x64\Release\ManagedProfiler.dll %_OUT_PATH%

xcopy %_PUBLISH_PATH% %_OUT_PATH% /i /c /e /y
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
copy %_REPO_PATH%\NOTICE.md %_OUT_PATH%