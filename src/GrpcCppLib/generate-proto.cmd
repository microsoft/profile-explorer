set PROTOBUF_PATH=C:\tools\vcpkg\installed\x64-windows\tools\protobuf
set GRPC_PATH=C:\tools\vcpkg\installed\x64-windows\tools\grpc
%PROTOBUF_PATH%\protoc.exe --cpp_out=. --proto_path=..\ClientServer ..\ClientServer\DebugService.proto
%PROTOBUF_PATH%\protoc.exe --grpc_out=. --plugin=protoc-gen-grpc=%GRPC_PATH%\grpc_cpp_plugin.exe --proto_path=..\ClientServer ..\ClientServer\DebugService.proto