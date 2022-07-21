set _GRAPHVIZ_PATH="C:\Program Files\Graphviz\bin"
set %_VERSION="0.8.3"
set _SETUP_PATH="C:\Program Files (x86)\Inno Setup 6"
set _PUBLISH_PATH="publish"
set _BUILD_TARGET="..\src\IRExplorerUI\IRExplorerUI.csproj"
set _EXTERNALS_PATH="..\src\external"
set _RESOURCES_PATH="..\resources"
set _OUT_PATH="out"

rd %_OUT_PATH% /s /q
rd %_PUBLISH_PATH% /s /q
mkdir %_OUT_PATH%

dotnet publish -c Release -r win-x64 --output %_PUBLISH_PATH% %_BUILD_TARGET%

xcopy %_GRAPHVIZ_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_PUBLISH_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_RESOURCES_PATH% %_OUT_PATH% /i /c /e /y
xcopy %_EXTERNALS_PATH% %_OUT_PATH% /i /c /y

pushd %_OUT_PATH%
dot.exe -c
popd

%_SETUP_PATH%\iscc.exe installer.iss /DAPP_VERSION=%_VERSION% /O%cd%
