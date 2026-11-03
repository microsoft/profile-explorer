# Copyright (c) Microsoft Corporation.
# Licensed under the MIT license.
# Run from a Visual Studio x64 (or x86) developer PowerShell.
$ErrorActionPreference = 'Stop'
$output = Join-Path ([IO.Path]::GetTempPath()) ("PEXPipeTests-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $output | Out-Null
try {
  $source = Join-Path $PSScriptRoot 'PipeValidationTests.cpp'
  & cl /nologo /O2 /DNDEBUG /DUNICODE /D_UNICODE /DNOMINMAX /EHsc /std:c++17 `
    $source "/Fo$output\tests.obj" "/Fe$output\tests.exe"
  if ($LASTEXITCODE -ne 0) { throw "Native pipe test compilation failed: $LASTEXITCODE" }
  $process = New-Object System.Diagnostics.Process
  $process.StartInfo.FileName = "$output\tests.exe"
  $process.StartInfo.UseShellExecute = $false
  $process.Start() | Out-Null
  if (!$process.WaitForExit(30000)) {
    Stop-Process -Id $process.Id
    throw 'Native pipe tests timed out'
  }
  if ($process.ExitCode -ne 0) { throw "Native pipe tests failed: $($process.ExitCode)" }
}
finally {
  Remove-Item -LiteralPath $output -Recurse -Force
}
