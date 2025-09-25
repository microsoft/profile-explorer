# ProfileExplorer.Mcp

Model Context Protocol (MCP) server enabling AI agents to interact with Profile Explorer.

## Overview

This project provides an MCP server that enables AI agent interactions with Profile Explorer. It allows users to perform profiling tasks through conversational AI by exposing Profile Explorer's core functionality as MCP tools. Users can ask questions like "load the Chrome process from this trace file" or "show me the slowest functions in ntdll.dll" and have them executed directly.

## Architecture

The project consists of:

- **ProfileExplorerMcpServer**: Main server class that handles MCP protocol communication
- **ProfileExplorerTools**: Static class containing MCP tool implementations
- **IMcpActionExecutor**: Interface for UI action execution (implemented by the UI project)
- **ProfilerStatus**: Data model for UI status information
- **McpServerConfiguration**: Helper for server initialization
- **MockMcpActionExecutor**: Mock implementation for testing

## Available MCP Tools

The server provides several MCP tools that enable AI agent profiling workflows:

### Core Profiling Operations
- `OpenTrace(profileFilePath, processNameOrId)` - Load a trace file and process based on agent-friendly descriptions (e.g., "Chrome", "the main app process", "PID 1234")
- `GetAvailableProcesses(profileFilePath, minWeightPercentage?, topCount?)` - Discover what processes are available in a trace file
- `GetAvailableFunctions(moduleName?, minSelfTimePercentage?, minTotalTimePercentage?, topCount?, sortBySelfTime?)` - Find functions based on agent queries like "hottest functions in kernel32" or "CPU-intensive functions"
- `GetAvailableBinaries(minTimePercentage?, minTimeMs?, topCount?)` - Identify which binaries/DLLs are consuming the most time

### Analysis and Deep Dive
- `GetFunctionAssembly(functionName)` - Retrieve assembly code for detailed function analysis
- `GetHelp()` - Get information about available capabilities

## AI Agent Workflow Examples

Instead of manually navigating UI menus and dialogs, users can accomplish profiling tasks through AI agent conversation:

**Example 1: Performance Investigation**
- User: "I have high CPU usage, can you help me analyze trace.etl?"
- AI: Uses `GetAvailableProcesses` → `OpenTrace` → `GetAvailableFunctions` to identify hotspots
- User: "What's consuming the most time in the kernel?"  
- AI: Uses `GetAvailableFunctions(moduleName: "ntdll.dll", topCount: 10)` to show kernel bottlenecks

**Example 2: Specific Function Analysis**
- User: "Show me the assembly for the NtReadFile function"
- AI: Uses `GetFunctionAssembly("NtReadFile")` to retrieve and display the code

**Example 3: Process Discovery** 
- User: "What processes were active during this trace?"
- AI: Uses `GetAvailableProcesses` to show all processes with their CPU usage percentages

## Usage

### Standalone Testing
```bash
cd src/ProfileExplorer.Mcp
dotnet run
```

### Integration with AI Assistants
The UI project should:
1. Implement `IMcpActionExecutor` interface to bridge MCP calls to Profile Explorer functionality
2. Initialize the server using `McpServerConfiguration.StartServerWithExecutorAsync(executor)` in `App.OnStartup`

Once integrated, users can interact with Profile Explorer through AI agents via AI assistants that support MCP.

## Implementation Notes

- All tool methods return JSON-serialized responses optimized for AI interpretation
- The server handles ambiguous agent queries (e.g., "Chrome process" maps to appropriate process selection)
- Error handling provides human-readable messages that AI assistants can relay to users
- The server uses stdio transport for MCP communication with AI systems
- UI thread marshaling should be handled by the executor implementation


For detailed implementation guidance, see the `IMcpActionExecutor` interface and `ProfileExplorerMcpServer` class.
