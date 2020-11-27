rd /s /q %cd%\publish
set OUT_X86=%cd%\publish\x86
set OUT_X64=%cd%\publish\x64

mkdir %OUT_X86%
mkdir %OUT_X86%\lib
mkdir %OUT_X86%\dll

pushd %cd%\GrpcCppLib\ReleaseDLL
copy irexplorer.lib %OUT_X86%\lib
copy *.dll %OUT_X86%\dll
popd

mkdir %OUT_x64%
mkdir %OUT_x64%\lib
mkdir %OUT_x64%\dll

pushd %cd%\GrpcCppLib\x64\ReleaseDLL
copy irexplorer.lib %OUT_x64%\lib
copy *.dll %OUT_x64%\dll
popd
