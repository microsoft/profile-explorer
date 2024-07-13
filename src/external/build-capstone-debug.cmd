cd capstone
git submodule update --init --recursive
rmdir /s /q build
mkdir build
cd build
cmake -G "Visual Studio 17 2022" -A x64 -DBUILD_SHARED_LIBS=1 -DCAPSTONE_BUILD_STATIC_RUNTIME=1 -DCMAKE_BUILD_TYPE=Debug -DCMAKE_CXX_FLAGS="/MP" ../.
cmake --build .  --config Debug -j 16
cd ..
