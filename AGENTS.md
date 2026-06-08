# Agent Instructions for Profile Explorer

## Git Policy

**NEVER commit or git add without explicit user permission.** When asked for a commit message, provide the message text only - do not execute `git commit` or `git add`.

## Building

**IMPORTANT: Always use Release configuration (`-c Release`). The user runs Release builds, not Debug.**

### Before Building
Always check if ProfileExplorer is running before attempting a build - it locks the DLLs:

```bash
tasklist /FI "IMAGENAME eq ProfileExplorer.exe" 2>nul | find /i "ProfileExplorer"
```

If running, ask the user to close it before building.

### Build Commands
```bash
# Full UI build (Release)
dotnet build "C:/src/profile-explorer/src/ProfileExplorerUI/ProfileExplorerUI.csproj" -c Release

# Core library only (faster, for checking compilation)
dotnet build "C:/src/profile-explorer/src/ProfileExplorerCore/ProfileExplorerCore.csproj" -c Release
```

### Iterative Development
For faster iteration when ProfileExplorer is running:
1. Build just the Core project first to check for compilation errors
2. Ask user to close ProfileExplorer
3. Build the full UI project
4. User can relaunch and test

The Core project build is much faster (~2-3s) and doesn't require ProfileExplorer to be closed.

## Bumping the App Version

The app version lives in 3 files that must stay in lockstep (history shows they've drifted apart and needed follow-up commits). Use the bump script — never edit these by hand:

```powershell
# Print current version across all files
.\scripts\Bump-Version.ps1 -Show

# Bump components (1.2.1 -> 1.2.2 / 1.3.0 / 2.0.0)
.\scripts\Bump-Version.ps1 -Bump Patch
.\scripts\Bump-Version.ps1 -Bump Minor
.\scripts\Bump-Version.ps1 -Bump Major

# Set an explicit version
.\scripts\Bump-Version.ps1 -Version 1.5.0

# Preview without writing
.\scripts\Bump-Version.ps1 -Bump Patch -WhatIf
```

The script refuses to bump when the files disagree on the current version; pass `-Version X.Y.Z -Force` to force a known-good value. See the script's header comment for full help.

The files it updates:
- `src/ProfileExplorerUI/ProfileExplorerUI.csproj` (`AssemblyVersion`, `FileVersion`, `Version`)
- `installer/x64/prepare-installer.cmd`
- `installer/arm64/prepare-installer.cmd`

## Diagnostic Logging

Enable diagnostic logging with:
```powershell
$env:PROFILE_EXPLORER_DEBUG = "1"
```

Logs are written to `%TEMP%\ProfileExplorer_Diagnostic_*.log`

### Analyzing Logs
Use the analysis script (don't read raw logs - they're ~12MB):
```powershell
C:\src\profile-explorer\scripts\Analyze-DiagnosticLog.ps1 -LogPath <path-to-log>
```

Or grep for specific patterns:
```bash
grep -i "pattern" <log-file> | head -30
```

## Key Files

### Symbol Loading Performance
- `ETWProfileDataProvider.cs` - Main symbol loading orchestration (~lines 759-1150)
- `SymbolFileSourceSettings.cs` - Symbol settings including timeouts, filters, caching
- `PEBinaryInfoProvider.cs` - Binary file downloading
- `PDBDebugInfoProvider.cs` - PDB file downloading

### Important Settings (SymbolFileSourceSettings)
- `SymbolServerTimeoutSeconds` - Normal timeout (default: 10s)
- `BellwetherTestEnabled` - Test symbol server health before bulk downloads
- `BellwetherTimeoutSeconds` - Timeout for bellwether test (default: 5s)
- `DegradedTimeoutSeconds` - Reduced timeout when server is slow (default: 3s)
- `RejectPreviouslyFailedFiles` - Negative caching for failed downloads
- `WindowsPathFilterEnabled` - Skip non-Windows binaries
- `CompanyFilterEnabled` - Only load symbols for specific companies (e.g., Microsoft)
