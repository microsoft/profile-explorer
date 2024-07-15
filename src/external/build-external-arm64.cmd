for /f "delims=" %%i in ('"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -all -prerelease -property installationPath') do set _VS=%%i
set _VS_ENV=%_VS%\VC\Auxiliary\Build\vcvarsamd64_arm64.bat
call "%_VS_ENV%"
call build-capstone-arm64.cmd
call build-graphviz-pgo.cmd
call build-tree-sitter-arm64.cmd