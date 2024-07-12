set _GRAPHVIZ_PATH="..\..\src\external\GraphViz"
set _PUBLISH_PATH="publish"
set _BUILD_TARGET="..\..\src\IRExplorerUI\IRExplorerUI.csproj"
set _EXTERNALS_PATH="..\..\src\external"
set _RESOURCES_PATH="..\..\resources"
set _OUT_PATH="out"

rd %_OUT_PATH% /s /q
rd %_PUBLISH_PATH% /s /q
mkdir %_OUT_PATH%

pushd %_EXTERNALS_PATH%
call build-capstone.cmd
popd

dotnet publish -c "Release" -r win-x64 --self-contained  --output %_PUBLISH_PATH% %_BUILD_TARGET%

xcopy %_GRAPHVIZ_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_PUBLISH_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_RESOURCES_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_EXTERNALS_PATH% %_OUT_PATH% /i /c /y
copy %_EXTERNALS_PATH%\capstone\build\Release\capstone.dll %_OUT_PATH%

pushd %_OUT_PATH%
dot.exe -c
popd