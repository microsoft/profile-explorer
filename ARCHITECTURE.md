# Profile Explorer — Architecture

## Summary

Profile Explorer is a Windows desktop application (WPF) for viewing CPU profiling traces collected via Event Tracing for Windows (ETW). It presents the hottest parts of a profiled application through an interactive UI: function list, flame graph, call tree, timeline, assembly view, and source view. A companion headless MCP server exposes the same profiling engine to AI assistants over the Model Context Protocol.

The application was originally a compiler IR viewer and retains the ability to parse assembly into an internal IR, enabling control-flow graph visualization and interactive disassembly navigation.

---

## High-Level Architecture

```
+-------------------------+    stdio (JSON-RPC)    +-----------------------------+
| GitHub Copilot CLI      | <--------------------> | ProfileExplorer.McpServer   |
| (AI Assistant)          |                        | (headless, .NET 8)          |
+-------------------------+                        +-------------+---------------+
                                                                 |
                                                   ProfileExplorerCore (engine)
                                                                 |
                                          +----------+-----------+-----------+
                                          |          |           |           |
                                       ETW       Symbols     Disasm      CallTree
                                     (TraceEvent) (PDB/DIA)  (Capstone)  (in-mem)
                                          |
                                          v
                                   +-------------+
                                   | .etl trace  |
                                   | files       |
                                   +-------------+

+-------------------------+
| ProfileExplorerUI       |    Same engine (ProfileExplorerCore)
| (WPF desktop app)       |--> with full interactive UI: flame graph,
|                         |    call tree, assembly, source, timeline
+-------------------------+
```

---

## Projects

| Project | Type | Description |
|---------|------|-------------|
| **ProfileExplorerUI** | WPF app (.NET 8) | Main desktop application. All UI panels, session management, profiling views, scripting, VS extension integration. |
| **ProfileExplorerCore** | Class library | UI-independent engine: ETW trace loading, profile data model, call tree construction, IR parsing, binary analysis, symbol resolution, Capstone disassembly, Tree-sitter parsing, graph algorithms. |
| **ProfileExplorer.McpServer** | Console app (.NET 8) | Headless MCP server exposing profiling tools over stdio. Uses `ProfileExplorerCore` directly — no UI dependency. **This is the active MCP server.** |
| **ProfileExplorer.Mcp** | Class library + mock exe | Original MCP server designed to be embedded inside the WPF app via `IMcpActionExecutor`. Not used by the headless server. See [MCP: Embedded vs Headless](#mcp-embedded-vs-headless) below. |
| **ProfileExplorerCoreTests** | xUnit tests | Unit tests for the core library. |
| **ProfileExplorerUITests** | xUnit tests | Unit tests for UI logic. |
| **ManagedProfiler** | .NET profiler | JIT profiler extension for capturing JIT output assembly. |
| **PDBViewer** | WinForms utility | Small tool for displaying PDB debug info file contents. |
| **GrpcLib** | Protobuf library | GRPC protocol definitions for VS extension communication. |
| **VSExtension** | VSIX | Visual Studio extension that connects to the desktop app. |

### External Dependencies (built from source)

| Submodule | Purpose |
|-----------|---------|
| `src/external/capstone` | Capstone disassembly framework (x64/ARM64 instruction decoding) |
| `src/external/graphviz` | Graphviz graph layout engine (control-flow graph visualization) |
| `src/external/tree-sitter` | Tree-sitter parser generator (C/C++, C#, Rust source parsing) |
| `src/external/TreeListView` | WPF tree list view control |

---

## Core Engine (ProfileExplorerCore)

### Profile Data Pipeline

```
.etl file
    |
    v
ETWEventProcessor          Reads raw ETW events via TraceEvent library
    |                      Builds per-process sample lists and stack traces
    v
ETWProfileDataProvider     Orchestrates full profile loading:
    |                      - Process enumeration (BuildProcessSummary)
    |                      - Trace loading (LoadTraceAsync)
    |                      - Symbol resolution (parallel PDB download)
    |                      - Source line mapping
    v
ProfileData                In-memory profile model:
    |                      - FunctionProfiles (per-function weight/time)
    |                      - InstructionWeight (per-instruction hotspots)
    |                      - Modules list
    |                      - CallTree (rooted call graph)
    v
ProfileCallTree            Full call tree with:
    |                      - GetSortedCallTreeNodes (all instances of a function)
    |                      - GetCombinedCallTreeNode (merged view)
    |                      - GetBacktrace (full stack for a node)
    |                      - Caller/callee aggregation
    v
UI views / MCP tools       Consume the profile model for display or JSON output
```

### Key Data Types

| Type | Location | Role |
|------|----------|------|
| `ProfileData` | Profile/Data/ProfileData.cs | Root container: function profiles, modules, call tree, metadata |
| `FunctionProfileData` | Profile/Data/FunctionProfileData.cs | Per-function: inclusive/exclusive weight, instruction weights, debug info |
| `ProfileCallTree` | Profile/CallTree/ProfileCallTree.cs | Full call tree with aggregation and backtrace support |
| `ProfileCallTreeNode` | Profile/CallTree/ProfileCallTreeNode.cs | Single node: function ref, weight, children, callers |
| `ETWEventProcessor` | Profile/ETW/ETWEventProcessor.cs | Low-level ETW event reading and sample extraction |
| `ETWProfileDataProvider` | Profile/ETW/ETWProfileDataProvider.cs | High-level trace loading orchestration (~1000 lines) |
| `IRTextFunction` | IRTextFunction.cs | Function identity (name + module) used as dictionary key |

### Symbol Resolution

Symbols are resolved in parallel during trace loading:
1. `_NT_SYMBOL_PATH` environment variable is read for symbol server configuration
2. A bellwether test probes symbol server health before bulk downloads
3. PDB files are downloaded in parallel with configurable timeouts (normal, degraded)
4. Failed downloads are negatively cached to avoid retry storms
5. Source line info is resolved on-demand per function (not bulk-loaded)

Key settings in `SymbolFileSourceSettings`:
- `SymbolServerTimeoutSeconds` (default 10s)
- `BellwetherTestEnabled` / `BellwetherTimeoutSeconds` (5s)
- `DegradedTimeoutSeconds` (3s)
- `RejectPreviouslyFailedFiles` (negative cache)
- `WindowsPathFilterEnabled`, `CompanyFilterEnabled` (skip irrelevant binaries)

---

## MCP: Embedded vs Headless

There are two MCP-related projects in the repo. Only the headless server is actively used.

| Aspect | `ProfileExplorer.Mcp` (embedded) | `ProfileExplorer.McpServer` (headless) |
|--------|----------------------------------|----------------------------------------|
| **Architecture** | Library defining `IMcpActionExecutor` interface. The WPF app implements this interface (`McpActionExecutor.cs`) and routes MCP calls through the UI dispatcher. | Standalone console exe. Calls `ProfileExplorerCore` APIs directly — no WPF, no dispatcher. |
| **Status** | **Not used.** Deadlocks when the WPF `Dispatcher.Invoke` blocks the MCP thread waiting for UI operations. | **Active.** All AI assistant workflows use this server. |
| **Coupling** | Loose — interface-based. Requires UI to implement the executor. | Tight — directly uses `ETWProfileDataProvider`, `ProfileData`, `CallTree`, `Disassembler`. |
| **Tools** | 6 tools via interface (`OpenTrace`, `GetStatus`, `GetAvailableProcesses`, `GetAvailableFunctions`, `GetAvailableBinaries`, `GetFunctionAssemblyToFile`) | 9 tools with richer features: multi-process selection, custom symbol paths, caller/callee with FunctionPct, CloseTrace for state reset, Capstone disassembly with per-instruction source lines and inline function info. |
| **State** | Stateless — each call is dispatched to the UI which holds state. | Stateful — `ProfileSession` singleton holds loaded profile, provider, and symbol settings. |

The embedded approach (`ProfileExplorer.Mcp`) was the repo's original attempt at MCP support. It works conceptually but in practice the WPF dispatcher serialization creates deadlocks: MCP tool calls block on `Dispatcher.Invoke`, but the dispatcher is waiting for previous work to complete. The headless server (`ProfileExplorer.McpServer`) solves this by bypassing the UI entirely.

---

## MCP Server (ProfileExplorer.McpServer)

The active MCP server. Exposes the profiling engine over MCP stdio transport. No WPF, no GUI — just the core engine.

### Registration

Registered in `~/.copilot/mcp-config.json` as `profile-explorer`:
```json
{
  "type": "stdio",
  "command": "D:\\repos\\profile-explorer\\src\\ProfileExplorer.McpServer\\bin\\Release\\net8.0\\ProfileExplorer.McpServer.exe",
  "env": {
    "_NT_SYMBOL_PATH": "cache*c:\\programdata\\dbg\\sym;SRV*c:\\programdata\\dbg\\sym*https://symweb.azurefd.net"
  }
}
```

### Tools

| Tool | Description |
|------|-------------|
| `GetAvailableProcesses` | List processes in a trace file with weight percentages. Filtering by min weight %, top N. |
| `OpenTrace` | Start async loading of a trace for a specific process. Optional `symbolPath` for custom/private PDBs. Returns immediately. |
| `GetTraceLoadStatus` | Poll loading progress. Returns Loading/Complete/Failed. |
| `CloseTrace` | Close the current trace and fully reset session state (including symbol caches). Required before loading a new trace. |
| `GetAvailableFunctions` | List functions with self-time/total-time %. Filter by module, min %, top N. Uses PDB-resolved names. |
| `GetAvailableBinaries` | List modules/DLLs aggregated by CPU time. Filter by min %, top N. |
| `GetFunctionAssembly` | Instruction-level hotspots with Capstone disassembly, source line mapping, and inline function info. |
| `GetFunctionCallerCallee` | Callers, callees, and full backtraces for a function. Includes both trace-relative (`WeightPct`) and function-relative (`FunctionPct`) percentages. |
| `GetHelp` | Usage workflow documentation. |

### Session Model

`ProfileSession` is a static singleton holding loaded state:
- `LoadedProfile` — the `ProfileData` after successful load
- `Provider` — kept alive for on-demand source line resolution and disassembly
- `PendingLoad` — `Task<ProfileData?>` for async loading
- `TotalWeight` — pre-computed total weight for percentage calculations
- `LoadSemaphore` — concurrency guard preventing overlapping trace loads
- `SymbolSettings` — `SymbolFileSourceSettings` used for the current load (needed for disassembly)
- `LoadException` — captured exception if loading fails
- `LoadedFilePath` / `PendingFilePath` / `PendingProcessId` — track current and pending load targets
- `LoadedProcessIds` — list of process IDs included in the loaded profile
- `Report` — symbol resolution report with per-module resolution stats

`Reset()` clears all state including static symbol caches (`PDBDebugInfoProvider.ClearResolvedCache()`), ensuring no stale negative caching between loads with different symbol paths.

### Function Name Resolution

`IRTextFunction.Name` is frozen as a hex placeholder (e.g., `28BC63`) during ETL parsing when PDB symbols are not yet loaded. The actual resolved name (e.g., `ExpWaitForSpinLockSharedAndAcquire`) lives on `FunctionDebugInfo.Name`, accessed via `FunctionProfileData.FunctionDebugInfo`.

Two `ResolveFunctionName` helpers handle the lookup:
- `ResolveFunctionName(IRTextFunction)` — looks up `profile.FunctionProfiles[func].FunctionDebugInfo?.Name`
- `ResolveFunctionName(ProfileCallTreeNode)` — uses `node.FunctionDebugInfo?.Name`

`FindFunction` searches by resolved PDB name first, falling back to the hex name.

### FunctionPct vs WeightPct

Call stack data includes two percentage types:
- `WeightPct` / `InclusivePct` — percentage of entire trace time (weight ÷ totalTraceWeight × 100)
- `FunctionPct` — percentage of the target function's time (weight ÷ functionWeight × 100), matching the GUI's drill-down view

### Diagnostic Logging

Structured diagnostic logging via `DiagnosticLogger` traces MCP operations, function resolution, and symbol loading. Log files are written alongside the trace file with timestamps.

### Typical Workflow

1. `GetAvailableProcesses(filePath)` — discover processes in a trace
2. `OpenTrace(filePath, processId, symbolPath?)` — start loading (async), optionally with custom symbol path
3. `GetTraceLoadStatus()` — poll until Complete
4. `GetAvailableFunctions(topCount: 20)` — find hot functions
5. `GetFunctionAssembly(name)` — drill into instruction-level hotspots
6. `GetFunctionCallerCallee(name)` — understand call context (with FunctionPct for drill-down)
7. `CloseTrace()` — reset state before loading a different trace

---

## Desktop Application (ProfileExplorerUI)

The WPF application provides the full interactive experience:

### UI Architecture

- **MainWindow** — split across partial classes for different concerns:
  - `MainWindow.xaml.cs` — core window logic
  - `MainWindowProfiling.cs` — profile loading and view management
  - `MainWindowSession.cs` — session save/restore
  - `MainWindowPanels.cs` — panel layout management
  - `MainWindowDiff.cs` — diff/comparison mode
  - `MainWindowDebugRpc.cs` — VS extension RPC handling
- **Panels/** — individual UI views (flame graph, call tree, function list, assembly, source, timeline, etc.)
- **Profile/** — profiling-specific UI components
- **Session/** — session persistence
- **Mcp/** — `McpActionExecutor.cs` bridges MCP commands to UI actions (used by the embedded MCP path)
- **Scripting/** — user scripting support

### Performance Design

The app is designed for near-instant interaction even with very large traces (10+ GB ETL files):
- Multi-threaded trace processing and filtering
- Async UI updates that don't block the main thread
- On-demand symbol resolution (only for viewed functions)
- Parallel PDB downloads with health-aware timeouts
- Incremental rendering for large data sets

---

## Build System

| Command | Description |
|---------|-------------|
| `build.cmd [debug\|release]` | x64 build (main project + external deps). Requires admin for msdia140.dll registration. |
| `build-arm64.cmd [debug\|release]` | Native ARM64 build. |
| `installer\x64\prepare.cmd` | Publish + create x64 installer (InnoSetup). |
| `installer\arm64\prepare.cmd` | Publish + create ARM64 installer. |
| `dotnet build src\ProfileExplorerUI\ProfileExplorerUI.csproj -c Release` | Build UI project only (after initial `build.cmd`). |
| `dotnet build src\ProfileExplorerCore\ProfileExplorerCore.csproj -c Release` | Build core library only (fast, ~2-3s). |

**Prerequisites**: Visual Studio 2022 (.NET desktop + C++ desktop workloads), .NET 8.0 SDK.

Output: `src\ProfileExplorerUI\bin\[Debug|Release]\net8.0-windows\ProfileExplorer.exe`

---

## Dependencies

| Package | Version | Used By | Purpose |
|---------|---------|---------|---------|
| `ModelContextProtocol` | 0.3.0-preview.4 | McpServer | MCP protocol (stdio transport, tool registration) |
| `Microsoft.Extensions.Hosting` | 9.0.0 | McpServer | .NET hosting / DI |
| `Microsoft.Extensions.Logging` | 9.0.0 | McpServer | Logging infrastructure |

The core and UI projects have additional dependencies (TraceEvent for ETW, DIA SDK for PDB reading, etc.) managed through the solution and build scripts.

---

## Known Limitations

| Limitation | Impact |
|-----------|--------|
| Windows only | ETW and WPF are Windows-specific technologies |
| One trace at a time (MCP) | The headless MCP server holds a single `ProfileSession` — call `CloseTrace()` before loading a new trace |
| Symbol server latency | First load of a trace can be slow if PDBs need downloading from symbol servers |
| Large trace memory | Very large traces consume significant memory for the in-memory call tree and profile data |
| Release builds required | The user's MCP config points to Release output; Debug builds won't be picked up |

---

## Repository Layout

```
profile-explorer/
  ARCHITECTURE.md          This file
  AGENTS.md                Agent/Copilot instructions
  CLAUDE.md                Claude Code instructions
  README.md                User-facing documentation
  build.cmd                x64 build script
  build-arm64.cmd          ARM64 build script
  src/
    ProfileExplorer.sln    Solution file
    ProfileExplorerUI/     WPF desktop application
    ProfileExplorerCore/   Engine library (UI-independent)
    ProfileExplorer.McpServer/  Headless MCP server (active)
    ProfileExplorer.Mcp/   Embedded MCP server (original, not used — deadlocks on WPF dispatcher)
    ProfileExplorerCoreTests/   Core unit tests
    ProfileExplorerUITests/     UI unit tests
    ManagedProfiler/       .NET JIT profiler
    PDBViewer/             PDB viewer utility
    GrpcLib/               VS extension protocol
    VSExtension/           VS extension
    external/              Git submodules (capstone, graphviz, tree-sitter)
  docs/                    Documentation site source
  installer/               Installer scripts (InnoSetup)
  scripts/                 Utility scripts (log analysis, etc.)
```
