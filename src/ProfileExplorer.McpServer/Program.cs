// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Profile;
using ProfileExplorer.Core.Profile.CallTree;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Profile.ETW;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.McpServer;

public class Program
{
  public static async Task Main(string[] args)
  {
    // Force-enable diagnostic logging — the MCP server is headless, always-on logging is essential.
    Environment.SetEnvironmentVariable("PROFILE_EXPLORER_DEBUG", "1");

    // Allow caller to specify the log directory via --log-dir <path>.
    // Must be applied before the first DiagnosticLogger call so Initialize() picks it up.
    for (int i = 0; i < args.Length - 1; i++)
    {
      if (args[i].Equals("--log-dir", StringComparison.OrdinalIgnoreCase) &&
          !string.IsNullOrEmpty(args[i + 1]))
      {
        Environment.SetEnvironmentVariable("PROFILE_EXPLORER_LOG_DIR", args[i + 1]);
        break;
      }
    }

    var builder = Host.CreateDefaultBuilder()
      .ConfigureLogging(logging =>
      {
        logging.ClearProviders();
      })
      .ConfigureServices(services =>
      {
        services.AddMcpServer()
          .WithStdioServerTransport()
          .WithToolsFromAssembly();
      });

    DiagnosticLogger.LogInfo("[MCP] ProfileExplorer MCP Server starting");
    DiagnosticLogger.LogInfo($"[MCP] Log file: {DiagnosticLogger.LogFilePath}");
    await builder.Build().RunAsync();
  }
}

/// <summary>
/// Holds the loaded profile state for the current session.
/// </summary>
public static class ProfileSession
{
  public static ProfileData? LoadedProfile { get; set; }
  public static ETWProfileDataProvider? Provider { get; set; }
  public static SymbolFileSourceSettings? SymbolSettings { get; set; }
  public static string? LoadedFilePath { get; set; }
  public static List<int> LoadedProcessIds { get; set; } = new();
  public static TimeSpan TotalWeight { get; set; }
  public static ProfileDataReport? Report { get; set; }

  // Async loading state
  public static Task<ProfileData?>? PendingLoad { get; set; }
  public static string? PendingFilePath { get; set; }
  public static string? PendingProcessId { get; set; }
  public static Exception? LoadException { get; set; }

  // Concurrency guard — only one trace load at a time.
  public static readonly SemaphoreSlim LoadSemaphore = new(1, 1);

  // Lazy-built lookup: demangled function name → ProfileFunctionId.
  // Populated on first FindFunction call after a trace loads.
  public static Dictionary<string, ProfileFunctionId>? DemangledFunctionLookup { get; set; }

  /// <summary>
  /// Resets all session state and static caches for a clean trace load.
  /// </summary>
  public static void Reset()
  {
    (Provider as IDisposable)?.Dispose();
    LoadedProfile = null;
    Provider = null;
    SymbolSettings = null;
    LoadedFilePath = null;
    LoadedProcessIds = new();
    TotalWeight = TimeSpan.Zero;
    Report = null;
    PendingLoad = null;
    PendingFilePath = null;
    PendingProcessId = null;
    LoadException = null;
    DemangledFunctionLookup = null;

    // Clear static resolution caches so each trace starts fresh.
    PDBDebugInfoProvider.ClearResolvedCache();
    BinaryFileLocator.ClearResolvedCache();
  }
}

[McpServerToolType]
public static class ProfileTools
{
  private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

  [McpServerTool, Description("Get the list of available processes from a trace file with optional weight filtering")]
  public static string GetAvailableProcesses(
    string profileFilePath,
    [Description("Minimum weight percentage threshold to filter processes (e.g. 1.0 for >=1% weight)")]
    double? minWeightPercentage = null,
    [Description("Limit results to top N heaviest processes")]
    int? topCount = null)
  {
    DiagnosticLogger.LogInfo($"[MCP] GetAvailableProcesses called: profileFilePath={profileFilePath}, minWeightPercentage={minWeightPercentage}, topCount={topCount}");
    var sw = Stopwatch.StartNew();

    if (!File.Exists(profileFilePath))
      return Error("GetAvailableProcesses", $"File not found: {profileFilePath}");

    try
    {
      var options = new ProfileDataProviderOptions();
      using var cancelTask = new CancelableTask();
      using var processor = new ETWEventProcessor(profileFilePath, options);
      var summaries = processor.BuildProcessSummary(
        (ProcessListProgress progress) => { }, cancelTask);

      // Compute total weight for percentages
      var totalWeight = TimeSpan.Zero;
      foreach (var s in summaries)
        totalWeight += s.Weight;

      // Compute percentages and duration
      foreach (var s in summaries)
      {
        s.WeightPercentage = totalWeight > TimeSpan.Zero
          ? Math.Round(s.Weight / totalWeight * 100, 2)
          : 0;
      }

      // Sort by weight desc
      summaries.Sort((a, b) => -a.Weight.CompareTo(b.Weight));

      IEnumerable<ProcessSummary> filtered = summaries;
      if (minWeightPercentage.HasValue)
        filtered = filtered.Where(p => p.WeightPercentage >= minWeightPercentage.Value);
      if (topCount.HasValue)
        filtered = filtered.Take(topCount.Value);

      var result = new
      {
        Action = "GetAvailableProcesses",
        ProfileFilePath = profileFilePath,
        Status = "Success",
        TotalProcessCount = summaries.Count,
        Processes = filtered.Select(p => new
        {
          ProcessId = p.Process.ProcessId,
          Name = p.Process.Name ?? "",
          ImageFileName = p.Process.ImageFileName ?? "",
          Weight = p.Weight.ToString(),
          WeightPercentage = p.WeightPercentage
        }).ToArray(),
        Timestamp = DateTime.UtcNow
      };
      return JsonSerializer.Serialize(result, JsonOpts);
    }
    catch (Exception ex)
    {
      DiagnosticLogger.LogError($"[MCP] GetAvailableProcesses failed: {ex.Message}", ex);
      return Error("GetAvailableProcesses", ex.Message);
    }
  }

  [McpServerTool, Description("Start loading a trace file with a specific process. Returns immediately. Use GetTraceLoadStatus to poll for completion.")]
  public static string OpenTrace(
    string profileFilePath,
    [Description("Process name or ID. A name like 'diskspd' selects ALL matching processes. Comma-separated IDs (e.g. '9492,9500') select specific ones.")]
    string processNameOrId,
    [Description("Optional additional symbol search path (e.g. 'd:\\temp\\landy' for custom kernel symbols)")]
    string? symbolPath = null,
    [Description("Optional additional binary search path (e.g. 'd:\\temp' for loose binaries like storport.sys). Required for assembly-level disassembly via GetFunctionAssembly when the binary isn't on the symbol server.")]
    string? binaryPath = null,
    [Description("Enable Azure Managed Identity for symbol server authentication. Use in headless/Azure environments where interactive browser auth is unavailable.")]
    bool useManagedIdentity = false)
  {
    DiagnosticLogger.LogInfo($"[MCP] OpenTrace called: profileFilePath={profileFilePath}, processNameOrId={processNameOrId}, symbolPath={symbolPath ?? "(none)"}, binaryPath={binaryPath ?? "(none)"}, useManagedIdentity={useManagedIdentity}");

    if (!File.Exists(profileFilePath))
      return Error("OpenTrace", $"File not found: {profileFilePath}");

    // Concurrency guard — only one trace load at a time.
    if (!ProfileSession.LoadSemaphore.Wait(0))
    {
      DiagnosticLogger.LogWarning("[MCP] OpenTrace rejected — another trace is currently loading");
      return Error("OpenTrace", "A trace is already loading. Wait for it to complete or call CloseTrace first.");
    }

    // Reset all session state and static caches for a clean load.
    ProfileSession.Reset();
    ProfileSession.PendingFilePath = profileFilePath;
    ProfileSession.PendingProcessId = processNameOrId;
    var loadStopwatch = Stopwatch.StartNew();

    ProfileSession.PendingLoad = Task.Run(async () =>
    {
      try
      {
        var options = new ProfileDataProviderOptions();
        var symbolSettings = new SymbolFileSourceSettings();
        symbolSettings.UseEnvironmentVarSymbolPaths = true;
        symbolSettings.ManagedIdentityEnabled = useManagedIdentity;
        if (!string.IsNullOrWhiteSpace(symbolPath))
          symbolSettings.InsertSymbolPath(symbolPath);
        if (!string.IsNullOrWhiteSpace(binaryPath))
        {
          options.BinarySearchPathsEnabled = true;
          options.InsertBinaryPath(binaryPath);
        }
        ProfileSession.SymbolSettings = symbolSettings;

        // Reinitialize credential chain to pick up ManagedIdentityEnabled flag.
        PDBDebugInfoProvider.ReinitializeCredentials(symbolSettings);

        DiagnosticLogger.LogInfo($"[MCP] SymbolSettings: UseEnvVar=true, CustomPath={symbolPath ?? "(none)"}, EnvVar={symbolSettings.EnvironmentVarSymbolPath ?? "(not set)"}, ManagedIdentity={useManagedIdentity}");
        DiagnosticLogger.LogInfo($"[MCP] SymbolPaths: {string.Join("; ", symbolSettings.SymbolPaths)}");
        DiagnosticLogger.LogInfo($"[MCP] BinarySearchPaths: {(options.HasBinarySearchPaths ? string.Join("; ", options.BinarySearchPaths) : "(none)")}");

        var report = new ProfileDataReport();
        var provider = new ETWProfileDataProvider();

        // Resolve process IDs — supports comma-separated IDs or name-based matching (all matches)
        var processIds = new List<int>();
        var parts = processNameOrId.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.All(p => int.TryParse(p, out _)))
        {
          // All parts are numeric — explicit PID list
          processIds = parts.Select(int.Parse).ToList();
          DiagnosticLogger.LogInfo($"[MCP] Using explicit PIDs: {string.Join(", ", processIds)}");
        }
        else
        {
          // Name-based: find ALL matching processes
          using var cancelTask2 = new CancelableTask();
          using var proc = new ETWEventProcessor(profileFilePath, options);
          var summaries = proc.BuildProcessSummary(
            (ProcessListProgress _) => { }, cancelTask2);
          var matches = summaries.Where(s =>
            (s.Process.Name?.Equals(processNameOrId, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (s.Process.ImageFileName?.Contains(processNameOrId, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

          if (matches.Count == 0)
            throw new Exception($"Process '{processNameOrId}' not found in trace");

          processIds = matches.Select(s => s.Process.ProcessId).ToList();
          DiagnosticLogger.LogInfo($"[MCP] Matched process '{processNameOrId}' to {matches.Count} PID(s): {string.Join(", ", processIds)}");
        }

        ProfileSession.LoadedProcessIds = processIds;

        using var cancelTask3 = new CancelableTask();
        var profile = await provider.LoadTraceAsync(
          profileFilePath,
          processIds,
          options,
          symbolSettings,
          report,
          progress => { Trace.WriteLine($"Load progress: {progress}"); },
          cancelTask3);

        if (profile == null)
          throw new Exception("LoadTraceAsync returned null — trace loading failed");

        // Compute total weight
        var totalWeight = TimeSpan.Zero;
        foreach (var kvp in profile.FunctionProfiles)
          totalWeight += kvp.Value.ExclusiveWeight;

        ProfileSession.TotalWeight = totalWeight;
        ProfileSession.LoadedProfile = profile;
        ProfileSession.LoadedFilePath = profileFilePath;
        ProfileSession.Provider = provider;
        ProfileSession.Report = report;

        DiagnosticLogger.LogInfo($"[MCP] OpenTrace completed in {loadStopwatch.ElapsedMilliseconds}ms — {profile.FunctionProfiles.Count} functions, {report.Modules?.Count ?? 0} modules");
        return profile;
      }
      catch (Exception ex)
      {
        ProfileSession.LoadException = ex;
        DiagnosticLogger.LogError($"[MCP] OpenTrace failed after {loadStopwatch.ElapsedMilliseconds}ms: {ex.Message}", ex);
        return null;
      }
      finally
      {
        ProfileSession.LoadSemaphore.Release();
      }
    });

    var loadingResult = new
    {
      Action = "OpenTrace",
      ProfileFilePath = profileFilePath,
      ProcessNameOrId = processNameOrId,
      Status = "Loading",
      Description = "Trace loading started asynchronously. Call GetTraceLoadStatus() to poll for completion.",
      Timestamp = DateTime.UtcNow
    };
    return JsonSerializer.Serialize(loadingResult, JsonOpts);
  }

  [McpServerTool, Description("Poll the status of an in-progress trace load started by OpenTrace. Call repeatedly until Status is 'Complete' or 'Failed'.")]
  public static string GetTraceLoadStatus()
  {
    if (ProfileSession.PendingLoad == null)
      return Error("GetTraceLoadStatus", "No trace load in progress. Call OpenTrace first.");

    if (!ProfileSession.PendingLoad.IsCompleted)
    {
      return JsonSerializer.Serialize(new
      {
        Action = "GetTraceLoadStatus",
        Status = "Loading",
        Description = "Trace is still loading (symbol resolution, profile processing). Poll again in 10-15 seconds.",
        Timestamp = DateTime.UtcNow
      }, JsonOpts);
    }

    if (ProfileSession.LoadException != null)
    {
      var err = ProfileSession.LoadException.Message;
      ProfileSession.PendingLoad = null;
      return Error("GetTraceLoadStatus", err);
    }

    if (ProfileSession.LoadedProfile != null)
    {
      ProfileSession.PendingLoad = null;

      // Build symbol resolution summary from the report.
      object[]? moduleReport = null;
      if (ProfileSession.Report?.Modules != null)
      {
        moduleReport = ProfileSession.Report.Modules
          .OrderByDescending(m => m.HasDebugInfoLoaded ? 0 : 1)
          .Select(m => (object)new
          {
            Module = m.ImageFileInfo?.ImageName ?? "Unknown",
            SymbolsLoaded = m.HasDebugInfoLoaded,
            BinaryState = m.State.ToString(),
            PdbPath = m.DebugInfoFile?.FilePath,
            BinaryPath = m.BinaryFileInfo?.FilePath
          }).ToArray();
      }

      return JsonSerializer.Serialize(new
      {
        Action = "GetTraceLoadStatus",
        Status = "Complete",
        Description = $"Trace loaded successfully. {ProfileSession.LoadedProcessIds.Count} process(es), {ProfileSession.LoadedProfile.FunctionProfiles.Count} functions found.",
        ProcessCount = ProfileSession.LoadedProcessIds.Count,
        ProcessIds = ProfileSession.LoadedProcessIds,
        FunctionCount = ProfileSession.LoadedProfile.FunctionProfiles.Count,
        ModuleCount = ProfileSession.LoadedProfile.Modules?.Count ?? 0,
        SymbolResolution = moduleReport,
        Timestamp = DateTime.UtcNow
      }, JsonOpts);
    }

    ProfileSession.PendingLoad = null;
    return Error("GetTraceLoadStatus", "Load completed but no profile data available");
  }

  [McpServerTool, Description("Get the list of available functions from the currently loaded process/trace")]
  public static string GetAvailableFunctions(
    [Description("Filter by module/DLL name (e.g. 'ntdll.dll', 'kernel32.dll'). Supports partial matching.")]
    string? moduleName = null,
    [Description("Minimum self-time percentage threshold (e.g. 0.1 for >=0.1% CPU usage).")]
    double? minSelfTimePercentage = null,
    [Description("Minimum total-time percentage threshold (e.g. 0.5 for >=0.5% total impact).")]
    double? minTotalTimePercentage = null,
    [Description("Limit results to top N functions (e.g. 10).")]
    int? topCount = null,
    [Description("Sort by self-time (true, default) or total-time (false).")]
    bool sortBySelfTime = true)
  {
    var profile = ProfileSession.LoadedProfile;
    if (profile == null)
      return Error("GetAvailableFunctions", "No profile loaded. Call OpenTrace and wait for completion first.");

    var totalWeight = ProfileSession.TotalWeight;
    var totalWeightMs = totalWeight.TotalMilliseconds;

    // Build function list
    var functions = profile.FunctionProfiles.Select(kvp =>
    {
      var func = kvp.Key;
      var data = kvp.Value;
      double selfPct = totalWeightMs > 0 ? data.ExclusiveWeight.TotalMilliseconds / totalWeightMs * 100 : 0;
      double totalPct = totalWeightMs > 0 ? data.Weight.TotalMilliseconds / totalWeightMs * 100 : 0;
      return new
      {
        Name = ResolveFunctionName(profile, func),
        ModuleName = func.ModuleName ?? "Unknown",
        SelfTimePercentage = Math.Round(selfPct, 2),
        TotalTimePercentage = Math.Round(totalPct, 2),
        SelfTime = data.ExclusiveWeight.ToString(),
        TotalTime = data.Weight.ToString()
      };
    });

    // Apply filters
    if (!string.IsNullOrWhiteSpace(moduleName))
      functions = functions.Where(f => f.ModuleName.Contains(moduleName, StringComparison.OrdinalIgnoreCase));
    if (minSelfTimePercentage.HasValue)
      functions = functions.Where(f => f.SelfTimePercentage >= minSelfTimePercentage.Value);
    if (minTotalTimePercentage.HasValue)
      functions = functions.Where(f => f.TotalTimePercentage >= minTotalTimePercentage.Value);

    // Sort and limit
    var resultList = sortBySelfTime
      ? functions.OrderByDescending(f => f.SelfTimePercentage).ToList()
      : functions.OrderByDescending(f => f.TotalTimePercentage).ToList();

    if (topCount.HasValue)
      resultList = resultList.Take(topCount.Value).ToList();

    var result = new
    {
      Action = "GetAvailableFunctions",
      Status = "Success",
      TotalFunctionCount = profile.FunctionProfiles.Count,
      FilteredFunctionCount = resultList.Count,
      Functions = resultList.ToArray(),
      Timestamp = DateTime.UtcNow
    };
    return JsonSerializer.Serialize(result, JsonOpts);
  }

  [McpServerTool, Description("Get the list of available binaries/DLLs from the currently loaded process/trace")]
  public static string GetAvailableBinaries(
    [Description("Minimum time percentage threshold to filter binaries.")]
    double? minTimePercentage = null,
    [Description("Minimum absolute time in milliseconds to filter binaries.")]
    double? minTimeMs = null,
    [Description("Limit results to top N binaries.")]
    int? topCount = null)
  {
    var profile = ProfileSession.LoadedProfile;
    if (profile == null)
      return Error("GetAvailableBinaries", "No profile loaded. Call OpenTrace and wait for completion first.");

    var totalWeight = ProfileSession.TotalWeight;
    var totalWeightMs = totalWeight.TotalMilliseconds;

    // Aggregate by module
    var moduleAgg = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
    foreach (var kvp in profile.FunctionProfiles)
    {
      var modName = kvp.Key.ModuleName ?? "Unknown";
      if (!moduleAgg.TryGetValue(modName, out var existing))
        existing = TimeSpan.Zero;
      moduleAgg[modName] = existing + kvp.Value.ExclusiveWeight;
    }

    var binaries = moduleAgg.Select(kvp =>
    {
      double pct = totalWeightMs > 0 ? kvp.Value.TotalMilliseconds / totalWeightMs * 100 : 0;
      return new
      {
        Name = kvp.Key,
        TimePercentage = Math.Round(pct, 2),
        Time = kvp.Value.ToString(),
        TimeMs = kvp.Value.TotalMilliseconds
      };
    });

    if (minTimePercentage.HasValue)
      binaries = binaries.Where(b => b.TimePercentage >= minTimePercentage.Value);
    if (minTimeMs.HasValue)
      binaries = binaries.Where(b => b.TimeMs >= minTimeMs.Value);

    var sorted = binaries.OrderByDescending(b => b.TimePercentage);
    var resultList = topCount.HasValue ? sorted.Take(topCount.Value).ToArray() : sorted.ToArray();

    var result = new
    {
      Action = "GetAvailableBinaries",
      Status = "Success",
      TotalBinaryCount = moduleAgg.Count,
      FilteredBinaryCount = resultList.Length,
      Binaries = resultList,
      Timestamp = DateTime.UtcNow
    };
    return JsonSerializer.Serialize(result, JsonOpts);
  }

  [McpServerTool, Description("Get assembly code for a specific function by name")]
  public static async Task<string> GetFunctionAssembly(string functionName)
  {
    DiagnosticLogger.LogInfo($"[MCP] GetFunctionAssembly called: functionName={functionName}");
    var sw = Stopwatch.StartNew();

    var profile = ProfileSession.LoadedProfile;
    if (profile == null)
      return Error("GetFunctionAssembly", "No profile loaded. Call OpenTrace and wait for completion first.");

    var match = FindFunction(profile, functionName);

    if (match == null)
      return Error("GetFunctionAssembly", $"Function '{functionName}' not found");

    var data = profile.GetFunctionProfile(match);
    var debugInfo = data.FunctionDebugInfo;

    // Try to resolve source lines via the provider's debug info
    IDebugInfoProvider? moduleDebugInfo = null;
    if (ProfileSession.Provider != null)
      moduleDebugInfo = ProfileSession.Provider.GetDebugInfoForFunction(match);

    if (moduleDebugInfo != null && debugInfo != null && !debugInfo.HasSourceLines)
      moduleDebugInfo.PopulateSourceLines(debugInfo);

    // Try to disassemble the function using capstone
    Dictionary<long, string>? disasmMap = null;
    long imageBase = 0;
    if (ProfileSession.Provider != null && debugInfo != null && ProfileSession.SymbolSettings != null)
    {
      try
      {
        string? asmText = await ProfileSession.Provider.DisassembleFunctionAsync(
          match, debugInfo, ProfileSession.SymbolSettings);
        if (!string.IsNullOrEmpty(asmText))
        {
          // Parse disassembly text: "{absoluteAddr:X}:    {mnemonic}  {operands}"
          // Convert absolute addresses to function-relative offsets
          disasmMap = new Dictionary<long, string>();
          foreach (var line in asmText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
          {
            int colonIdx = line.IndexOf(':');
            if (colonIdx > 0 && long.TryParse(line[..colonIdx].Trim(),
                System.Globalization.NumberStyles.HexNumber, null, out long absAddr))
            {
              if (imageBase == 0) imageBase = absAddr - debugInfo.RVA;
              long funcOffset = absAddr - imageBase - debugInfo.RVA;
              string instruction = line[(colonIdx + 1)..].Trim();
              disasmMap[funcOffset] = instruction;
            }
          }
        }
      }
      catch (Exception ex)
      {
        DiagnosticLogger.LogError($"[MCP] Disassembly failed for {functionName}: {ex.Message}", ex);
        Trace.TraceError($"Disassembly failed for {functionName}: {ex.Message}");
      }
    }

    // Adjust for CPU IP skid: sampled IPs often point to the instruction AFTER the
    // one that was actually executing. When we have a disassembly map, shift each
    // sample's weight to the preceding valid instruction offset (same as UI's
    // TryFindElementForOffset with InitialMultiplier=1).
    var adjustedWeights = data.InstructionWeight;
    if (disasmMap != null && data.InstructionWeight != null)
    {
      var adjusted = new Dictionary<long, TimeSpan>();
      foreach (var kv in data.InstructionWeight)
      {
        long adjustedOffset = kv.Key;
        // Search backwards (up to 16 bytes) for the nearest valid instruction
        for (long delta = 1; delta <= 16; delta++)
        {
          long candidate = kv.Key - delta;
          if (candidate >= 0 && disasmMap.ContainsKey(candidate))
          {
            adjustedOffset = candidate;
            break;
          }
        }
        if (adjusted.ContainsKey(adjustedOffset))
          adjusted[adjustedOffset] += kv.Value;
        else
          adjusted[adjustedOffset] = kv.Value;
      }
      adjustedWeights = adjusted;
    }

    var totalWeightMs = ProfileSession.TotalWeight.TotalMilliseconds;

    var instructionWeights = adjustedWeights?.OrderByDescending(kv => kv.Value)
      .Take(30)
      .Select(kv =>
      {
        string? sourceFile = null;
        int? sourceLine = null;

        // Use per-RVA DIA lookup (same as UI) for accurate source line mapping
        object[]? inlinees = null;
        if (moduleDebugInfo != null && debugInfo != null)
        {
          long rva = kv.Key + debugInfo.RVA;
          var lineInfo = moduleDebugInfo.FindSourceLineByRVA(rva, includeInlinees: true);
          if (!lineInfo.IsUnknown)
          {
            sourceLine = lineInfo.Line;
            sourceFile = lineInfo.FilePath;
          }
          if (lineInfo.Inlinees is { Count: > 0 })
          {
            inlinees = lineInfo.Inlinees.Select(i => (object)new
            {
              Function = i.Function,
              FilePath = i.FilePath,
              Line = i.Line
            }).ToArray();
          }
        }

        // Look up disassembly text for this offset
        string? asmInstruction = null;
        disasmMap?.TryGetValue(kv.Key, out asmInstruction);

        double pctOfFunc = data.ExclusiveWeight.TotalMilliseconds > 0
          ? kv.Value.TotalMilliseconds / data.ExclusiveWeight.TotalMilliseconds * 100 : 0;

        return new
        {
          Offset = $"0x{kv.Key:X}",
          Assembly = asmInstruction,
          WeightMs = Math.Round(kv.Value.TotalMilliseconds, 1),
          PctOfFunction = Math.Round(pctOfFunc, 1),
          PctOfTrace = totalWeightMs > 0 ? Math.Round(kv.Value.TotalMilliseconds / totalWeightMs * 100, 2) : 0,
          SourceFile = sourceFile,
          SourceLine = sourceLine,
          Inlinees = inlinees
        };
      })
      .ToArray();

    var result = new
    {
      Action = "GetFunctionAssembly",
      Status = "Success",
      FunctionName = ResolveFunctionName(profile, new ProfileFunctionId(match.ModuleName, match.Name)),
      ModuleName = match.ModuleName ?? "Unknown",
      SelfTime = data.ExclusiveWeight.ToString(),
      TotalTime = data.Weight.ToString(),
      SelfPct = totalWeightMs > 0 ? Math.Round(data.ExclusiveWeight.TotalMilliseconds / totalWeightMs * 100, 2) : 0,
      HasDisassembly = disasmMap != null,
      InstructionCount = data.InstructionWeight?.Count ?? 0,
      InstructionWeights = instructionWeights,
      Timestamp = DateTime.UtcNow
    };
    return JsonSerializer.Serialize(result, JsonOpts);
  }

  [McpServerTool, Description(
    "Deterministic, structured, address-based native disassembly for a single x64/ARM64 address — " +
    "independent of any loaded trace or CPU sample aggregation. Given an exact binary path and a " +
    "module RVA or absolute instruction pointer, resolves trustworthy function bounds from a supplied " +
    "PDB (pdbPath) or, when omitted/unresolved, from the PE exception directory (.pdata/unwind info); " +
    "never scans .text unbounded and never guesses. Set frameKind='ReturnAddress' for addresses taken " +
    "from a stack frame's return slot to normalize to the preceding call instruction when " +
    "deterministically possible. Returns explicit FailureReason values (e.g. FunctionBoundsNotResolved, " +
    "UnsupportedArchitecture, RvaNotInCodeSection) instead of a partial/best-effort result on failure.")]
  public static string DisassembleAddress(
    string binaryPath,
    string address,
    string addressForm = "ModuleRva",
    string frameKind = "InstructionPointer",
    string? imageBase = null,
    string? pdbPath = null)
  {
    DiagnosticLogger.LogInfo($"[MCP] DisassembleAddress called: binaryPath={binaryPath}, address={address}, " +
                             $"addressForm={addressForm}, frameKind={frameKind}, pdbPath={pdbPath}");

    if (!TryParseAddress(address, out long parsedAddress))
      return Error("DisassembleAddress", $"Could not parse address '{address}' (expected hex like '0x1400' or decimal).");

    if (!Enum.TryParse<NativeAddressForm>(addressForm, ignoreCase: true, out var parsedAddressForm))
      return Error("DisassembleAddress",
        $"Invalid addressForm '{addressForm}'. Expected 'ModuleRva' or 'AbsoluteInstructionPointer'.");

    if (!Enum.TryParse<FrameAddressKind>(frameKind, ignoreCase: true, out var parsedFrameKind))
      return Error("DisassembleAddress",
        $"Invalid frameKind '{frameKind}'. Expected 'InstructionPointer' or 'ReturnAddress'.");

    long? parsedImageBase = null;
    if (!string.IsNullOrWhiteSpace(imageBase))
    {
      if (!TryParseAddress(imageBase, out long imageBaseValue))
        return Error("DisassembleAddress", $"Could not parse imageBase '{imageBase}' (expected hex like '0x140000000' or decimal).");
      parsedImageBase = imageBaseValue;
    }

    PdbSymbolProvider? symbolProvider = null;
    try
    {
      if (!string.IsNullOrWhiteSpace(pdbPath))
      {
        if (!File.Exists(pdbPath))
          return Error("DisassembleAddress", $"PDB file not found: {pdbPath}");

        symbolProvider = new PdbSymbolProvider();
        if (!symbolProvider.LoadDebugInfo(pdbPath))
          return Error("DisassembleAddress", $"Failed to load PDB: {pdbPath}");
      }

      var result = NativeAddressDisassembler.Resolve(new NativeAddressDisassemblyRequest
      {
        BinaryPath = binaryPath,
        Address = parsedAddress,
        AddressForm = parsedAddressForm,
        FrameKind = parsedFrameKind,
        ImageBase = parsedImageBase,
        SymbolDebugInfo = symbolProvider
      });

      var response = new
      {
        Action = "DisassembleAddress",
        Status = result.Success ? "Success" : "Error",
        FailureReason = result.FailureReason.ToString(),
        Error = result.Success ? null : result.ErrorMessage,
        ModuleName = result.ModuleName,
        Architecture = result.Architecture?.ToString(),
        RequestedRva = $"0x{result.RequestedRva:X}",
        RequestedAddress = $"0x{result.RequestedAddress:X}",
        NormalizedRva = $"0x{result.NormalizedRva:X}",
        NormalizedAddress = $"0x{result.NormalizedAddress:X}",
        WasNormalized = result.WasNormalized,
        QualifiedName = result.QualifiedName,
        Range = result.Range is { } range ? new
        {
          StartRva = $"0x{range.StartRva:X}",
          EndRva = $"0x{range.EndRva:X}",
          Size = range.Size,
          Provenance = range.Provenance.ToString(),
          FunctionName = range.FunctionName,
          RuntimeFunctionKind = range.RuntimeFunctionKind?.ToString()
        } : null,
        Instructions = result.Instructions.Select(i => new
        {
          Rva = $"0x{i.Rva:X}",
          Address = $"0x{i.Address:X}",
          Size = i.Size,
          Text = i.Text,
          Mnemonic = i.Mnemonic,
          Operands = i.OperandText,
          Target = i.Target is { } target ? new
          {
            Rva = $"0x{target.Rva:X}",
            Address = $"0x{target.Address:X}",
            SymbolName = target.SymbolName,
            IsCall = target.IsCall,
            IsJump = target.IsJump
          } : null
        }).ToArray(),
        Timestamp = DateTime.UtcNow
      };

      return JsonSerializer.Serialize(response, JsonOpts);
    }
    finally
    {
      symbolProvider?.Dispose();
    }
  }

  /// <summary>Parses an address as hex (with or without a "0x" prefix) or decimal.</summary>
  private static bool TryParseAddress(string text, out long value)
  {
    string trimmed = text.Trim();
    if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
      return long.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out value);

    if (long.TryParse(trimmed, out value))
      return true;

    return long.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out value);
  }

  [McpServerTool, Description("Get callers and callees for a function, showing who calls it and what it calls, with call stack traces")]
  public static string GetFunctionCallerCallee(
    string functionName,
    [Description("Max number of callers to return (default 10)")]
    int? maxCallers = null,
    [Description("Max number of callees to return (default 10)")]
    int? maxCallees = null,
    [Description("Max number of full back-traces (call stacks) to return (default 5)")]
    int? maxBacktraces = null)
  {
    DiagnosticLogger.LogInfo($"[MCP] GetFunctionCallerCallee called: functionName={functionName}, maxCallers={maxCallers}, maxCallees={maxCallees}, maxBacktraces={maxBacktraces}");

    var profile = ProfileSession.LoadedProfile;
    if (profile == null)
      return Error("GetFunctionCallerCallee", "No profile loaded. Call OpenTrace and wait for completion first.");

    if (profile.CallTree == null)
      return Error("GetFunctionCallerCallee", "No call tree data available in this profile.");

    var match = FindFunction(profile, functionName);
    if (match == null)
      return Error("GetFunctionCallerCallee", $"Function '{functionName}' not found");

    var data = profile.GetFunctionProfile(match);
    var totalWeightMs = ProfileSession.TotalWeight.TotalMilliseconds;
    var functionWeightMs = data.Weight.TotalMilliseconds;
    int callerLimit = maxCallers ?? 10;
    int calleeLimit = maxCallees ?? 10;
    int backtraceLimit = maxBacktraces ?? 5;

    // Get all call tree instances of this function, sorted by weight
    var instances = profile.CallTree.GetSortedCallTreeNodes(match.ToProfileId());
    if (instances == null || instances.Count == 0)
      return Error("GetFunctionCallerCallee", $"Function '{functionName}' has no call tree nodes");

    // Aggregate callers across all instances
    var callerAgg = new Dictionary<string, (TimeSpan weight, TimeSpan exclusiveWeight, string module)>();
    foreach (var inst in instances)
    {
      foreach (var caller in inst.Callers)
      {
        if (caller is not {HasFunction: true}) continue;
        var key = $"{caller.ModuleName}!{ResolveFunctionName(caller)}";
        if (!callerAgg.TryGetValue(key, out var existing))
          existing = (TimeSpan.Zero, TimeSpan.Zero, caller.ModuleName ?? "Unknown");
        callerAgg[key] = (existing.weight + caller.Weight, existing.exclusiveWeight + caller.ExclusiveWeight, existing.module);
      }
    }

    var callers = callerAgg
      .OrderByDescending(kv => kv.Value.weight)
      .Take(callerLimit)
      .Select(kv => new
      {
        Function = kv.Key,
        InclusiveTimeMs = Math.Round(kv.Value.weight.TotalMilliseconds, 2),
        InclusivePct = totalWeightMs > 0 ? Math.Round(kv.Value.weight.TotalMilliseconds / totalWeightMs * 100, 2) : 0,
        FunctionPct = functionWeightMs > 0 ? Math.Round(kv.Value.weight.TotalMilliseconds / functionWeightMs * 100, 2) : 0
      }).ToArray();

    // Aggregate callees from the combined node's children
    var combined = profile.CallTree.GetCombinedCallTreeNode(match.ToProfileId());
    var callees = Array.Empty<object>();
    if (combined != null && combined.HasChildren)
    {
      callees = combined.Children
        .Where(c => c is {HasFunction: true})
        .OrderByDescending(c => c.Weight)
        .Take(calleeLimit)
        .Select(c => new
        {
          Function = $"{c.ModuleName}!{ResolveFunctionName(c)}",
          InclusiveTimeMs = Math.Round(c.Weight.TotalMilliseconds, 2),
          InclusivePct = totalWeightMs > 0 ? Math.Round(c.Weight.TotalMilliseconds / totalWeightMs * 100, 2) : 0,
          FunctionPct = functionWeightMs > 0 ? Math.Round(c.Weight.TotalMilliseconds / functionWeightMs * 100, 2) : 0,
          SelfTimeMs = Math.Round(c.ExclusiveWeight.TotalMilliseconds, 2),
          SelfPct = totalWeightMs > 0 ? Math.Round(c.ExclusiveWeight.TotalMilliseconds / totalWeightMs * 100, 2) : 0
        }).ToArray<object>();
    }

    // Get top backtraces (full call stacks leading to this function)
    var backtraces = instances
      .Take(backtraceLimit)
      .Select(inst =>
      {
        var bt = profile.CallTree.GetBacktrace(inst);
        return new
        {
          WeightMs = Math.Round(inst.Weight.TotalMilliseconds, 2),
          WeightPct = totalWeightMs > 0 ? Math.Round(inst.Weight.TotalMilliseconds / totalWeightMs * 100, 2) : 0,
          FunctionPct = functionWeightMs > 0 ? Math.Round(inst.Weight.TotalMilliseconds / functionWeightMs * 100, 2) : 0,
          Stack = bt.Select(n => $"{n.ModuleName}!{ResolveFunctionName(n)}").ToArray()
        };
      }).ToArray();

    var result = new
    {
      Action = "GetFunctionCallerCallee",
      Status = "Success",
      FunctionName = ResolveFunctionName(profile, new ProfileFunctionId(match.ModuleName, match.Name)),
      ModuleName = match.ModuleName ?? "Unknown",
      SelfTime = data.ExclusiveWeight.ToString(),
      TotalTime = data.Weight.ToString(),
      SelfPct = totalWeightMs > 0 ? Math.Round(data.ExclusiveWeight.TotalMilliseconds / totalWeightMs * 100, 2) : 0,
      TotalPct = totalWeightMs > 0 ? Math.Round(data.Weight.TotalMilliseconds / totalWeightMs * 100, 2) : 0,
      InstanceCount = instances.Count,
      Callers = callers,
      Callees = callees,
      TopBacktraces = backtraces,
      Timestamp = DateTime.UtcNow
    };
    return JsonSerializer.Serialize(result, JsonOpts);
  }

  [McpServerTool, Description("Close the currently loaded trace and reset all session state. Use this to abandon a stuck load or free resources before loading a new trace.")]
  public static string CloseTrace()
  {
    DiagnosticLogger.LogInfo("[MCP] CloseTrace called");
    ProfileSession.Reset();
    return JsonSerializer.Serialize(new
    {
      Action = "CloseTrace",
      Status = "Success",
      Description = "Trace closed and all session state reset.",
      Timestamp = DateTime.UtcNow
    }, JsonOpts);
  }

  [McpServerTool, Description("Get help information about available MCP commands")]
  public static string GetHelp()
  {
    var help = new
    {
      ServerName = "Profile Explorer Headless MCP Server",
      Version = "2.0.0",
      Description = "Headless MCP server for Profile Explorer — no GUI, direct engine access",
      Workflow = new[]
      {
        "1. GetAvailableProcesses(filePath) — discover processes in a trace",
        "2. OpenTrace(filePath, processNameOrId) — start loading (async). Name matches ALL processes (e.g. 'diskspd' loads all 4). Comma-separated IDs also supported.",
        "3. GetTraceLoadStatus() — poll until 'Complete'",
        "4. GetAvailableFunctions/GetAvailableBinaries — query the loaded profile",
        "5. GetFunctionAssembly(name) — get instruction-level hotspot data",
        "6. GetFunctionCallerCallee(name) — get callers, callees, and full call stacks",
        "7. CloseTrace() — close trace and reset state (required before loading a new trace)"
      },
      IndependentTools = new[]
      {
        "DisassembleAddress(binaryPath, address, addressForm, frameKind, imageBase, pdbPath) — " +
        "deterministic, structured disassembly at an exact module RVA or absolute instruction " +
        "pointer for x64/ARM64, independent of any loaded trace. Resolves bounds from an optional " +
        "PDB or the PE exception directory (.pdata); never scans .text unbounded; returns explicit " +
        "failures (e.g. FunctionBoundsNotResolved) instead of guessing."
      }
    };
    return JsonSerializer.Serialize(help, JsonOpts);
  }

  // Prefer exact name match, then fall back to Contains
  /// <summary>
  /// Returns the PDB-resolved name for a function, falling back to IRTextFunction.Name (hex placeholder).
  /// </summary>
  private static string ResolveFunctionName(ProfileData profile, ProfileFunctionId func)
  {
    var data = profile?.GetFunctionProfile(func);
    var debugName = data?.FunctionDebugInfo?.Name;
    return !string.IsNullOrEmpty(debugName) ? debugName : func.FunctionName;
  }

  /// <summary>
  /// Returns the PDB-resolved name for a call tree node.
  /// </summary>
  private static string ResolveFunctionName(ProfileCallTreeNode node)
  {
    var debugName = node.FunctionDebugInfo?.Name;
    return !string.IsNullOrEmpty(debugName) ? debugName : node.FunctionName ?? "Unknown";
  }

  private static IRTextFunction? FindFunction(ProfileData profile, string functionName)
  {
    var id = FindFunctionId(profile, functionName);
    return id.HasValue ? profile.ResolveFunction(id.Value) : null;
  }

  private static ProfileFunctionId? FindFunctionId(ProfileData profile, string functionName)
  {
    // 1. Exact match on PDB-resolved (possibly decorated) name.
    foreach (var f in profile.FunctionProfiles.Keys)
      if (ResolveFunctionName(profile, f).Equals(functionName, StringComparison.OrdinalIgnoreCase)) return f;

    // 2. Exact match on demangled name (callers pass human-readable names; PE stores decorated MSVC names).
    if (ProfileSession.DemangledFunctionLookup == null)
    {
      // Build once per trace load — demangling is not thread-safe so do it lazily here.
      var lookup = new Dictionary<string, ProfileFunctionId>(StringComparer.OrdinalIgnoreCase);
      foreach (var f in profile.FunctionProfiles.Keys)
      {
        var raw = ResolveFunctionName(profile, f);
        var demangled = PDBDebugInfoProvider.DemangleFunctionName(raw,
          FunctionNameDemanglingOptions.OnlyName | FunctionNameDemanglingOptions.NoReturnType |
          FunctionNameDemanglingOptions.NoSpecialKeywords);
        lookup.TryAdd(demangled, f);
        lookup.TryAdd(raw, f);      // also keep decorated so we re-use this dict for all lookups
      }
      ProfileSession.DemangledFunctionLookup = lookup;
    }

    if (ProfileSession.DemangledFunctionLookup.TryGetValue(functionName, out var demangledMatch))
      return demangledMatch;

    // 3. Exact match on neutral function name (hex placeholder when no symbols).
    foreach (var f in profile.FunctionProfiles.Keys)
      if (f.FunctionName.Equals(functionName, StringComparison.OrdinalIgnoreCase)) return f;

    // 4. Contains on demangled name (partial match).
    var containsMatch = ProfileSession.DemangledFunctionLookup.Keys
      .FirstOrDefault(k => k.Contains(functionName, StringComparison.OrdinalIgnoreCase));
    if (containsMatch != null && ProfileSession.DemangledFunctionLookup.TryGetValue(containsMatch, out var partialMatch))
      return partialMatch;

    // 5. Contains on decorated name, then on neutral/hex placeholder name.
    foreach (var f in profile.FunctionProfiles.Keys)
      if (ResolveFunctionName(profile, f).Contains(functionName, StringComparison.OrdinalIgnoreCase)) return f;
    foreach (var f in profile.FunctionProfiles.Keys)
      if (f.FunctionName.Contains(functionName, StringComparison.OrdinalIgnoreCase)) return f;
    return null;
  }

  private static string Error(string action, string message)
  {
    return JsonSerializer.Serialize(new
    {
      Action = action,
      Status = "Error",
      Error = message,
      Timestamp = DateTime.UtcNow
    }, JsonOpts);
  }
}
