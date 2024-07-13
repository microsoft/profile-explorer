set _PGO_WEIGHTS=%~dp0\pgo\weights

cd graphviz
git submodule update --init --recursive
set PATH=%PATH%;%cd%\windows\dependencies\graphviz-build-utilities;%cd%\windows\dependencies\graphviz-build-utilities\winflexbison
rmdir /s /q build
mkdir build

cd build
cmake -G "Visual Studio 17 2022" -A x64 -DCMAKE_BUILD_TYPE=Release -DCMAKE_CXX_FLAGS="/MP /GL" -DCMAKE_SHARED_LINKER_FLAGS="/INCREMENTAL:NO /LTCG /USEPROFILE" ../.

mkdir %~dp0\graphviz\build\cmd\dot\Release
mkdir %~dp0\graphviz\build\lib\cdt\Release
mkdir %~dp0\graphviz\build\lib\cgraph\Release
mkdir %~dp0\graphviz\build\lib\gvc\Release
mkdir %~dp0\graphviz\build\lib\pathplan\Release
mkdir %~dp0\graphviz\build\lib\xdot\Release
mkdir %~dp0\graphviz\build\plugin\core\Release
mkdir %~dp0\graphviz\build\plugin\dot_layout\Release

rem copy %_PGO_WEIGHTS%\dot.pgd %~dp0\graphviz\build\cmd\dot\Release
copy %_PGO_WEIGHTS%\cdt.pgd %~dp0\graphviz\build\lib\cdt\Release
copy %_PGO_WEIGHTS%\cgraph.pgd %~dp0\graphviz\build\lib\cgraph\Release
copy %_PGO_WEIGHTS%\gvc.pgd %~dp0\graphviz\build\lib\gvc\Release
copy %_PGO_WEIGHTS%\pathplan.pgd %~dp0\graphviz\build\lib\pathplan\Release
copy %_PGO_WEIGHTS%\xdot.pgd %~dp0\graphviz\build\lib\xdot\Release
copy %_PGO_WEIGHTS%\gvplugin_core.pgd %~dp0\graphviz\build\plugin\core\Release
copy %_PGO_WEIGHTS%\gvplugin_dot_layout.pgd %~dp0\graphviz\build\plugin\dot_layout\Release

cmake --build .  --config Release -j 16
cd ..
cd ..