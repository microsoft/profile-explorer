# ProfileExplorer.Mcp

Model Context Protocol (MCP) server for Profile Explorer UI automation and analysis.

## Overview

This project provides an MCP server that enables programmatic access to Profile Explorer's UI automation. It exposes the 4 key UI entry points documented in `mcp-ui-entrypoints.md` as MCP tools, allowing external systems to automate the profile loading workflow.

## Architecture

The project consists of:

- **ProfileExplorerMcpServer**: Main server class that handles MCP protocol communication
- **ProfileExplorerTools**: Static class containing MCP tool implementations
- **IMcpActionExecutor**: Interface for UI action execution (implemented by the UI project)
- **ProfilerStatus**: Data model for UI status information
- **McpServerConfiguration**: Helper for server initialization
- **MockMcpActionExecutor**: Mock implementation for testing

## Available MCP Tools

Based on the 4 documented UI entry points in `mcp-ui-entrypoints.md`:

### UI Entry Point Tools
- `OpenLoadProfileDialog()` - Open the Profiling menu and Load Profile dialog (executes AppCommand.LoadProfile)
- `SetProfileFilePath(profileFilePath)` - Set the profile file path in Load Profile dialog (sets ProfileLoadWindow.ProfileFilePath)
- `SelectProcesses(processIds)` - Select process(es) in the process list (selects items in ProcessList)
- `ExecuteProfileLoad(useBackendDirectly?)` - Execute profile load operation (LoadButton_Click or MainWindow.LoadProfileData)

### Status and Information
- `GetProfilerStatus()` - Get the current status of the Profile Explorer UI
- `GetHelp()` - Get help information about available MCP commands

## Workflow Example

Typical sequence to load a profile:
1. `OpenLoadProfileDialog()` - Opens the Load Profile dialog
2. `SetProfileFilePath("path/to/trace.etl")` - Sets the trace file path, enumerates processes
3. `SelectProcesses([1234, 5678])` - Selects which processes to load
4. `ExecuteProfileLoad(true)` - Loads the profile using backend API (recommended) or `ExecuteProfileLoad(false)` for UI simulation

## Usage

### Standalone Testing
```bash
cd src/ProfileExplorer.Mcp
dotnet run
```

### Integration with UI
The UI project should:
1. Implement `IMcpActionExecutor` interface
2. Initialize the server using `McpServerConfiguration.StartServerWithExecutorAsync(executor)`
3. Enable MCP server startup via environment variable (e.g., `PROFILE_EXPLORER_ENABLE_MCP=true`)

## Implementation Notes

- All tool methods return JSON-serialized responses for consistent communication
- Error handling is implemented with try-catch blocks and structured error responses
- The server uses stdio transport for MCP communication
- UI thread marshaling should be handled by the executor implementation

## Dependencies

- ModelContextProtocol (0.3.0-preview.4)
- Microsoft.Extensions.Hosting (9.0.0)
- Microsoft.Extensions.Logging (9.0.0)

## Next Steps

1. Implement `McpActionExecutor` in the ProfileExplorerUI project
2. Add MCP server initialization to `App.OnStartup` with environment variable gating
3. Test integration with real UI actions
4. Add structured logging and error reporting

For detailed integration guidance, see `docs/mcp-ui-entrypoints.md`.
