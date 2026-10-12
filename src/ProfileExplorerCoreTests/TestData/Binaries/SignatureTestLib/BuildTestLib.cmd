@echo off
REM Copyright (c) Microsoft Corporation.
REM Licensed under the MIT license.
REM
REM Rebuilds the PdbFunctionSignatureTests fixture (testlib.dll/testlib.pdb x64,
REM testlib32.dll/testlib32.pdb x86, testlibarm64.dll/testlibarm64.pdb ARM64) from testlib.cpp,
REM each with a full private PDB via the local MSVC toolchain. Requires Visual Studio (or the
REM Build Tools) with the x86/x64 and ARM64 VC++ component installed.
REM
REM Usage: run from a plain (non-Developer) cmd.exe or PowerShell prompt:
REM   BuildTestLib.cmd "C:\Program Files\Microsoft Visual Studio\<edition>\<version>"
REM If no argument is given, it tries to auto-detect the VS install via vswhere.

setlocal
set VSROOT=%~1

if "%VSROOT%"=="" (
  for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath`) do set VSROOT=%%i
)

if "%VSROOT%"=="" (
  echo Could not locate a Visual Studio installation. Pass the install root as an argument.
  exit /b 1
)

echo Using Visual Studio install: %VSROOT%
cd /d "%~dp0"

echo.
echo === Building x64 (testlib.dll / testlib.pdb) ===
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /LD /Zi /Od testlib.cpp /Fe:testlib.dll /link /DEBUG:FULL
if errorlevel 1 goto :error

echo.
echo === Building x86 (testlib32.dll / testlib32.pdb) ===
REM __stdcall/__fastcall are meaningfully distinct from __cdecl only on x86 -- this build is what
REM PdbFunctionSignatureTests.TryGetFunctionSignature_X86Build_DistinctCallingConventionsAreReported
REM exercises (x64 collapses all three keywords to a single unified calling convention).
call "%VSROOT%\VC\Auxiliary\Build\vcvars32.bat" >nul
cl /nologo /LD /Zi /Od testlib.cpp /Fe:testlib32.dll /link /DEBUG:FULL
if errorlevel 1 goto :error

echo.
echo === Building ARM64 (testlibarm64.dll / testlibarm64.pdb) ===
REM Requires the "MSVC ARM64 build tools" (Microsoft.VisualStudio.Component.VC.Tools.ARM64)
REM component. Install via: vs_installer.exe modify --installPath "%VSROOT%" ^
REM   --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --quiet --norestart
call "%VSROOT%\VC\Auxiliary\Build\vcvarsx86_arm64.bat" >nul
cl /nologo /LD /Zi /Od testlib.cpp /Fe:testlibarm64.dll /link /DEBUG:FULL
if errorlevel 1 goto :error

echo.
echo All builds succeeded. Copy the .dll/.pdb pairs into:
echo   src\ProfileExplorerCoreTests\TestData\Binaries\SignatureTestLib\
echo   src\ProfileExplorerCoreTests\TestData\Symbols\SignatureTestLib\
exit /b 0

:error
echo Build failed.
exit /b 1
