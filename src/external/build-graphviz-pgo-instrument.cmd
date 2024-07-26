cd graphviz
set PATH=%PATH%;%cd%\windows\dependencies\graphviz-build-utilities;%cd%\windows\dependencies\graphviz-build-utilities\winflexbison
rmdir /s /q build
mkdir build

cd build
cmake -G "Visual Studio 17 2022" -A x64 -DCMAKE_BUILD_TYPE=Release  -Denable_sharp=OFF -Dwith_zlib=OFF -Dwith_smyrna=OFF -Dwith_expat=OFF -Dwith_gvedit=OFF -DCMAKE_CXX_FLAGS="/MP /GL" -DCMAKE_SHARED_LINKER_FLAGS="/INCREMENTAL:NO /LTCG /GENPROFILE" ../.
cmake --build .  --config Release -j 16
cd ..
cd ..