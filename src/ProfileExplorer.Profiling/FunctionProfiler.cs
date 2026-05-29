// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Profiling.Disassembly;
using ProfileExplorer.Profiling.Profiling;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Profiling;

/// <summary>
/// Main entry point for function-level CPU profiling with disassembly annotation.
/// Consumes CPU samples from any source (DataLayer, TraceEvent, etc.) via <see cref="IProfileSample"/>,
/// resolves symbols via its own PDB reader, and produces per-function/per-instruction profiles
/// with optional annotated disassembly.
/// </summary>
public class FunctionProfiler : IDisposable {
  private readonly ProfilerOptions options_;
  private readonly SymbolServerClient symbolServer_;
  private readonly IpResolver ipResolver_;
  private readonly SampleAggregator sampleAggregator_;
  private readonly CallTreeBuilder callTreeBuilder_;
  private readonly CounterAggregator? counterAggregator_;
  private readonly ManagedMethodResolver? managedResolver_;

  private readonly Dictionary<string, IProfileImage> imagesByModule_ = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, IDebugInfoProvider> debugInfoByModule_ = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, string> pdbPathByModule_ = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, string> binaryPathByModule_ = new(StringComparer.OrdinalIgnoreCase);

  private IReadOnlyList<FunctionProfile>? cachedProfiles_;
  private bool symbolsLoaded_;

  public FunctionProfiler(ProfilerOptions options) {
    options.Validate();
    options_ = options;
    symbolServer_ = new SymbolServerClient(options);

    managedResolver_ = options.IncludeManagedCode ? new ManagedMethodResolver() : null;
    ipResolver_ = new IpResolver(managedResolver_);
    sampleAggregator_ = new SampleAggregator(ipResolver_);
    callTreeBuilder_ = new CallTreeBuilder(ipResolver_);
    counterAggregator_ = options.IncludePerformanceCounters ? new CounterAggregator(ipResolver_) : null;
  }

  /// <summary>
  /// Register loaded images (modules) with their PDB identity for symbol resolution.
  /// </summary>
  public void AddImages(IEnumerable<IProfileImage> images) {
    foreach (var image in images) {
      string key = image.ImageName;
      imagesByModule_[key] = image;
      ipResolver_.AddImage(key, image.BaseAddress, image.Size);
    }

    cachedProfiles_ = null;
  }

  /// <summary>
  /// Add CPU samples. Can be called multiple times (e.g., per-processor batches).
  /// </summary>
  public void AddSamples(IEnumerable<IProfileSample> samples) {
    var sampleList = samples as IReadOnlyList<IProfileSample> ?? samples.ToList();
    sampleAggregator_.AddSamples(sampleList);
    callTreeBuilder_.AddSamples(sampleList);
    cachedProfiles_ = null;
  }

  /// <summary>
  /// Add hardware performance counter events (PMU/PMC).
  /// Only processed if <see cref="ProfilerOptions.IncludePerformanceCounters"/> is true.
  /// </summary>
  public void AddPerformanceCounterEvents(IEnumerable<IPerformanceCounterEvent> events) {
    counterAggregator_?.AddEvents(events);
  }

  /// <summary>
  /// Register managed/.NET method mappings (from CLR JIT events).
  /// Only processed if <see cref="ProfilerOptions.IncludeManagedCode"/> is true.
  /// </summary>
  public void AddManagedMethods(IEnumerable<IManagedMethodMapping> methods) {
    if (managedResolver_ == null) return;

    foreach (var method in methods) {
      managedResolver_.AddMethod(method);
    }
  }

  /// <summary>
  /// Load symbols for all registered images. Downloads PDBs from the symbol server.
  /// </summary>
  public async Task LoadSymbolsAsync(CancellationToken ct = default) {
    if (symbolsLoaded_) return;

    foreach (var (moduleName, image) in imagesByModule_) {
      if (image.PdbGuid == Guid.Empty) continue;

      try {
        string pdbName = !string.IsNullOrEmpty(image.PdbName)
          ? Path.GetFileName(image.PdbName)
          : Path.ChangeExtension(image.ImageName, ".pdb");

        string? pdbPath = await symbolServer_.FindSymbolFileAsync(pdbName, image.PdbGuid, image.PdbAge, ct);
        if (pdbPath == null) continue;

        pdbPathByModule_[moduleName] = pdbPath;

        // Load debug info and register function list with the IP resolver.
        var provider = new PdbSymbolProvider();
        if (provider.LoadDebugInfo(pdbPath)) {
          debugInfoByModule_[moduleName] = provider;
          var sortedFunctions = provider.GetSortedFunctions();
          if (sortedFunctions.Count > 0) {
            ipResolver_.SetFunctions(moduleName, sortedFunctions);
          }
          else {
            Console.Error.WriteLine($"  PDB loaded but 0 functions: {moduleName} ({pdbPath})");
          }
        }
        else {
          Console.Error.WriteLine($"  PDB load FAILED: {moduleName} - {PdbSymbolProvider.DiaRegistrationError}");
          provider.Dispose();
        }
      }
      catch (Exception) {
        // Symbol loading failure for this module — continue with others.
      }
    }

    symbolsLoaded_ = true;
    cachedProfiles_ = null;
  }

  /// <summary>
  /// Build aggregated per-function profiles from added samples.
  /// </summary>
  public IReadOnlyList<FunctionProfile> GetFunctionProfiles(
    string? processName = null,
    int? processId = null) {
    if (cachedProfiles_ != null) return cachedProfiles_;

    var profiles = sampleAggregator_.Build(processName, processId);

    // Enrich with source info and counter data.
    var enrichedProfiles = new List<FunctionProfile>(profiles.Count);

    foreach (var profile in profiles) {
      string? sourceFile = profile.SourceFile;
      int? sourceLine = profile.SourceLine;
      bool hasAssembly = profile.HasAssembly;

      // Source file info from debug info.
      if (debugInfoByModule_.TryGetValue(profile.ModuleName, out var debugInfo)) {
        var funcInfo = debugInfo.FindFunctionByRVA(profile.FunctionRva);
        if (funcInfo != null) {
          var sourceFileInfo = debugInfo.FindSourceFilePathByRVA(profile.FunctionRva);
          if (sourceFileInfo.HasFilePath) {
            sourceFile = sourceFileInfo.FilePath;
            sourceLine = sourceFileInfo.StartLine;
          }
        }

        hasAssembly = true;
      }

      var enriched = new FunctionProfile(
        moduleName: profile.ModuleName,
        functionName: profile.FunctionName,
        functionRva: profile.FunctionRva,
        functionSize: profile.FunctionSize,
        inclusiveWeight: profile.InclusiveWeight,
        exclusiveWeight: profile.ExclusiveWeight,
        inclusivePercent: profile.InclusivePercent,
        exclusivePercent: profile.ExclusivePercent,
        sourceFile: sourceFile,
        sourceLine: sourceLine,
        isManaged: profile.IsManaged,
        instructionWeights: (Dictionary<long, TimeSpan>)profile.InstructionWeights);

      enriched.HasAssembly = hasAssembly;

      // Counter data.
      if (counterAggregator_ != null) {
        var counters = counterAggregator_.GetCounters(enriched.QualifiedName);
        if (counters != null) {
          enriched.InstructionCounters = counters;
        }
      }

      enrichedProfiles.Add(enriched);
    }

    // Filter by minimum self percent.
    if (options_.MinSelfPercent > 0) {
      enrichedProfiles = enrichedProfiles.Where(p => p.ExclusivePercent >= options_.MinSelfPercent).ToList();
    }

    cachedProfiles_ = enrichedProfiles;
    return enrichedProfiles;
  }

  /// <summary>
  /// Build a call tree from added samples. Requires StackFrames on IProfileSample.
  /// </summary>
  public CallTreeNode GetCallTree(
    string? processName = null,
    int? processId = null) {
    return callTreeBuilder_.Build();
  }

  /// <summary>
  /// Get annotated disassembly for a specific function.
  /// Downloads the binary on-demand, disassembles via Capstone, and annotates with timing data.
  /// </summary>
  public async Task<AnnotatedAssembly?> GetAnnotatedAssemblyAsync(
    FunctionProfile function,
    CancellationToken ct = default) {
    // Download binary if not already cached.
    if (!binaryPathByModule_.TryGetValue(function.ModuleName, out var binaryPath)) {
      if (imagesByModule_.TryGetValue(function.ModuleName, out var image)) {
        binaryPath = await symbolServer_.FindBinaryFileAsync(
          image.ImageName, image.TimeDateStamp, image.Size, ct);

        if (binaryPath != null) {
          binaryPathByModule_[function.ModuleName] = binaryPath;
        }
      }
    }

    if (binaryPath == null) {
      // Binary not available — try to return hot lines from instruction weights
      // without disassembly. Avoids DIA COM calls (AccessViolationException risk).
      Console.Error.WriteLine($"  Binary not found for {function.ModuleName}, falling back to instruction weights ({function.InstructionWeights.Count} offsets, {function.ExclusiveWeight.TotalMilliseconds:F1}ms)");
      try {
        var result = GetHotLinesWithoutBinary(function);
        Console.Error.WriteLine($"  GetHotLinesWithoutBinary: {(result != null ? $"{result.HotLines.Count} hot lines" : "null")}");
        return result;
      }
      catch (Exception ex) {
        Console.Error.WriteLine($"  GetHotLinesWithoutBinary failed for {function.QualifiedName}: {ex.GetType().Name}: {ex.Message}");
        return null;
      }
    }

    // Get image base for address calculation.
    long imageBase = 0;
    if (imagesByModule_.TryGetValue(function.ModuleName, out var img)) {
      imageBase = img.BaseAddress;
    }

    // Disassemble.
    using var disassembler = new Disassembler();
    var instructions = disassembler.DisassembleFunction(
      binaryPath,
      function.FunctionRva,
      function.FunctionSize,
      imageBase,
      options_.Architecture,
      debugInfoByModule_.GetValueOrDefault(function.ModuleName));

    if (instructions.Count == 0) return null;

    // Get debug info for source line annotation.
    debugInfoByModule_.TryGetValue(function.ModuleName, out var debugInfoProvider);
    FunctionDebugInfo? funcDebugInfo = debugInfoProvider?.FindFunctionByRVA(function.FunctionRva);

    // Annotate.
    return AssemblyAnnotator.Annotate(
      instructions,
      function.InstructionWeights,
      function.FunctionRva,
      debugInfoProvider,
      funcDebugInfo,
      options_.Architecture,
      options_.MinHotLinePercent,
      options_.MaxHotLines);
  }

  public void Dispose() {
    symbolServer_.Dispose();

    foreach (var (_, provider) in debugInfoByModule_) {
      provider.Dispose();
    }

    debugInfoByModule_.Clear();
  }

  /// <summary>
  /// Generate hot lines from instruction weights only, without requiring
  /// the binary for Capstone disassembly. Avoids DIA COM calls to prevent
  /// AccessViolationException from cross-thread COM access.
  /// Uses pre-loaded source file/line from the FunctionProfile if available.
  /// </summary>
  private AnnotatedAssembly? GetHotLinesWithoutBinary(FunctionProfile function) {
    if (function.InstructionWeights.Count == 0) return null;

    var totalWeight = function.InstructionWeights.Values.Aggregate(TimeSpan.Zero, (sum, w) => sum + w);
    var hotLines = new List<HotLine>();
    var lines = new List<AssemblyLine>();
    var sb = new System.Text.StringBuilder();

    // Use source file from FunctionProfile (populated during GetFunctionProfiles enrichment).
    string? sourceFile = function.SourceFile;

    foreach (var (offset, weight) in function.InstructionWeights.OrderByDescending(kv => kv.Value)) {
      double percent = totalWeight > TimeSpan.Zero
        ? weight.TotalMilliseconds / totalWeight.TotalMilliseconds * 100 : 0;

      if (percent < options_.MinHotLinePercent) continue;

      string text = $"[offset +0x{offset:X}]";

      var line = new AssemblyLine(
        address: function.FunctionRva + offset,
        rva: function.FunctionRva + offset,
        instructionText: text,
        weight: weight,
        percent: percent,
        sourceFile: sourceFile,
        sourceLine: null);
      lines.Add(line);

      sb.AppendLine($"{function.FunctionRva + offset:X}:    {text}    [Time(%): {percent:F2}%, Time: {weight.TotalMilliseconds:F2} ms]");

      hotLines.Add(new HotLine(
        instructionOffset: offset,
        percent: percent,
        time: weight,
        instructionText: text,
        sourceFile: sourceFile,
        sourceLine: null));

      if (hotLines.Count >= options_.MaxHotLines) break;
    }

    if (hotLines.Count == 0) return null;

    return new AnnotatedAssembly(sb.ToString(), lines, hotLines);
  }
}
