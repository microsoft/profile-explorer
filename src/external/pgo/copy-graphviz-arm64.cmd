set _EXTERNALS_PATH="%~dp0\.."
set _OUT_PATH="%~dp0\out_arm64"

rd %_OUT_PATH% /s /q
mkdir %_OUT_PATH%

copy %_EXTERNALS_PATH%\graphviz\build_arm64\cmd\dot\Release\* %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\lib\cdt\Release\* %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\lib\cgraph\Release\* %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\lib\gvc\Release\* %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\lib\pathplan\Release\* %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\lib\xdot\Release\* %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\plugin\core\Release\* %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\build_arm64\plugin\dot_layout\Release\* %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\windows\dependencies\libraries\vcpkg\installed\x64-windows\bin\zlib1.dll %_OUT_PATH%
copy %_EXTERNALS_PATH%\graphviz\windows\dependencies\libraries\vcpkg\installed\x64-windows\bin\libexpat.dll %_OUT_PATH%

cd %_OUT_PATH%
dot.exe -c
cd ..