# Claude Code Instructions for Profile Explorer

## Git Policy

**NEVER commit or git add without explicit user permission.** When asked for a commit message, provide the message text only - do not execute `git commit` or `git add`.

## Building

**IMPORTANT: Always use Release configuration (`-c Release`). The user runs Release builds, not Debug.**

```bash
# Full UI build (Release)
dotnet build "C:/src/profile-explorer/src/ProfileExplorerUI/ProfileExplorerUI.csproj" -c Release

# Core library only (faster, for checking compilation)
dotnet build "C:/src/profile-explorer/src/ProfileExplorerCore/ProfileExplorerCore.csproj" -c Release
```

### Before Building
Check if ProfileExplorer is running before attempting a build - it locks the DLLs:

```bash
tasklist /FI "IMAGENAME eq ProfileExplorer.exe" 2>nul | find /i "ProfileExplorer"
```

If running, ask the user to close it before building.

## See Also
See `AGENTS.md` for more detailed instructions including:
- Diagnostic logging setup
- Key files for symbol loading
- Important settings
