set _BUILD_TARGET="src\ProfileExplorerUI\ProfileExplorerUI.csproj"
set _PROFILER_PATH="src\ManagedProfiler"
set _EXTERNALS_PATH="src\external"
set _OUT_PATH="build"

git submodule update --init --recursive
dotnet publish -c "Release" -r win-x64 --self-contained  --output %_OUT_PATH% %_BUILD_TARGET%

for /f "delims=" %%i in ('"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -all -prerelease -property installationPath') do set _VS=%%i
set _VS_ENV=%_VS%\VC\Auxiliary\Build\vcvars64.bat
call "%_VS_ENV%"

pushd %_EXTERNALS_PATH%
call build-external.cmd
popd

msbuild %_PROFILER_PATH%\ManagedProfiler.vcxproj /t:Rebuild /p:Configuration=Release /p:Platform=x64
