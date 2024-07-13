set _GRAPHVIZ_PATH="..\..\src\external\GraphViz"
set _PUBLISH_PATH="publish"
set _BUILD_TARGET="..\..\src\IRExplorerUI\IRExplorerUI.csproj"
set _EXTERNALS_PATH="..\..\src\external\arm64"
set _RESOURCES_PATH="..\..\resources"
set _OUT_PATH="out"

rd %_OUT_PATH% /s /q
rd %_PUBLISH_PATH% /s /q
mkdir %_OUT_PATH%

pushd %_EXTERNALS_PATH%
call build-external-arm64.cmd
popd

dotnet publish -c "Release" -r win-arm64 --self-contained true --output %_PUBLISH_PATH% %_BUILD_TARGET%

xcopy %_GRAPHVIZ_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_PUBLISH_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_RESOURCES_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_EXTERNALS_PATH% %_OUT_PATH% /i /c /y
copy %_EXTERNALS_PATH%\capstone\build_arm64\Release\capstone.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\capstone\build\Release\capstone.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\cmd\dot\Release\dot.exe %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\lib\cdt\Release\cdt.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\lib\cgraph\Release\cgraph.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\lib\gvc\Release\gvc.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\lib\pathplan\Release\pathplan.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\lib\xdot\Release\xdot.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\plugin\core\Release\gvplugin_core.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build\plugin\core\Release\gvplugin_dot_layout.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\windows\dependencies\libraries\vcpkg\installed\x64-windows\bin\zlib1.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\windows\dependencies\libraries\vcpkg\installed\x64-windows\bin\libexpat.dll %_OUT_PATH%

pushd %_OUT_PATH%
dot.exe -c
popd