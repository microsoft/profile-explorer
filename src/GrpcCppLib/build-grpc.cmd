set VCPCKG_PATH=%1
pushd %VCPCKG_PATH%
vcpkg.exe install grpc:x64-windows
vcpkg.exe install grpc:x86-windows
popd