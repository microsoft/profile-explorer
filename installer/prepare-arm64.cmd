set _GRAPHVIZ_PATH="..\src\external\GraphViz"
set %_VERSION="0.8.3"
set _SETUP_PATH="C:\Program Files (x86)\Inno Setup 6"
set _PUBLISH_PATH="publish-arm64"
set _BUILD_TARGET="..\src\IRExplorerUI\IRExplorerUI.csproj"
set _EXTERNALS_PATH="..\src\external\arm64"
set _RESOURCES_PATH="..\resources"
set _OUT_PATH="out-arm64"

rd %_OUT_PATH% /s /q
rd %_PUBLISH_PATH% /s /q
mkdir %_OUT_PATH%

call %_EXTERNALS_PATH%\build-external-arm64.cmd
dotnet publish -c "Release" -r win-arm64 --self-contained true --output %_PUBLISH_PATH% %_BUILD_TARGET%

xcopy %_GRAPHVIZ_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_PUBLISH_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_RESOURCES_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_EXTERNALS_PATH% %_OUT_PATH% /i /c /y
xcopy %_EXTERNALS_PATH%\capstone\build_arm64\Release\capstone.dll %_OUT_PATH% /i /c /y

pushd %_OUT_PATH%
dot.exe -c
popd

%_SETUP_PATH%\iscc.exe installer-arm64.iss /DAPP_VERSION=%_VERSION% /O%cd%
