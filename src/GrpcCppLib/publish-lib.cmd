set VCPKG_LIBS_x86=C:\tools\vcpkg\installed\x86-windows\debug\lib
set VCPKG_LIBS_x64=C:\tools\vcpkg\installed\x64-windows\debug\lib
set VCPKG_DLLS_x86=C:\tools\vcpkg\installed\x86-windows\debug\bin
set VCPKG_DLLS_x64=C:\tools\vcpkg\installed\x64-windows\debug\bin
set OUT_X86=%cd%\publish\x86
set OUT_X64=%cd%\publish\x64

mkdir %OUT_X86%
mkdir %OUT_X86%\lib
mkdir %OUT_X86%\dll

pushd ReleaseStaticLib
copy irexplorer.lib %OUT_X86%\lib
lib.exe -out:%OUT_X86%\lib\irexplorer_deps.lib %VCPKG_LIBS_x86%\grpc.lib %VCPKG_LIBS_x86%\grpc++.lib %VCPKG_LIBS_x86%\abseil_dll.lib %VCPKG_LIBS_x86%\libssl.lib %VCPKG_LIBS_x86%\zlibd.lib %VCPKG_LIBS_x86%\libcrypto.lib %VCPKG_LIBS_x86%\libprotobufd.lib %VCPKG_LIBS_x86%\gpr.lib %VCPKG_LIBS_x86%\cares.lib %VCPKG_LIBS_x86%\upb.lib %VCPKG_LIBS_x86%\re2.lib %VCPKG_LIBS_x86%\address_sorting.lib
popd

copy %VCPKG_DLLS_x86%\abseil_dll.dll %OUT_X86%\dll
copy %VCPKG_DLLS_x86%\cares.dll %OUT_X86%\dll
copy %VCPKG_DLLS_x86%\libcrypto-1_1.dll %OUT_X86%\dll
copy %VCPKG_DLLS_x86%\libprotobufd.dll %OUT_X86%\dll
copy %VCPKG_DLLS_x86%\libprotobuf-lited.dll %OUT_X86%\dll
copy %VCPKG_DLLS_x86%\libprotocd.dll %OUT_X86%\dll
copy %VCPKG_DLLS_x86%\libssl-1_1.dll %OUT_X86%\dll
copy %VCPKG_DLLS_x86%\re2.dll %OUT_X86%\dll
copy %VCPKG_DLLS_x86%\zlibd1.dll %OUT_X86%\dll


mkdir %OUT_x64%
mkdir %OUT_x64%\lib
mkdir %OUT_x64%\dll

pushd x64\ReleaseStaticLib
copy irexplorer.lib %OUT_x64%\lib
lib.exe -out:%OUT_x64%\lib\irexplorer_deps.lib %VCPKG_LIBS_x64%\grpc.lib %VCPKG_LIBS_x64%\grpc++.lib %VCPKG_LIBS_x64%\abseil_dll.lib %VCPKG_LIBS_x64%\libssl.lib %VCPKG_LIBS_x64%\zlibd.lib %VCPKG_LIBS_x64%\libcrypto.lib %VCPKG_LIBS_x64%\libprotobufd.lib %VCPKG_LIBS_x64%\gpr.lib %VCPKG_LIBS_x64%\cares.lib %VCPKG_LIBS_x64%\upb.lib %VCPKG_LIBS_x64%\re2.lib %VCPKG_LIBS_x64%\address_sorting.lib
popd

copy %VCPKG_DLLS_x64%\abseil_dll.dll %OUT_x64%\dll
copy %VCPKG_DLLS_x64%\cares.dll %OUT_x64%\dll
copy %VCPKG_DLLS_x64%\libcrypto-1_1.dll %OUT_x64%\dll
copy %VCPKG_DLLS_x64%\libprotobufd.dll %OUT_x64%\dll
copy %VCPKG_DLLS_x64%\libprotobuf-lited.dll %OUT_x64%\dll
copy %VCPKG_DLLS_x64%\libprotocd.dll %OUT_x64%\dll
copy %VCPKG_DLLS_x64%\libssl-1_1.dll %OUT_x64%\dll
copy %VCPKG_DLLS_x64%\re2.dll %OUT_x64%\dll
copy %VCPKG_DLLS_x64%\zlibd1.dll %OUT_x64%\dll