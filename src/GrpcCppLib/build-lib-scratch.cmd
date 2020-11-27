set VCPCKG_PATH=%cd%\vcpkg
rd /s /q %VCPCKG_PATH%
git clone https://github.com/microsoft/vcpkg %VCPCKG_PATH%
pushd %VCPCKG_PATH%
call bootstrap-vcpkg.bat
popd

call build-lib.cmd %VCPCKG_PATH%