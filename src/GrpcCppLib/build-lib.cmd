set VCPCKG_PATH=%1
rd /s /q %cd%\GrpcCppLib

call build-grpc.cmd %VCPCKG_PATH%
call generate-proto.cmd %VCPCKG_PATH%

rem Build x64
msbuild GrpcCppLib.vcxproj /p:Configuration=ReleaseDLL /p:Platform=x64 /t:Rebuild /p:VCPKG_PATH=%VCPCKG_PATH%
copy %VCPCKG_PATH%\installed\x64-windows\bin\abseil_dll.dll %cd%\GrpcCppLib\x64\ReleaseDLL
copy %VCPCKG_PATH%\installed\x64-windows\bin\cares.dll %cd%\GrpcCppLib\x64\ReleaseDLL
copy %VCPCKG_PATH%\installed\x64-windows\bin\libcrypto-1_1-x64 %cd%\GrpcCppLib\x64\ReleaseDLL
copy %VCPCKG_PATH%\installed\x64-windows\bin\libprotobuf.dll %cd%\GrpcCppLib\x64\ReleaseDLL
copy %VCPCKG_PATH%\installed\x64-windows\bin\libprotobuf-lite.dll %cd%\GrpcCppLib\x64\ReleaseDLL
copy %VCPCKG_PATH%\installed\x64-windows\bin\libprotoc.dll %cd%\GrpcCppLib\x64\ReleaseDLL
copy %VCPCKG_PATH%\installed\x64-windows\bin\libssl-1_1-x64.dll %cd%\GrpcCppLib\x64\ReleaseDLL
copy %VCPCKG_PATH%\installed\x64-windows\bin\re2.dll %cd%\GrpcCppLib\x64\ReleaseDLL
copy %VCPCKG_PATH%\installed\x64-windows\bin\zlib1.dll %cd%\GrpcCppLib\x64\ReleaseDLL

rem Build x86
msbuild GrpcCppLib.vcxproj /p:Configuration=ReleaseDLL /p:Platform=Win32 /t:Rebuild /p:VCPKG_PATH=%VCPCKG_PATH%
copy %VCPCKG_PATH%\installed\x86-windows\bin\abseil_dll.dll %cd%\GrpcCppLib\ReleaseDLL
copy %VCPCKG_PATH%\installed\x86-windows\bin\cares.dll %cd%\GrpcCppLib\ReleaseDLL
copy %VCPCKG_PATH%\installed\x86-windows\bin\libcrypto-1_1 %cd%\GrpcCppLib\ReleaseDLL
copy %VCPCKG_PATH%\installed\x86-windows\bin\libprotobuf.dll %cd%\GrpcCppLib\ReleaseDLL
copy %VCPCKG_PATH%\installed\x86-windows\bin\libprotobuf-lite.dll %cd%\GrpcCppLib\ReleaseDLL
copy %VCPCKG_PATH%\installed\x86-windows\bin\libprotoc.dll %cd%\GrpcCppLib\ReleaseDLL 
copy %VCPCKG_PATH%\installed\x86-windows\bin\libssl-1_1.dll %cd%\GrpcCppLib\ReleaseDLL
copy %VCPCKG_PATH%\installed\x86-windows\bin\re2.dll %cd%\GrpcCppLib\ReleaseDLL
copy %VCPCKG_PATH%\installed\x86-windows\bin\zlib1.dll %cd%\GrpcCppLib\ReleaseDLL

call publish-lib.cmd