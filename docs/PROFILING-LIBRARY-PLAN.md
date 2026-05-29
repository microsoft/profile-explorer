# ProfileExplorer.Profiling — Extraction Plan

## Goal

Extract Profile Explorer's core function-level profiling, symbol resolution, and disassembly logic into a standalone NuGet library (`ProfileExplorer.Profiling`) that can be consumed alongside DataLayer (or any other ETL sample source) without either package referencing the other.

## Motivation

BigRedPerfAI currently spawns Profile Explorer as a headless MCP server process to get per-instruction CPU profiling for hot functions. This works but requires:

- PE installed on the machine (or Azure Batch package)
- Process lifecycle management (spawn, MCP handshake, kill)
- Single-trace-at-a-time constraint (one PE process = one open trace)
- MCP JSON-RPC overhead for every function query

By extracting the core logic into a library, consumers can:

- Use DataLayer to load traces + CPU samples (no PE process needed)
- Use PE.Profiling for aggregation + disassembly (in-process, no IPC)
- Scale to multiple traces in parallel (no process singleton constraint)
- Mix and match: DataLayer samples, TraceEvent samples, or anything else

## Architecture: Side-by-Side Composition

```
┌─────────────────────────────────────────────────────────┐
│                      BigRedPerfAI                       │
│                                                         │
│  ┌──────────────────┐       ┌────────────────────────┐  │
│  │  DataLayer NuGet │       │  PE.Profiling NuGet    │  │
│  │                  │       │                        │  │
│  │  - Load ETL      │       │  - PDB symbol reader   │  │
│  │  - CPU samples   │  ───► │  - Function aggregation│  │
│  │  - Process/stack │ adapt │  - Instruction weights  │  │
│  │  - (no PDBs)     │       │  - Call tree builder   │  │
│  │                  │       │  - PMU counter support  │  │
│  └──────────────────┘       │  - Managed/.NET profiling│ │
│                             │  - Binary download     │  │
│         ~20 lines           │  - Capstone disassembly │  │
│       adapter code          │  - IP skid correction  │  │
│                             │  - Assembly annotation  │  │
│                             └────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

DataLayer and PE.Profiling have **zero references to each other**. The consumer writes thin adapter classes (~20-50 lines) to bridge DataLayer's `ICpuSample` to PE.Profiling's `IProfileSample` interface.

## Why PE.Profiling Must Own Symbol Resolution (Not Delegate to DataLayer)

DataLayer's symbol system uses **symcache** — a pre-processed, compressed format optimized for WPA stack resolution. Symcache is lossy compared to full PDBs:

| Data                                  | Full PDB | Symcache |
|---------------------------------------|----------|----------|
| Function name → RVA mapping           | ✅        | ✅        |
| Source file + line (entry point)      | ✅        | ⚠️ Partial |
| **Per-instruction source line mapping** | ✅        | ❌ No     |
| Function size / boundaries            | ✅        | ⚠️ Approx  |
| Inline frame tree                     | ✅        | ⚠️ Limited |
| Local variable layout                 | ✅        | ❌ No     |
| Type info for disassembly annotation  | ✅        | ❌ No     |

The critical gap is **per-instruction source line mapping**. When PE annotates disassembly, it maps each instruction offset to a source line via DIA's `findLinesByRVA`. Symcache only knows "function X starts at line Y" — it cannot attribute individual instructions to source lines, which is exactly what BigRedPerfAI embeds in AI prompts.

Additionally, DataLayer's `UseSymbols().LoadSymbolsAsync()` may be configured to only create symcache without retaining PDBs, or may skip modules the consumer cares about. PE.Profiling cannot depend on those PDBs being available.

By including PE's own `PDBDebugInfoProvider`, the library:

- Downloads PDBs directly using GUID+Age from ETW image events
- Full DIA-based source line mapping is always available
- No dependency on DataLayer's symbol loading behavior
- Works identically with any sample source (DataLayer, TraceEvent, or other)

## What BigRedPerfAI Currently Gets from PE

BigRedPerfAI calls PE via MCP to get annotated disassembly for hot functions. The output looks like:

```
180069B9F:    call  CIconCache::GetIcon    [Time(%): 26.78%, Time: 58.18 ms]
180069BA4:    test  eax, eax               [Time(%):  1.02%, Time:  2.22 ms]
180069BA6:    je    0x180069BC0
```

This is parsed into `HotLine` records:

```csharp
record HotLine(int LineNumber, int SampleCount, double Percent, string SourceText, long InstructionOffset);
```

Only the **CritPathCpuPlugin** uses this. The AI prompt includes these hot lines so the model knows exactly which instructions are expensive.

## PE.Profiling Public API

### Input Abstractions

PE.Profiling defines its own input interfaces — no dependency on DataLayer or TraceEvent:

```csharp
namespace ProfileExplorer.Profiling;

/// A single CPU sample with an instruction pointer and weight.
public interface IProfileSample
{
    long InstructionPointer { get; }
    TimeSpan Weight { get; }
    int ProcessId { get; }
    int ThreadId { get; }
    string? ImageName { get; }           // Module that owns the IP
    long ImageBaseAddress { get; }       // Module base address
    IReadOnlyList<long>? StackFrames { get; }  // Full stack IPs, leaf-first (optional)
}

/// Describes a loaded module/image with its PDB identity.
public interface IProfileImage
{
    string ImageName { get; }
    long BaseAddress { get; }
    int Size { get; }
    int TimeDateStamp { get; }
    Guid PdbGuid { get; }
    int PdbAge { get; }
    string PdbName { get; }
}
```

### Core Entry Points

```csharp
namespace ProfileExplorer.Profiling;

public class FunctionProfiler : IDisposable
{
    public FunctionProfiler(ProfilerOptions options);

    /// Register loaded images (modules) with their PDB identity for symbol resolution.
    void AddImages(IEnumerable<IProfileImage> images);

    /// Add CPU samples. Can be called multiple times (e.g., per-processor batches).
    void AddSamples(IEnumerable<IProfileSample> samples);

    /// Add hardware performance counter events (PMU/PMC).
    void AddPerformanceCounterEvents(IEnumerable<IPerformanceCounterEvent> events);

    /// Register managed/.NET method mappings (from CLR JIT events).
    void AddManagedMethods(IEnumerable<IManagedMethodMapping> methods);

    /// Load symbols for all registered images. Downloads PDBs from symbol server.
    Task LoadSymbolsAsync(CancellationToken ct = default);

    /// Build aggregated per-function profiles from added samples.
    IReadOnlyList<FunctionProfile> GetFunctionProfiles(
        string? processName = null,
        int? processId = null);

    /// Build a call tree from added samples (requires StackFrames on IProfileSample).
    CallTreeNode GetCallTree(
        string? processName = null,
        int? processId = null);

    /// Get annotated disassembly for a specific function.
    /// Downloads binary on-demand, disassembles via Capstone, annotates with timing.
    Task<AnnotatedAssembly> GetAnnotatedAssemblyAsync(
        FunctionProfile function,
        CancellationToken ct = default);
}

public class ProfilerOptions
{
    public IReadOnlyList<string> SymbolPaths { get; set; }   // e.g., ["srv*C:\\Symbols*https://symbolserver.example.com"]
    public int SymbolTimeoutSeconds { get; set; } = 30;
    public int BellwetherTimeoutSeconds { get; set; } = 5;
    public bool EnableCompanyFilter { get; set; } = true;    // Only load Microsoft symbols
    public string? CompanyName { get; set; } = "Microsoft";
    public bool EnableNegativeCache { get; set; } = true;    // Skip previously-failed downloads
    public double MinSelfPercent { get; set; } = 0.0;        // Filter threshold for GetFunctionProfiles
    public ProcessorArchitecture Architecture { get; set; } = ProcessorArchitecture.Amd64;
    public bool IncludePerformanceCounters { get; set; } = false;  // Process PMU/PMC data
    public bool IncludeManagedCode { get; set; } = true;     // Resolve .NET JIT methods
}
```

### Output Models

```csharp
namespace ProfileExplorer.Profiling;

public class FunctionProfile
{
    public string ModuleName { get; }
    public string FunctionName { get; }
    public long FunctionRva { get; }
    public int FunctionSize { get; }
    public TimeSpan InclusiveWeight { get; }      // Total time (self + callees)
    public TimeSpan ExclusiveWeight { get; }      // Self time only
    public double InclusivePercent { get; }
    public double ExclusivePercent { get; }
    public string? SourceFile { get; }
    public int? SourceLine { get; }
    public bool HasAssembly { get; }              // Binary available for disassembly?

    /// Per-instruction-offset weights (RVA offset from function start → accumulated weight).
    public IReadOnlyDictionary<long, TimeSpan> InstructionWeights { get; }

    /// Per-instruction PMU counter values (only populated when IncludePerformanceCounters = true).
    public IReadOnlyDictionary<long, InstructionCounterValues>? InstructionCounters { get; }

    /// Whether this is a managed (.NET) function.
    public bool IsManaged { get; }
}

public class AnnotatedAssembly
{
    public string FullText { get; }               // Complete annotated disassembly text
    public IReadOnlyList<AssemblyLine> Lines { get; }
    public IReadOnlyList<HotLine> HotLines { get; }  // Lines above MinPercent threshold
}

public class AssemblyLine
{
    public long Address { get; }
    public long Rva { get; }
    public string InstructionText { get; }        // e.g., "call CIconCache::GetIcon"
    public TimeSpan Weight { get; }
    public double Percent { get; }
    public string? SourceFile { get; }
    public int? SourceLine { get; }
}

public class HotLine
{
    public long InstructionOffset { get; }
    public double Percent { get; }
    public TimeSpan Time { get; }
    public string InstructionText { get; }
    public string? SourceFile { get; }
    public int? SourceLine { get; }
}
```

### Call Tree Models

```csharp
namespace ProfileExplorer.Profiling;

public class CallTreeNode
{
    public string ModuleName { get; }
    public string FunctionName { get; }
    public TimeSpan InclusiveWeight { get; }       // Self + all descendants
    public TimeSpan ExclusiveWeight { get; }       // Self only (leaf samples)
    public double InclusivePercent { get; }
    public double ExclusivePercent { get; }
    public IReadOnlyList<CallTreeNode> Children { get; }
    public IReadOnlyList<CallTreeNode> Callers { get; }  // For bottom-up view
    public IReadOnlyDictionary<int, ThreadWeight> ThreadWeights { get; }  // Per-thread breakdown
    public CallTreeNodeKind Kind { get; }          // NativeUser, NativeKernel, Managed

    /// Call sites within this function that call into children.
    public IReadOnlyList<CallSite> CallSites { get; }
}

public class CallSite
{
    public long Rva { get; }                       // Call instruction RVA
    public TimeSpan Weight { get; }
    public IReadOnlyList<(CallTreeNode Target, TimeSpan Weight)> Targets { get; }  // Polymorphic targets
}

public record ThreadWeight(TimeSpan Inclusive, TimeSpan Exclusive);

public enum CallTreeNodeKind { NativeUser, NativeKernel, Managed }
```

### Performance Counter Models

```csharp
namespace ProfileExplorer.Profiling;

/// Input: a hardware performance counter sample event.
public interface IPerformanceCounterEvent
{
    long InstructionPointer { get; }
    TimeSpan Timestamp { get; }
    int ProcessId { get; }
    int ThreadId { get; }
    short CounterId { get; }                       // Which counter (e.g., InstructionsRetired)
}

/// Describes a registered performance counter source.
public class PerformanceCounterInfo
{
    public int Id { get; }
    public string Name { get; }                    // e.g., "InstructionsRetired", "BranchMispredictions"
    public long Frequency { get; }                 // Sampling interval
}

/// Computed metric derived from two base counters (e.g., cache miss rate).
public class PerformanceMetric
{
    public string Name { get; }
    public string BaseCounterName { get; }
    public string RelativeCounterName { get; }
    public bool IsPercentage { get; }
    public double ComputeMetric(long baseValue, long relativeValue);
}

/// Per-instruction counter values (attached to FunctionProfile.InstructionCounters).
public class InstructionCounterValues
{
    public IReadOnlyDictionary<int, long> CounterValues { get; }  // CounterId → sample count
}
```

### Managed Code Models

```csharp
namespace ProfileExplorer.Profiling;

/// Input: a JIT-compiled managed method mapping from CLR events.
public interface IManagedMethodMapping
{
    int ProcessId { get; }
    string MethodName { get; }                     // Fully qualified (Namespace.Class.Method)
    long NativeStartAddress { get; }               // JIT code start address
    int NativeSize { get; }                        // JIT code size in bytes
    int MethodToken { get; }                       // Metadata token
    string? ModuleName { get; }                    // Managed assembly name
    Guid ManagedPdbGuid { get; }                   // Portable PDB GUID
    int ManagedPdbAge { get; }
    string? ManagedPdbName { get; }
    IReadOnlyList<ILToNativeMapping>? ILMappings { get; }  // IL offset → native offset
}

public record ILToNativeMapping(int ILOffset, int NativeStartOffset, int NativeEndOffset);
```

## Consumer Usage — BigRedPerfAI with DataLayer

```csharp
// BigRedPerfAI pipeline step — replaces MCP-based ProfileExplorerService

// 1. DataLayer loads the trace and extracts raw CPU samples
using var trace = TraceProcessor.Create(etlPath);
var cpuData = trace.UseCpuSamplingData();
var imageData = trace.UseImageSections();     // For module load info
trace.Process();

// 2. Adapt DataLayer models to PE.Profiling input (thin adapter classes)
var images = imageData.Result.Images
    .Select(img => new DataLayerImageAdapter(img));

var samples = cpuData.Result.Samples
    .Where(s => s.Process?.ImageName == processName)
    .Select(s => new DataLayerSampleAdapter(s));

// 3. PE.Profiling does function-level profiling + disassembly
using var profiler = new FunctionProfiler(new ProfilerOptions
{
    SymbolPaths = ["srv*C:\\Symbols*https://symbolserver.example.com"],
    MinSelfPercent = 1.0
});

profiler.AddImages(images);
profiler.AddSamples(samples);
await profiler.LoadSymbolsAsync();

var functions = profiler.GetFunctionProfiles(processName: "explorer.exe");

foreach (var fn in functions.OrderByDescending(f => f.ExclusiveWeight).Take(10))
{
    var asm = await profiler.GetAnnotatedAssemblyAsync(fn);
    // asm.HotLines → same data BigRedPerfAI currently gets from PE via MCP
}
```

### Adapter Classes (~20 lines each, in BigRedPerfAI)

```csharp
class DataLayerSampleAdapter : IProfileSample
{
    private readonly ICpuSample _s;
    public DataLayerSampleAdapter(ICpuSample s) => _s = s;
    public long InstructionPointer => (long)_s.InstructionPointer.Value;
    public TimeSpan Weight => _s.Weight;
    public int ProcessId => _s.Process?.Id ?? 0;
    public int ThreadId => _s.Thread?.Id ?? 0;
    public string? ImageName => _s.Image?.FileName;
    public long ImageBaseAddress => (long)(_s.Image?.BaseAddress.Value ?? 0);
    public IReadOnlyList<long>? StackFrames => null; // Optional, for call tree
}

class DataLayerImageAdapter : IProfileImage
{
    private readonly IImage _img;
    public DataLayerImageAdapter(IImage img) => _img = img;
    public string ImageName => _img.FileName;
    public long BaseAddress => (long)_img.BaseAddress.Value;
    public int Size => (int)_img.Size.Bytes;
    public int TimeDateStamp => (int)_img.TimeDateStamp;
    // PDB identity from image debug directory — may need trace.UseImageSections() or similar
    public Guid PdbGuid => /* from debug directory */ Guid.Empty;
    public int PdbAge => 0;
    public string PdbName => _img.FileName;
}
```

## Files to Extract from ProfileExplorerCore

### PDB Symbol Resolution

| Source File | Extract As | Notes |
|---|---|---|
| `Binary/PDBDebugInfoProvider.cs` | `Symbols/PdbSymbolProvider.cs` | Core PDB reader. Remove `AnnotateSourceLocations` (PE IR tags). Keep DIA-based RVA→function, RVA→source line, inlinee enumeration. |
| `Binary/IDebugInfoProvider.cs` | `Symbols/IDebugInfoProvider.cs` | Interface + `SymbolFileDescriptor` (GUID+Age). Direct extraction. |
| `Binary/FunctionDebugInfo.cs` | `Symbols/FunctionDebugInfo.cs` | Function name, RVA, size, source lines, binary search by RVA. Direct extraction. |
| `Binary/SourceLineDebugInfo.cs` | `Symbols/SourceLineDebugInfo.cs` | Instruction offset → source line mapping. Direct extraction. |
| `Binary/SourceFileDebugInfo.cs` | `Symbols/SourceFileDebugInfo.cs` | Source file path + metadata. Direct extraction. |
| `Binary/SymbolFileCache.cs` | `Symbols/SymbolFileCache.cs` | Protobuf-serialized function-list cache per PDB. Avoids re-enumerating. |
| `Binary/DotNetDebugInfoProvider.cs` | `Symbols/DotNetDebugInfoProvider.cs` | Optional: managed/portable PDB support. |
| `Settings/SymbolFileSourceSettings.cs` | `Configuration/SymbolSettings.cs` | Refactor: remove `SettingsBase` inheritance, make POCO. Keep timeouts, bellwether, negative cache, company filter. |

### Binary Download

| Source File | Extract As | Notes |
|---|---|---|
| `Binary/PEBinaryInfoProvider.cs` | `Binary/BinaryProvider.cs` | PE reader + symbol server binary download. Decouple from `PDBDebugInfoProvider` auth — share auth handler via options. |
| `Binary/IBinaryInfoProvider.cs` | `Binary/IBinaryInfoProvider.cs` | Interface + `BinaryFileDescriptor`. Direct extraction. |

### Disassembly

| Source File | Extract As | Notes |
|---|---|---|
| `Binary/Disassembler.cs` | `Disassembly/Disassembler.cs` | Capstone P/Invoke wrapper. Keep x86/x64/ARM64 support. Decouple from PE's IR element model — return instruction list instead of annotating IR elements. |

### Profiling / Aggregation

| Source File | Extract As | Notes |
|---|---|---|
| `Profile/Data/FunctionProfileData.cs` | `Profiling/FunctionProfileBuilder.cs` | Extract `InstructionWeight` aggregation logic and `TryFindElementForOffset` (IP skid correction). |
| `Compilers/ASM/ASMCompilerIRInfo.cs` | `Profiling/InstructionOffsetConfig.cs` | IP skid constants: ARM64 = fixed 4 bytes, x86/x64 = variable 1-16 bytes. |
| `Profile/ETW/ETWProfileDataProvider.cs` | `Profiling/SampleAggregator.cs` | Extract the sample→function mapping logic from `ProcessSamplesChunk`. Remove PE document model coupling. |

### Call Tree

| Source File | Extract As | Notes |
|---|---|---|
| `Profile/CallTree/ProfileCallTree.cs` | `CallTree/CallTreeBuilder.cs` | Core call tree. Self-contained, parallel merge support. |
| `Profile/CallTree/ProfileCallTreeNode.cs` | `CallTree/CallTreeNode.cs` | Node with inclusive/exclusive weight, per-thread weights, children/callers. Includes `CallTreeGroupNode` for multi-path function aggregation. |
| `Profile/CallTree/ProfileCallSite.cs` | `CallTree/CallSite.cs` | Per-RVA call site with polymorphic target tracking. |
| `Profile/Processing/CallTreeProcessor.cs` | `CallTree/CallTreeProcessor.cs` | Parallel processor that walks resolved stacks bottom-up. Already a separate class. |

### Performance Counters (PMU/PMC)

| Source File | Extract As | Notes |
|---|---|---|
| `Profile/Data/PerformanceCounters.cs` | `Counters/PerformanceCounters.cs` | Self-contained: `PerformanceCounter`, `PerformanceMetric`, `PerformanceCounterValueSet`, `PerformanceCounterConfig`. Direct extraction. |
| `Profile/Data/RawProfileModel.cs` (struct `PerformanceCounterEvent`) | `Counters/PerformanceCounterEvent.cs` | Packed struct: IP, Timestamp, ContextId, CounterId. Direct extraction. |
| `Profile/ETW/ETWProfileDataProvider.cs` (`ProcessPerformanceCounters`) | `Counters/CounterAggregator.cs` | Extract counter→function/instruction attribution logic from `ProcessPerformanceCounters()`. Shares IP→module resolution with sample aggregation — factor into shared helper. |

### Managed / .NET Code Profiling

| Source File | Extract As | Notes |
|---|---|---|
| `Binary/DotNetDebugInfoProvider.cs` | `Managed/DotNetDebugInfoProvider.cs` | Managed PDB reader. Implements `IDebugInfoProvider`. Includes `MethodCode` (JIT bytes), IL-to-native mapping, portable PDB source resolution. Direct extraction. |
| `Profile/Data/ManagedRawProfileData.cs` | `Managed/ManagedMethodResolver.cs` | Per-process managed method registry. Binary search by IP. Contains `ManagedMethodMapping`. |
| `Profile/ETW/ETWEventProcessorManaged.cs` | N/A (consumer responsibility) | CLR event subscription (MethodLoadVerbose, ILToNativeMap). Consumers parse these events and feed `IManagedMethodMapping` to PE.Profiling. Not extracted — this is source-specific. |

### Supporting Utilities

| Source File | Extract As | Notes |
|---|---|---|
| `Providers/IBinaryFileFinder.cs` | `Binary/IBinaryFileFinder.cs` | Interface. Direct extraction. |
| `Providers/IDebugFileFinder.cs` | `Symbols/IDebugFileFinder.cs` | Interface. Direct extraction. |
| `Compilers/ASM/ASMBinaryFileFinder.cs` | `Binary/DefaultBinaryFileFinder.cs` | Thin wrapper. |
| `Compilers/ASM/ASMDebugFileFinder.cs` | `Symbols/DefaultDebugFileFinder.cs` | Thin wrapper. |

### Symbol Server Resolution (Vendored)

Instead of taking a transitive dependency on `Microsoft.Diagnostics.Tracing.TraceEvent` (which is large and pulls in ETL parsing, CLR parsers, etc.), PE.Profiling vendors just the symbol path resolution logic:

| Source | Extract As | Notes |
|---|---|---|
| TraceEvent `SymbolReader.FindSymbolFilePath` | `Symbols/SymbolServerClient.cs` | HTTP-based PDB/binary download from symbol server. Constructs standard URL format: `https://server/{name}/{GUID}{Age}/{name}`. Reimplement in ~200 lines with `HttpClient`. |
| TraceEvent `SymbolReader.FindExecutableFilePath` | `Binary/SymbolServerClient.cs` (shared) | Binary download: `https://server/{name}/{TimeDateStamp}{ImageSize}/{name}`. Same pattern as PDB. |
| PE's `SymwebHandler` auth | `Symbols/SymwebAuthHandler.cs` | Azure Identity-based auth for internal symbol server. Already in PE's codebase. Direct extraction. |

## Native Dependencies

The extracted library carries these native deps (must be bundled in the NuGet package):

| Dependency | Purpose | How Bundled |
|---|---|---|
| `capstone.dll` | Disassembly (x86/x64/ARM64) | NuGet `runtimes/` folder (win-x64, win-arm64) |
| `msdia140.dll` (DIA SDK) | PDB reading via COM interop | NuGet `runtimes/` folder + COM registration or side-by-side |
| `dbghelp.dll` | C++ name demangling (`UnDecorateSymbolName`) | System-provided (ships with Windows) |

### NuGet Package Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Azure.Identity` | ~1.11.x | Auth for internal symbol server |
| `protobuf-net` | ~3.2.x | Symbol cache serialization |

> **No TraceEvent dependency.** Symbol server resolution (PDB/binary download via HTTP) is vendored into `SymbolServerClient.cs` (~200 lines) to avoid pulling in TraceEvent's large transitive dependency graph (ETL parsing, CLR event parsers, etc.). The symbol server protocol is simple HTTP GET with a well-known URL format.

## Project Structure

```
profile-explorer/
└── src/
    └── ProfileExplorer.Profiling/
        ├── ProfileExplorer.Profiling.csproj
        ├── FunctionProfiler.cs              — Main entry point / orchestrator
        ├── ProfilerOptions.cs               — Configuration POCO
        ├── Abstractions/
        │   ├── IProfileSample.cs            — Input: CPU sample
        │   ├── IProfileImage.cs             — Input: loaded module
        │   ├── IPerformanceCounterEvent.cs   — Input: PMU/PMC sample
        │   └── IManagedMethodMapping.cs      — Input: .NET JIT method
        ├── Models/
        │   ├── FunctionProfile.cs           — Output: per-function weights
        │   ├── AnnotatedAssembly.cs         — Output: disassembly + timing
        │   ├── AssemblyLine.cs              — Output: single instruction
        │   └── HotLine.cs                   — Output: hot instruction
        ├── Profiling/
        │   ├── SampleAggregator.cs          — Samples → per-function/instruction weights
        │   ├── FunctionProfileBuilder.cs    — Instruction weight accumulation
        │   ├── InstructionOffsetConfig.cs   — IP skid correction constants
        │   └── IpResolver.cs               — Shared IP → module/function resolution
        ├── CallTree/
        │   ├── CallTreeBuilder.cs           — Build call tree from resolved stacks
        │   ├── CallTreeNode.cs              — Node: inclusive/exclusive, per-thread, kind
        │   ├── CallSite.cs                  — Per-RVA call site with polymorphic targets
        │   └── CallTreeProcessor.cs         — Parallel chunk-based tree construction + merge
        ├── Counters/
        │   ├── CounterAggregator.cs         — PMC events → per-function/instruction counters
        │   ├── PerformanceCounters.cs       — Counter, Metric, ValueSet, Config types
        │   └── PerformanceCounterEvent.cs   — Packed struct (IP, Timestamp, CounterId)
        ├── Managed/
        │   ├── DotNetDebugInfoProvider.cs   — Managed PDB reader (portable PDB + IL mapping)
        │   ├── ManagedMethodResolver.cs     — Binary search IP → managed method
        │   └── MethodCode.cs               — JIT code bytes + call targets
        ├── Symbols/
        │   ├── PdbSymbolProvider.cs          — DIA-based PDB reader
        │   ├── IDebugInfoProvider.cs         — Interface + SymbolFileDescriptor
        │   ├── FunctionDebugInfo.cs          — Function metadata
        │   ├── SourceLineDebugInfo.cs        — Instruction → source line
        │   ├── SourceFileDebugInfo.cs        — Source file metadata
        │   ├── SymbolFileCache.cs            — PDB function cache
        │   ├── SymbolServerClient.cs         — Vendored: HTTP PDB/binary download
        │   ├── SymwebAuthHandler.cs          — Azure Identity auth for symweb
        │   ├── IDebugFileFinder.cs           — PDB locator interface
        │   └── DefaultDebugFileFinder.cs     — PDB locator implementation
        ├── Binary/
        │   ├── BinaryProvider.cs             — PE reader + binary download
        │   ├── IBinaryInfoProvider.cs        — Interface + BinaryFileDescriptor
        │   ├── IBinaryFileFinder.cs          — Binary locator interface
        │   └── DefaultBinaryFileFinder.cs    — Binary locator implementation
        ├── Disassembly/
        │   ├── Disassembler.cs               — Capstone wrapper (x86/x64/ARM64)
        │   ├── AssemblyAnnotator.cs          — Combine disassembly + weights + counters
        │   └── DisassemblerSectionLoader.cs  — Native + managed code section loading
        └── runtimes/
            ├── win-x64/native/
            │   ├── capstone.dll
            │   └── msdia140.dll
            └── win-arm64/native/
                ├── capstone.dll
                └── msdia140.dll
```

## Internal Refactoring in PE

After extraction, PE's own code should consume `ProfileExplorer.Profiling` internally:

1. `ProfileExplorerCore` adds a project reference to `ProfileExplorer.Profiling`
2. `ETWProfileDataProvider` delegates to `SampleAggregator` + `FunctionProfileBuilder`
3. `ProfileModuleBuilder` delegates to `BinaryProvider` + `Disassembler`
4. The headless MCP server (`ProfileExplorer.McpServer`) uses `FunctionProfiler` directly
5. Existing PE behavior is preserved — just backed by the extracted library

This ensures the extracted library stays in sync with PE's own usage.

## Migration Path for BigRedPerfAI

### Phase 1: Keep PE via MCP (no changes)
PE internally uses the extracted library. BigRedPerfAI continues using MCP. Zero risk.

### Phase 2: Add DataLayer + PE.Profiling path (parallel)
Add a new `DataLayerProfileStep` alongside the existing `ProfileExplorerStep`. Feature-flag to choose. Validate output parity.

### Phase 3: Deprecate MCP path
Once validated, remove the MCP-based `ProfileExplorerService` and PE process management. BigRedPerfAI only needs `DataLayer` + `ProfileExplorer.Profiling` NuGet packages.

### What BigRedPerfAI Gains

| Before (MCP) | After (DataLayer + PE.Profiling) |
|---|---|
| Must install PE on machine | Just NuGet packages |
| One trace at a time (process singleton) | Unlimited parallel traces |
| Process spawn + MCP handshake overhead | In-process, zero IPC |
| Fragile process lifecycle (crash, timeout, zombie) | Library calls, normal error handling |
| 600s timeout for trace load | Same perf, no timeout worries |
| PE version compatibility concerns | NuGet version pinning |

## Testing Plan

### Test Framework & Conventions

- **MSTest** (matching PE's existing test projects)
- **No mocking frameworks** — PE uses pure in-memory objects or real files; follow the same pattern
- **Golden-file / baseline testing** — PE's `EndToEndWorkflowTests` compare output against CSV baselines; reuse this pattern for assembly output validation
- **Test data** — PE already has a checked-in `MsoTrace` test case with real ETL, PDB, and DLL files; PE.Profiling tests reuse this data

### Test Project Structure

```
profile-explorer/
└── src/
    └── ProfileExplorer.Profiling.Tests/
        ├── ProfileExplorer.Profiling.Tests.csproj
        ├── Unit/
        │   ├── SampleAggregatorTests.cs
        │   ├── FunctionProfileBuilderTests.cs
        │   ├── IpSkidCorrectionTests.cs
        │   ├── AssemblyAnnotatorTests.cs
        │   ├── ProfilerOptionsValidationTests.cs
        │   ├── CallTreeBuilderTests.cs
        │   ├── CounterAggregatorTests.cs
        │   ├── ManagedMethodResolverTests.cs
        │   └── SymbolServerClientTests.cs
        ├── Integration/
        │   ├── PdbSymbolProviderTests.cs
        │   ├── BinaryProviderTests.cs
        │   ├── DisassemblerTests.cs
        │   ├── FunctionProfilerEndToEndTests.cs
        │   └── BaselineTests.cs
        ├── TestData/
        │   └── (symlinks or copies from ProfileExplorerCoreTests/TestData)
        ├── Baselines/
        │   ├── MsoTrace/
        │   │   ├── function_profiles.csv
        │   │   ├── instruction_weights_<function>.csv
        │   │   └── assembly_<function>.txt
        │   └── README.md
        └── Helpers/
            ├── SyntheticSampleBuilder.cs
            ├── TestDataHelper.cs
            └── BaselineHelper.cs
```

### Unit Tests (No I/O, No Network, Fast)

These test pure computation logic with synthetic in-memory data. Should run in < 5 seconds total.

#### `SampleAggregatorTests` — Sample → Function/Instruction Aggregation

| Test | What it validates |
|------|-------------------|
| `SingleSample_CreatesOneFunctionProfile` | One sample → one function with correct exclusive weight |
| `MultipleSamples_SameFunction_AggregatesWeight` | 100 samples to same IP → weight = sum of individual weights |
| `MultipleSamples_DifferentFunctions_SeparateProfiles` | Samples across 3 functions → 3 distinct `FunctionProfile` objects |
| `InstructionWeights_AggregatesPerOffset` | 50 samples across 5 instruction offsets → correct per-offset weights |
| `FilterByProcessId_OnlyIncludesMatchingProcess` | Mixed process samples → filtered correctly |
| `FilterByProcessName_OnlyIncludesMatchingProcess` | Same, by name |
| `EmptySamples_ReturnsEmptyProfiles` | Edge case: no samples → empty list, no crash |
| `SamplesWithNoImage_SkippedGracefully` | Samples with null module → skipped, not thrown |
| `InclusiveWeight_AccountsForCallStack` | Samples with stack frames → callers get inclusive weight, leaf gets exclusive |
| `PercentCalculation_RelativeToTotalWeight` | Verify ExclusivePercent/InclusivePercent sum correctly |

#### `FunctionProfileBuilderTests` — Per-Function Weight Accumulation

| Test | What it validates |
|------|-------------------|
| `AddWeight_AccumulatesCorrectly` | Repeated `AddInstructionWeight` calls → sums |
| `InstructionWeightMap_DistinctOffsets` | 10 different offsets → 10 entries in map |
| `InstructionWeightMap_SameOffset_Merges` | Same offset added 5 times → single merged entry |
| `ExclusiveWeight_EqualsInstructionWeightSum` | Self-weight = sum of all instruction weights |

#### `IpSkidCorrectionTests` — IP Adjustment Logic

| Test | What it validates |
|------|-------------------|
| `x64_SkidsBackByOne_FindsInstruction` | IP one byte past instruction → corrected to instruction start |
| `x64_SkidsBackVariable_FindsNearestInstruction` | IP 1-16 bytes past → finds nearest preceding instruction |
| `x64_BeyondMaxAdjust_ReturnsOriginal` | IP too far from any instruction → returns uncorrected |
| `ARM64_FixedFourByteCorrection` | ARM64 always adjusts by exactly 4 bytes |
| `ExactMatch_NoCorrection` | IP matches an instruction exactly → no adjustment |
| `EmptyInstructionMap_ReturnsOriginal` | No known instructions → returns uncorrected |

#### `AssemblyAnnotatorTests` — Disassembly + Timing Formatting

| Test | What it validates |
|------|-------------------|
| `AnnotatesHotInstructions_WithTimePercent` | Instruction with 25% weight → `[Time(%): 25.00%, Time: XX.XX ms]` |
| `ColdInstructions_NoAnnotation` | 0% weight instructions → no timing suffix |
| `HotLineExtraction_AboveThreshold` | MinPercent=1.0 → only lines ≥1% in HotLines list |
| `HotLineExtraction_OrderedByPercent` | HotLines sorted descending by percent |
| `SourceLineAttribution_IncludedWhenAvailable` | Instructions with source mapping → SourceFile/SourceLine populated |
| `FullText_MatchesExpectedFormat` | Complete annotated assembly text matches golden string |
| `SmartSnippet_ContextWindow` | ±20 lines around hot instruction, capped at 150 lines total |

#### `ProfilerOptionsValidationTests` — Configuration Edge Cases

| Test | What it validates |
|------|-------------------|
| `DefaultOptions_AreValid` | Default `ProfilerOptions` passes validation |
| `EmptySymbolPaths_Throws` | No symbol paths → clear error |
| `NegativeTimeout_Throws` | Negative timeout → `ArgumentOutOfRangeException` |
| `MinSelfPercent_ClampedToZeroToHundred` | Values outside [0,100] → clamped or rejected |

#### `CallTreeBuilderTests` — Call Tree Construction

| Test | What it validates |
|------|-------------------|
| `SingleStack_CreatesLinearTree` | One sample with 3 frames → root→mid→leaf chain |
| `SharedPrefix_MergesNodes` | Two stacks sharing root → single root with two children |
| `InclusiveWeight_PropagatesToRoot` | Leaf weight propagates up the tree |
| `ExclusiveWeight_OnlyOnLeaf` | Only leaf nodes have exclusive weight |
| `PerThreadWeights_Tracked` | Samples from 2 threads → per-thread weight breakdown |
| `ParallelMerge_ProducesCorrectTree` | Merging partial trees → same result as sequential |
| `RecursiveFunction_HandledCorrectly` | A→B→A→B cycle → nodes created, no infinite loop |
| `EmptyStacks_Skipped` | Samples with null/empty StackFrames → skipped gracefully |
| `CallSites_TrackPolymorphicTargets` | Same call-site RVA calling 2 different targets → both tracked |

#### `CounterAggregatorTests` — PMU Counter Attribution

| Test | What it validates |
|------|-------------------|
| `SingleCounter_AttributedToFunction` | One PMC event → function gets counter value |
| `MultipleCounters_SameInstruction` | 3 counter types at same IP → all tracked in `InstructionCounterValues` |
| `DerivedMetric_ComputesCorrectly` | Cache-miss-rate = misses/references computed by `PerformanceMetric` |
| `CountersDisabled_NoProcessing` | `IncludePerformanceCounters=false` → no counter data, no errors |
| `UnresolvedIP_Skipped` | Counter event with unknown module → skipped gracefully |

#### `ManagedMethodResolverTests` — .NET Method Resolution

| Test | What it validates |
|------|-------------------|
| `RegisterMethod_FindByIP` | Register method at address range → IP in range resolves to method |
| `IPOutsideRange_ReturnsNull` | IP outside all registered methods → null |
| `OverlappingMethods_PicksSmallest` | Two methods where one contains the other → most specific wins |
| `ManagedPdb_ResolveSourceLine` | Managed PDB with IL mapping → correct source line for native offset |
| `EmptyILMappings_GracefulDegradation` | Method without IL mappings → function found, no source lines |

#### `SymbolServerClientTests` — Vendored HTTP Symbol Resolution

| Test | What it validates |
|------|-------------------|
| `BuildPdbUrl_CorrectFormat` | GUID+Age → `/{name}/{GUID}{Age}/{name}` URL |
| `BuildBinaryUrl_CorrectFormat` | Timestamp+Size → `/{name}/{TS}{Size}/{name}` URL |
| `ParseSymbolPath_MultipleServers` | `srv*cache*server1;srv*cache*server2` → two servers parsed |
| `ParseSymbolPath_LocalAndRemote` | `C:\local;srv*C:\cache*https://remote` → local path + remote URL |

### Integration Tests (Real Files, Potentially Network)

These use real PDB, DLL, and ETL files. Organized into tiers by cost.

#### Tier 1: Local Files Only (No Network, ~30 seconds)

Uses the existing `MsoTrace` test data (ETL + PDB + DLL already checked in).

##### `PdbSymbolProviderTests` — PDB Loading & Resolution

| Test | What it validates |
|------|-------------------|
| `LoadPdb_EnumeratesFunctions` | Opens `Mso20Win32Client.pdb` → returns non-empty function list |
| `FindFunctionByRva_ReturnsCorrectName` | Known RVA → expected function name |
| `FindFunctionByRva_UnknownRva_ReturnsNull` | Non-existent RVA → null, not crash |
| `FindSourceLineByRva_ReturnsFileAndLine` | Known RVA → source file path + line number |
| `FindSourceLineByRva_InlinedFunction_ReturnsInlineeInfo` | RVA inside inlined function → inlinee name + source |
| `EnumerateSourceLines_PerInstruction` | Function with N instructions → N source line mappings (DIA `findLinesByRVA`) |
| `SymbolFileCache_RoundTrips` | Load PDB → save cache → load cache → same function list |
| `SymbolFileCache_SkipsReload` | Cached PDB → second call doesn't re-enumerate |

##### `DisassemblerTests` — Capstone Disassembly

| Test | What it validates |
|------|-------------------|
| `Disassemble_x64Function_ReturnsInstructions` | Real DLL function → non-empty instruction list |
| `Disassemble_ResolvesCallTargets` | `call` instructions → target function name resolved |
| `Disassemble_FunctionBoundaries` | Disassembly stays within function RVA+Size |
| `Disassemble_HandlesShortFunctions` | 1-2 instruction function → works correctly |
| `Disassemble_InvalidBinary_GracefulError` | Corrupted/missing DLL → clear error, not crash |

##### `FunctionProfilerEndToEndTests` — Full Pipeline, Local Data

| Test | What it validates |
|------|-------------------|
| `MsoTrace_MatchesPEBaseline` | Load MsoTrace ETL → PE.Profiling function profiles match PE's own `EndToEndWorkflowTests` baselines (function names, weights, ordering) |
| `MsoTrace_TopFunction_AssemblyMatchesBaseline` | Top function assembly output matches golden file |
| `MsoTrace_HotLines_MatchPEOutput` | HotLine records (offset, percent, text) match PE's output for same trace |
| `MsoTrace_ProcessFilter_CorrectResults` | Filter by process name → only matching functions |
| `MsoTrace_MultipleProcesses_Independent` | Two process filters → independent, correct results |

##### `BaselineTests` — Golden-File Regression Suite

| Test | What it validates |
|------|-------------------|
| `FunctionProfiles_MatchBaseline` | CSV comparison: module, function, exclusive%, inclusive% |
| `InstructionWeights_MatchBaseline` | Per-function CSV: offset, weight_ms, percent |
| `AssemblyOutput_MatchBaseline` | Full annotated assembly text comparison (with tolerance for timing values) |
| `UpdateBaselines` | Helper (not auto-run): regenerates baseline files when intentional changes occur |

**Baseline management:**
- Baselines checked in under `Baselines/MsoTrace/`
- `BaselineHelper.cs` provides `AssertMatchesBaseline(actual, baselinePath, tolerance)` with configurable numeric tolerance (e.g., ±0.1% for timing values that vary with trace replay)
- New test cases (future traces) just add a directory under `Baselines/`

#### Tier 2: Network Required (~2-5 minutes)

These download PDBs/binaries from symbol servers. Run in CI but can be skipped locally via `[TestCategory("Network")]`.

##### `BinaryProviderTests` — Symbol Server Downloads

| Test | What it validates |
|------|-------------------|
| `DownloadBinary_KnownModule_Succeeds` | ntdll.dll with known timestamp/size → downloaded |
| `DownloadBinary_UnknownModule_ReturnsNull` | Fake timestamp/size → null, not crash |
| `DownloadPdb_KnownGuidAge_Succeeds` | ntoskrnl PDB with known GUID+Age → downloaded |
| `BellwetherTest_DetectsServerHealth` | Bellwether check → returns healthy/degraded status |
| `NegativeCache_SkipsPreviouslyFailed` | Failed download → second attempt skipped within TTL |
| `CompanyFilter_SkipsNonMicrosoft` | Non-Microsoft module → skipped when filter enabled |
| `Timeout_RespectsSettings` | Slow server simulation → times out per config |

##### `SymbolAuth_Tests` — Authentication

| Test | What it validates |
|------|-------------------|
| `Symweb_AuthenticatesWithAzureIdentity` | Can download from internal symbol server with managed identity or interactive |
| `PublicServer_NoAuthRequired` | msdl.microsoft.com → works without auth |
| `AuthFailure_FallsBackToPublic` | Symweb auth fails → tries public server |

#### Tier 3: Parity Validation (~10+ minutes)

These validate that PE.Profiling produces identical output to PE's headless MCP server. Run nightly or on-demand, not on every PR.

##### `ParityTests` — PE.Profiling vs. PE MCP Server

| Test | What it validates |
|------|-------------------|
| `FunctionProfiles_MatchMcpOutput` | Same ETL → PE.Profiling profiles match PE MCP `get_available_functions` |
| `Assembly_MatchesMcpOutput` | Same function → PE.Profiling assembly matches PE MCP `get_function_assembly` |
| `HotLines_MatchMcpOutput` | Same function → HotLine records match (percent, offset, text) |
| `ProcessList_MatchesMcpOutput` | Same ETL → process list + weights match PE MCP `get_available_processes` |

These tests run both PE.Profiling and PE's MCP server against the same trace and diff the results. They serve as the primary confidence gate for the extraction — if these pass, the library faithfully reproduces PE's behavior.

### Test Data Strategy

| Data | Source | Checked In? | Notes |
|------|--------|-------------|-------|
| `MsoTrace` (ETL + PDB + DLL) | Existing PE test data | ✅ Yes | Reuse from `ProfileExplorerCoreTests/TestData/` |
| Additional traces | Watson CABs from BigRedPerfAI test scenarios | ❌ No (too large) | Download on-demand in Tier 2/3 tests |
| Baseline CSVs | Generated by `UpdateBaselines` helper | ✅ Yes | Regenerated when intentional changes occur |
| Synthetic samples | Built in-memory by `SyntheticSampleBuilder` | N/A | No files needed |

**`SyntheticSampleBuilder`** — helper for unit tests:

```csharp
public class SyntheticSampleBuilder
{
    // Build fake samples with controllable distribution
    public static IReadOnlyList<IProfileSample> CreateUniform(
        int count, string module, string function, long baseIp, TimeSpan weight);

    public static IReadOnlyList<IProfileSample> CreateHotspot(
        int total, long hotIp, double hotPercent, ...);

    public static IReadOnlyList<IProfileSample> CreateMultiFunction(
        params (string module, string function, int sampleCount)[] functions);

    // Build fake images
    public static IReadOnlyList<IProfileImage> CreateImages(
        params (string name, long baseAddr, int size, Guid pdbGuid, int pdbAge)[] images);
}
```

### CI Integration

| Pipeline Stage | Tests Run | Gate? |
|---|---|---|
| PR validation | Unit + Tier 1 integration | ✅ Must pass |
| Post-merge (develop) | Unit + Tier 1 + Tier 2 | ✅ Must pass |
| Nightly | Unit + Tier 1 + Tier 2 + Tier 3 parity | ⚠️ Alert on failure |

Test categories for filtering:

```csharp
[TestCategory("Unit")]          // Pure logic, no I/O
[TestCategory("Integration")]   // Real files, local only
[TestCategory("Network")]       // Downloads from symbol server
[TestCategory("Parity")]        // PE.Profiling vs PE MCP comparison
```

CI command:
```powershell
# PR gate (fast, ~30s)
dotnet test -c Release --filter "TestCategory=Unit|TestCategory=Integration"

# Full (with network, ~5min)
dotnet test -c Release --filter "TestCategory!=Parity"

# Nightly parity check
dotnet test -c Release
```

### What Existing PE Tests Cover (and the Gaps This Fills)

PE's existing test projects have significant gaps that PE.Profiling's tests directly address:

| Area | PE's Current Coverage | PE.Profiling Tests |
|------|----------------------|-------------------|
| PDB loading / RVA→function | ❌ Empty stub (`PDBDebugInfoProviderTests`) | ✅ `PdbSymbolProviderTests` (8 tests) |
| x86/x64 disassembly | ❌ No tests | ✅ `DisassemblerTests` (5 tests) |
| Binary download | ❌ No tests | ✅ `BinaryProviderTests` (7 tests) |
| Per-function aggregation | ❌ No direct tests | ✅ `SampleAggregatorTests` (10 tests) |
| Per-instruction weights | ❌ No tests | ✅ `FunctionProfileBuilderTests` (4 tests) |
| IP skid correction | ❌ No tests | ✅ `IpSkidCorrectionTests` (6 tests) |
| Assembly annotation format | ❌ No tests | ✅ `AssemblyAnnotatorTests` (7 tests) |
| End-to-end profiling pipeline | ⚠️ Partial (EndToEndWorkflowTests) | ✅ `FunctionProfilerEndToEndTests` + baselines |
| Call tree construction | ⚠️ Partial (SyntheticProfileTests in UI) | ✅ `CallTreeBuilderTests` (9 tests) |
| PMU counter attribution | ❌ No tests | ✅ `CounterAggregatorTests` (5 tests) |
| Managed/.NET method resolution | ❌ No tests | ✅ `ManagedMethodResolverTests` (5 tests) |
| Symbol server URL construction | ❌ No tests (delegated to TraceEvent) | ✅ `SymbolServerClientTests` (4 tests) |

This means the extraction itself improves PE's overall test coverage, since the extracted library will have comprehensive tests for logic that was previously untested.

## Design Decisions

| # | Question | Decision | Rationale |
|---|----------|----------|-----------|
| 1 | **NuGet feed** | Internal first (Azure DevOps PerfToolKit feed), then nuget.org | Start internal for fast iteration, publish externally once the API stabilizes. |
| 2 | **.NET target** | `net8.0-windows` (match PE's current TFM) | Keep it the same as PE for now. Can multi-target later if needed. |
| 3 | **DIA SDK redistribution** | Bundle `msdia140.dll` in the NuGet package | Package in `runtimes/win-x64/native/` and `runtimes/win-arm64/native/`. |
| 4 | **Scope** | Full feature set: CPU sampling, disassembly, call tree, PMU counters, managed/.NET profiling | Include everything PE has. See expanded API and extraction tables above. |
| 5 | **TraceEvent dependency** | Vendor symbol server resolution (~200 lines) — **no TraceEvent dependency** | The symbol server protocol is simple HTTP GET with a well-known URL format (`/{name}/{GUID}{Age}/{name}`). Vendoring avoids pulling in TraceEvent's large transitive graph (ETL parsing, CLR parsers, kernel parsers, etc.) that consumers don't need. |
