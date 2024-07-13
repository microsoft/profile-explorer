cd capstone
git submodule update --init --recursive
rmdir /s /q build
mkdir build
cd build
cmake -G "Visual Studio 17 2022" -A x64 -DBUILD_SHARED_LIBS=1 -DCAPSTONE_BUILD_STATIC_RUNTIME=1 -DCMAKE_BUILD_TYPE=Release -DCMAKE_CXX_FLAGS="/MP /GL" -DCMAKE_SHARED_LINKER_FLAGS="/LTCG" ../.
cmake --build .  --config Release -j 16
cd ..

rmdir /s /q build_static
mkdir build_static
cd build_static
cmake -G "Visual Studio 17 2022" -A x64 -DBUILD_SHARED_LIBS=0 -DCAPSTONE_BUILD_STATIC_RUNTIME=1 -DCMAKE_BUILD_TYPE=Release -DCMAKE_CXX_FLAGS="/MP /GL" -DCMAKE_SHARED_LINKER_FLAGS="/LTCG" ../.
cmake --build .  --config Release -j 16
cd ..