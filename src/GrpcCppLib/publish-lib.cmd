set OUT_X86=%cd%\publish\x86
set OUT_X64=%cd%\publish\x64

mkdir %OUT_X86%
mkdir %OUT_X86%\lib
mkdir %OUT_X86%\dll

pushd ReleaseStaticLib
copy irexplorer.lib %OUT_X86%\lib
copy *.dll %OUT_X86%\dll
popd

mkdir %OUT_x64%
mkdir %OUT_x64%\lib
mkdir %OUT_x64%\dll

pushd x64\ReleaseStaticLib
copy irexplorer.lib %OUT_x64%\lib
copy *.dll %OUT_x64%\dll
popd
