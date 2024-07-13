cd graphviz
git submodule update --init --recursive
set PATH=%PATH%;%~dp0\windows\dependencies\graphviz-build-utilities;%~dp0\windows\dependencies\graphviz-build-utilities\winflexbison
rmdir /s /q build
mkdir build

cd build
cmake -G "Visual Studio 17 2022" -A x64 -DCMAKE_BUILD_TYPE=Release -DCMAKE_CXX_FLAGS="/MP /GL" -DCMAKE_SHARED_LINKER_FLAGS="/INCREMENTAL:NO /LTCG /GENPROFILE" ../.
cmake --build .  --config Release -j 16
cd ..
cd ..