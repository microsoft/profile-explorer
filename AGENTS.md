# Agent Instructions for Profile Explorer

## Building

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
