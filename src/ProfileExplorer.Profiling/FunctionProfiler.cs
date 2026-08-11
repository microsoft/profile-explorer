// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Profile;
using ProfileExplorer.Core.Profile.CallTree;
using ProfileExplorer.Core.Profile.Data;
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
  private readonly ISymbolFileLocator symbolResolver_;
  private readonly bool ownsSymbolResolver_;
  private readonly IpResolver ipResolver_;
  private readonly SampleAggregator sampleAggregator_;
  private readonly CallTreeBuilder callTreeBuilder_;
  private readonly CounterAggregator? counterAggregator_;
  private readonly ManagedMethodResolver? managedResolver_;

  // Images keyed by base address — the identity the resolver uses. Symbol loading iterates this so two
  // modules with the same file name (different binaries at different bases) are each loaded and get
  // their own function set, matching the original Core.
  private readonly Dictionary<long, IProfileImage> imagesByBase_ = new();
  // Disassembly-side caches keyed by the EXACT (case-sensitive) module name — the only identity
  // available on the disassembly path (ProfileFunctionId / functionId is name-based). Case-only-
  // different DLLs stay distinct; two truly same-named binaries share these caches (they also merge in
  // the final profile). Address resolution itself is keyed by base in IpResolver, so it never collides.
  private readonly Dictionary<string, IProfileImage> imagesByModule_ = new(StringComparer.Ordinal);
  private readonly Dictionary<string, ISymbolDebugInfo> debugInfoByModule_ = new(StringComparer.Ordinal);
  private readonly Dictionary<string, string> pdbPathByModule_ = new(StringComparer.Ordinal);
  private readonly Dictionary<string, string> binaryPathByModule_ = new(StringComparer.Ordinal);

  private ProfileReport? cachedReport_;
  private bool symbolsLoaded_;
  // Image bases whose symbol load has already been attempted, so a later (filtered or full) load skips
  // them instead of re-probing. This lets a filtered LoadSymbolsForSamplesAsync be followed by a full
  // LoadSymbolsAsync that still loads the remaining, previously-unfiltered modules. Keyed by base so
  // two same-named modules (different bases) are each attempted independently.
  private readonly HashSet<long> loadedModules_ = new();

  public FunctionProfiler(ProfilerOptions options)
    : this(options, symbolResolver: null) {
  }

  /// <summary>
  /// Creates a profiler with a custom symbol/binary resolver. When <paramref name="symbolResolver"/>
  /// is <c>null</c>, the library's vendored <see cref="SymbolServerClient"/> is used (and owned/disposed
  /// by this instance). When a resolver is supplied, the caller retains ownership of its lifetime.
  /// </summary>
  public FunctionProfiler(ProfilerOptions options, ISymbolFileLocator? symbolResolver)
    : this(options, symbolResolver, validateOptions: true) {
  }

  /// <summary>
  /// Create a profiler for PRE-RESOLVED input only (the host owns symbol resolution): no symbol
  /// paths, download, or option validation are required. Use with <see cref="AddResolvedSample"/> +
  /// <see cref="GetReport"/>. This is how Profile Explorer's ETW load path drives the library.
  /// </summary>
  public static FunctionProfiler CreateForResolvedInput() {
    var options = new ProfilerOptions {
      MinSelfPercent = 0,
      IncludeManagedCode = false,
      IncludePerformanceCounters = false
    };
    return new FunctionProfiler(options, NullSymbolFileLocator.Instance, validateOptions: false);
  }

  private FunctionProfiler(ProfilerOptions options, ISymbolFileLocator? symbolResolver, bool validateOptions) {
    if (validateOptions) {
      options.Validate();
    }

    options_ = options;

    if (symbolResolver != null) {
      symbolResolver_ = symbolResolver;
      ownsSymbolResolver_ = false;
    }
    else {
      symbolResolver_ = new SymbolServerClient(options);
      ownsSymbolResolver_ = true;
    }

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
      imagesByBase_[image.BaseAddress] = image;
      ipResolver_.AddImage(key, image.BaseAddress, image.Size);
    }

    InvalidateReport();
  }

  /// <summary>
  /// OS pointer size in bytes (8 = 64-bit, 4 = 32-bit) for the trace being profiled, used to classify
  /// kernel vs. user instruction pointers for imageless (JITted/unmapped) leaf samples. Set this from
  /// the trace's authoritative metadata (e.g. TraceEvent's <c>ETWTraceEventSource.PointerSize</c>, which
  /// mirrors the ETW <c>SystemConfigCPU</c> value Profile Explorer's main path reads) BEFORE calling
  /// <see cref="AddSamples"/>. When left unset, it is derived from the registered image address space.
  /// Reading returns the effective value (the explicit override if set, otherwise the derived one).
  /// </summary>
  public int PointerSize {
    get => ipResolver_.PointerSize;
    set => ipResolver_.PointerSize = value;
  }

  /// <summary>
  /// Register pre-resolved, RVA-sorted function debug info for a module, bypassing the profiler's
  /// own PDB download and reading. Use this when the host has already acquired and read the module's
  /// symbols (e.g. Profile Explorer, which owns symbol acquisition via TraceEvent): the engine then
  /// resolves and aggregates against these functions instead of loading its own.
  /// <para>
  /// Must be called BEFORE <see cref="AddSamples"/>, because samples are resolved as they are added.
  /// Providing functions this way makes <see cref="LoadSymbolsAsync"/> a no-op.
  /// </para>
  /// </summary>
  /// <param name="moduleName">The image's module name, matching the value registered via <see cref="AddImages"/>.</param>
  /// <param name="baseAddress">The image's base address, matching the value registered via <see cref="AddImages"/>.</param>
  /// <param name="sortedFunctions">The module's functions, sorted by ascending RVA.</param>
  public void AddResolvedFunctions(string moduleName, long baseAddress,
                                   IReadOnlyList<FunctionDebugInfo> sortedFunctions) {
    ArgumentNullException.ThrowIfNull(moduleName);
    ArgumentNullException.ThrowIfNull(sortedFunctions);

    if (!imagesByBase_.TryGetValue(baseAddress, out var image)) {
      throw new ArgumentException(
        $"No image registered at base address 0x{baseAddress:X}; call AddImages before AddResolvedFunctions.",
        nameof(baseAddress));
    }

    if (!string.Equals(image.ImageName, moduleName, StringComparison.OrdinalIgnoreCase)) {
      throw new ArgumentException(
        $"Module name '{moduleName}' does not match image '{image.ImageName}' registered at base address 0x{baseAddress:X}.",
        nameof(moduleName));
    }

    ipResolver_.SetFunctions(baseAddress,
      sortedFunctions as List<FunctionDebugInfo> ?? new List<FunctionDebugInfo>(sortedFunctions));
    symbolsLoaded_ = true; // Host owns symbol acquisition; suppress the profiler's own download path.
    InvalidateReport();
  }

  /// <summary>
  /// Add CPU samples. Can be called multiple times (e.g., per-processor batches). When
  /// <paramref name="instancePath"/> (a root-first call-tree instance path) is provided, only samples
  /// whose stack begins with that path from the root are aggregated (call-tree "focus" filtering).
  /// </summary>
  public void AddSamples(IEnumerable<IProfileSample> samples,
                         IReadOnlyList<ProfileFunctionId>? instancePath = null) {
    var sampleList = samples as IReadOnlyList<IProfileSample> ?? samples.ToList();
    sampleAggregator_.AddSamples(sampleList, instancePath);
    callTreeBuilder_.AddSamples(sampleList, instancePath);
    InvalidateReport();
  }

  /// <summary>
  /// Aggregate a single PRE-RESOLVED sample (the host owns symbol resolution). Frames are leaf-first
  /// and already resolved; the host must omit frames it could not resolve. Feeds both the per-function
  /// profile and the call tree. Call <see cref="GetReport"/> after all samples are added.
  /// </summary>
  public void AddResolvedSample(TimeSpan weight, int threadId, IReadOnlyList<ResolvedFrame> framesLeafFirst,
                                IReadOnlyList<ProfileFunctionId>? instancePath = null) {
    sampleAggregator_.AddResolvedStack(weight, framesLeafFirst, instancePath);
    callTreeBuilder_.AddResolvedStack(weight, threadId, framesLeafFirst, instancePath);
    InvalidateReport();
  }

  /// <summary>
  /// Aggregate a range of PRE-RESOLVED samples IN PARALLEL (the host owns symbol resolution). The
  /// library partitions <c>[startIndex, endIndex)</c> across up to <paramref name="maxWorkers"/>
  /// worker threads; each worker projects its samples via <paramref name="project"/> (invoked
  /// concurrently, so it must be thread-safe), aggregates per-function profiles into the shared
  /// thread-safe map, and builds an isolated per-worker call tree. The per-worker trees are then
  /// merged into one — reproducing the parallelism of the former Core FunctionProfileProcessor /
  /// CallTreeProcessor while keeping aggregation in the library.
  /// <para>
  /// Any host-side filtering (thread, time range, call-tree instance) should be applied inside
  /// <paramref name="project"/> by returning <c>false</c> to EXCLUDE a sample entirely (it counts
  /// toward nothing, not even the total weight). Returning <c>true</c> with an empty frame list keeps
  /// the sample's weight in the total (the percentage denominator) while attributing it to no
  /// function or call-tree node — this is how a passed-filter sample whose stack fully failed to
  /// resolve is handled, matching Core's FunctionProfileProcessor. Call <see cref="GetReport"/>
  /// once this returns.
  /// </para>
  /// </summary>
  /// <param name="startIndex">Inclusive first sample index.</param>
  /// <param name="endIndex">Exclusive last sample index.</param>
  /// <param name="project">Thread-safe projection of a sample into leaf-first <see cref="ResolvedFrame"/>s.</param>
  /// <param name="buildCallTree">When false, skips call-tree construction entirely (only profiles).</param>
  /// <param name="maxWorkers">Max worker threads; &lt;= 0 uses <see cref="Environment.ProcessorCount"/>.</param>
  public void AddResolvedSamplesParallel(int startIndex, int endIndex, ResolvedSampleProjector project,
                                         bool buildCallTree = true, int maxWorkers = 0) {
    ArgumentNullException.ThrowIfNull(project);
    int count = endIndex - startIndex;
    if (count <= 0) return;

    int workers = maxWorkers > 0 ? maxWorkers : Environment.ProcessorCount;
    workers = Math.Max(1, Math.Min(workers, count));
    int chunkSize = (count + workers - 1) / workers; // ceil

    var chunkTrees = buildCallTree ? new ProfileCallTree[workers] : null;
    var tasks = new Task[workers];

    for (int w = 0; w < workers; w++) {
      int worker = w;
      int chunkStart = startIndex + worker * chunkSize;
      int chunkEnd = Math.Min(chunkStart + chunkSize, endIndex);

      if (chunkStart >= chunkEnd) {
        tasks[worker] = Task.CompletedTask;
        continue;
      }

      tasks[worker] = Task.Run(() => {
        var frames = new List<ResolvedFrame>();
        var chunkTree = buildCallTree ? callTreeBuilder_.CreateChunkTree(worker, workers) : null;
        var workerTotal = TimeSpan.Zero;

        for (int i = chunkStart; i < chunkEnd; i++) {
          frames.Clear();

          if (!project(i, frames, out var weight, out int threadId)) {
            continue; // Filtered out (thread/instance/time) — excluded from the total, matching Core.
          }

          // A sample that passed filtering counts toward the total weight (the percentage
          // denominator) even when its stack fully failed to resolve — matching Core's
          // FunctionProfileProcessor, which added every passed-filter sample's weight to the total
          // before skipping unknown frames. Such samples contribute to no function/call-tree node.
          workerTotal += weight;

          if (frames.Count == 0) {
            continue;
          }

          sampleAggregator_.AggregateResolvedStack(weight, frames);

          if (chunkTree != null) {
            callTreeBuilder_.AddResolvedStackToChunk(chunkTree, weight, threadId, frames);
          }
        }

        // Fold this worker's total into the shared total with a single lock (instead of one lock per
        // sample), reproducing the former per-chunk accumulate-then-merge without global contention.
        sampleAggregator_.AddTotalWeight(workerTotal);

        if (chunkTrees != null) {
          chunkTrees[worker] = chunkTree;
        }
      });
    }

    Task.WaitAll(tasks);

    if (chunkTrees != null) {
      var built = new List<ProfileCallTree>(workers);

      foreach (var tree in chunkTrees) {
        if (tree != null) {
          built.Add(tree);
        }
      }

      callTreeBuilder_.MergeChunkTrees(built);
    }

    InvalidateReport();
  }

  /// <summary>
  /// Add hardware performance counter events (PMU/PMC).
  /// Only processed if <see cref="ProfilerOptions.IncludePerformanceCounters"/> is true.
  /// </summary>
  public void AddPerformanceCounterEvents(IEnumerable<IPerformanceCounterEvent> events) {
    counterAggregator_?.AddEvents(events);
    InvalidateReport();
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

    InvalidateReport();
  }

  /// <summary>
  /// Load symbols for all registered images. Downloads PDBs from the symbol server.
  /// </summary>
  public Task LoadSymbolsAsync(CancellationToken ct = default) => LoadSymbolsCoreAsync(null, ct);

  /// <summary>
  /// Load symbols ONLY for the modules the given samples actually touch (leaf + stack frames),
  /// skipping the PDB download/read for the (often thousands of) loaded-but-unsampled images. This is
  /// correct for aggregation: a sample in a module whose symbols aren't loaded still resolves to
  /// (module, RVA, null-function) and counts toward the total weight — only sampled modules need
  /// symbols to attribute self/inclusive time to named functions. Call BEFORE <see cref="AddSamples"/>.
  /// </summary>
  public Task LoadSymbolsForSamplesAsync(IReadOnlyList<IProfileSample> samples, CancellationToken ct = default)
    => LoadSymbolsCoreAsync(ipResolver_.CollectTouchedModules(samples), ct);

  private async Task LoadSymbolsCoreAsync(IReadOnlySet<string>? moduleFilter, CancellationToken ct = default) {
    if (symbolsLoaded_) return;

    foreach (var image in imagesByBase_.Values) {
      string moduleName = image.ImageName;
      if (moduleFilter != null && !moduleFilter.Contains(moduleName)) continue;
      if (loadedModules_.Contains(image.BaseAddress)) continue; // Already attempted by an earlier load.
      if (image.PdbGuid == Guid.Empty) continue;

      loadedModules_.Add(image.BaseAddress); // Mark attempted regardless of outcome so no base is re-probed.
      try {
        string pdbName = !string.IsNullOrEmpty(image.PdbName)
          ? Path.GetFileName(image.PdbName)
          : Path.ChangeExtension(image.ImageName, ".pdb");

        string? pdbPath = await symbolResolver_.FindSymbolFileAsync(pdbName, image.PdbGuid, image.PdbAge, ct);
        if (pdbPath == null) continue;

        pdbPathByModule_[moduleName] = pdbPath;

        // Load debug info and register function list with the IP resolver.
        // Cache the enumerated PDB function list to avoid re-enumeration on subsequent loads.
        var provider = new PdbSymbolProvider();
        var cacheKey = new SymbolFileDescriptor(pdbName, image.PdbGuid, image.PdbAge);
        string cacheDir = !string.IsNullOrEmpty(options_.SymbolCacheDirectory)
          ? Path.Combine(options_.SymbolCacheDirectory, "symcache")
          : SymbolFileCache.DefaultCacheDirectoryPath;

        if (provider.LoadDebugInfo(pdbPath, cacheKey, cacheDir)) {
          debugInfoByModule_[moduleName] = provider;
          var sortedFunctions = provider.GetSortedFunctions();
          if (sortedFunctions.Count > 0) {
            // Pass the provider too so the resolver can fall back to its DIA-backed RVA lookup for
            // PGO-split function chunks the contiguous list omits (matches Core, which credits their
            // inclusive time to the parent function).
            // Attach by base address so two same-named modules (different bases) resolve independently.
            ipResolver_.SetFunctions(image.BaseAddress, sortedFunctions, provider);
          }
          else {
            Log($"PDB loaded but 0 functions: {moduleName} ({pdbPath})");
          }
        }
        else {
          Log($"PDB load FAILED: {moduleName} - {PdbSymbolProvider.DiaRegistrationError}");
          provider.Dispose();
        }
      }
      catch (Exception) {
        // Symbol loading failure for this module — continue with others.
      }
    }

    // Only a full (unfiltered) load has attempted every module; a filtered load must NOT suppress a
    // later full LoadSymbolsAsync that still needs the remaining, previously-skipped modules.
    if (moduleFilter == null) {
      symbolsLoaded_ = true;
    }

    InvalidateReport();
  }

  /// <summary>
  /// Invalidate the memoized report so the next <see cref="GetReport"/> call rebuilds it.
  /// Called whenever new data (images, samples, counters, managed methods, symbols) is added.
  /// </summary>
  private void InvalidateReport() {
    cachedReport_ = null;
  }

  /// <summary>
  /// Build the aggregated profiling report (per-function profiles + call tree + totals) from
  /// added samples. Requires StackFrames on <see cref="IProfileSample"/> for the call tree.
  /// </summary>
  public ProfileReport GetReport(
    string? processName = null,
    int? processId = null) {
    if (cachedReport_ != null) return cachedReport_;

    var functions = sampleAggregator_.Build();
    var totalWeight = sampleAggregator_.TotalWeight;

    // Merge per-instruction performance counter data into the function profiles.
    if (counterAggregator_ != null) {
      foreach (var (id, data) in functions) {
        var counters = counterAggregator_.GetCounters(id);
        if (counters != null) {
          data.InstructionCounters = new Dictionary<long, PerformanceCounterValueSet>(counters);
        }
      }
    }

    // Filter by minimum self percent.
    if (options_.MinSelfPercent > 0 && totalWeight.Ticks > 0) {
      double totalMs = totalWeight.TotalMilliseconds;
      var filtered = new Dictionary<ProfileFunctionId, FunctionProfileData>();

      foreach (var (id, data) in functions) {
        double exclusivePercent = data.ExclusiveWeight.TotalMilliseconds / totalMs * 100;

        if (exclusivePercent >= options_.MinSelfPercent) {
          filtered[id] = data;
        }
      }

      functions = filtered;
    }

    var callTree = callTreeBuilder_.Build();
    cachedReport_ = new ProfileReport(functions, callTree, totalWeight);
    return cachedReport_;
  }

  /// <summary>
  /// Get annotated disassembly for a specific function.
  /// Downloads the binary on-demand, disassembles via Capstone, and annotates with timing data.
  /// </summary>
  public async Task<AnnotatedAssembly?> GetAnnotatedAssemblyAsync(
    ProfileFunctionId functionId,
    FunctionProfileData function,
    CancellationToken ct = default) {
    string moduleName = functionId.ModuleName;
    long functionRva = function.FunctionDebugInfo.RVA;
    int functionSize = (int)function.FunctionDebugInfo.Size;

    // Download binary if not already cached.
    if (!binaryPathByModule_.TryGetValue(moduleName, out var binaryPath)) {
      if (imagesByModule_.TryGetValue(moduleName, out var image)) {
        // Prefer the local on-disk binary when the capturing machine's OS build matches this one
        // (e.g. a dev running the same Insider build the trace came from). This mirrors the old
        // ProfileExplorerCore.BinaryFileLocator.FindExactLocalBinaryFile path and lets us disassemble
        // private OS binaries that aren't published to the public symbol server. Gated on a PE
        // TimeDateStamp match so we never disassemble the wrong build.
        binaryPath = TryFindLocalBinary(image);

        binaryPath ??= await symbolResolver_.FindBinaryFileAsync(
          image.ImageName, image.TimeDateStamp, image.Size, ct);

        if (binaryPath != null) {
          binaryPathByModule_[moduleName] = binaryPath;
        }
      }
    }

    if (binaryPath == null) {
      // Binary not available — try to return hot lines from instruction weights
      // without disassembly. Avoids DIA COM calls (AccessViolationException risk).
      Log($"Binary not found for {moduleName}, falling back to instruction weights ({function.InstructionWeight.Count} offsets, {function.ExclusiveWeight.TotalMilliseconds:F1}ms)");
      try {
        var result = GetHotLinesWithoutBinary(function, functionRva);
        Log($"GetHotLinesWithoutBinary: {(result != null ? $"{result.HotLines.Count} hot lines" : "null")}");
        return result;
      }
      catch (Exception ex) {
        Log($"GetHotLinesWithoutBinary failed for {functionId}: {ex.GetType().Name}: {ex.Message}");
        return null;
      }
    }

    // Get debug info for source line + call-target annotation.
    debugInfoByModule_.TryGetValue(moduleName, out var debugInfoProvider);

    // Disassemble using the mature capstone disassembler (reads architecture and image base
    // from the PE binary itself). Resolves call/jump targets via the debug info provider.
    using var disassembler = Disassembler.CreateForBinary(binaryPath, debugInfoProvider, null);

    if (disassembler == null) return null;

    var instructions = disassembler.DisassembleToList(functionRva, functionSize);

    if (instructions.Count == 0) return null;

    FunctionDebugInfo? funcDebugInfo = debugInfoProvider?.FindFunctionByRVA(functionRva) ?? function.FunctionDebugInfo;

    // Annotate.
    return AssemblyAnnotator.Annotate(
      instructions,
      function.InstructionWeight,
      functionRva,
      debugInfoProvider,
      funcDebugInfo,
      options_.Architecture,
      options_.MinHotLinePercent,
      options_.MaxHotLines);
  }

  public void Dispose() {
    if (ownsSymbolResolver_ && symbolResolver_ is IDisposable disposableResolver) {
      disposableResolver.Dispose();
    }

    foreach (var (_, provider) in debugInfoByModule_) {
      provider.Dispose();
    }

    debugInfoByModule_.Clear();
  }

  // Forward a diagnostic message to the consumer-provided sink (no-op when none is configured).
  private void Log(string message) => options_.LogCallback?.Invoke(message);

  // Resolve the local on-disk binary for a traced image, but only trust it when its PE TimeDateStamp
  // matches the trace's image record (i.e. the local machine is on the same OS build). Returns null
  // when the image carries no path, the file is missing, or the build doesn't match. Mirrors
  // ProfileExplorerCore.BinaryFileLocator.FindExactLocalBinaryFile so DataLayer can disassemble
  // private OS binaries from the local install just like the old MCP engine.
  private string? TryFindLocalBinary(IProfileImage image) {
    foreach (string? candidate in LocalBinaryCandidates(image)) {
      if (string.IsNullOrEmpty(candidate) || !File.Exists(candidate)) {
        continue;
      }

      try {
        var info = PEBinaryInfoProvider.GetBinaryFileInfo(candidate);
        if (info == null) {
          continue;
        }

        // Trust the local file only when it matches the same identity the symbol server keys on —
        // TimeDateStamp + SizeOfImage — plus the module file name, so a stray local PE that merely
        // shares a timestamp (image.ImagePath comes from trace data) is never disassembled by mistake.
        bool stampMatches = info.TimeStamp == image.TimeDateStamp;
        bool sizeMatches = image.Size <= 0 || info.ImageSize == image.Size;
        bool nameMatches = string.IsNullOrEmpty(image.ImageName) ||
          string.Equals(Path.GetFileName(candidate), Path.GetFileName(image.ImageName),
                        StringComparison.OrdinalIgnoreCase);

        if (stampMatches && sizeMatches && nameMatches) {
          Log($"Using local on-disk binary for {image.ImageName}: {candidate} " +
              $"(TimeDateStamp {info.TimeStamp:X8}, SizeOfImage {info.ImageSize:X} match trace)");
          return candidate;
        }

        Log($"Local {image.ImageName} at {candidate} rejected: TimeDateStamp {info.TimeStamp:X8} vs trace " +
            $"{image.TimeDateStamp:X8}, SizeOfImage {info.ImageSize:X} vs trace {image.Size:X}, " +
            $"nameMatch={nameMatches} (wrong build/binary)");
      }
      catch (Exception ex) {
        Log($"Local binary probe failed for {image.ImageName} at {candidate}: {ex.GetType().Name}: {ex.Message}");
      }
    }

    return null;
  }

  // Candidate on-disk locations for a traced image, in priority order: the trace-recorded path first,
  // then the standard OS install locations by file name (covers traces whose image path is an
  // unresolvable NT/device path). Mirrors ProfileExplorerCore.BinaryFileLocator's local search; every
  // candidate is still TimeDateStamp-gated by the caller so we never use the wrong build.
  private static IEnumerable<string?> LocalBinaryCandidates(IProfileImage image) {
    yield return ResolveLocalImagePath(image.ImagePath);

    string? name = string.IsNullOrEmpty(image.ImageName) ? null : Path.GetFileName(image.ImageName);
    if (string.IsNullOrEmpty(name)) {
      yield break;
    }

    string sys = Environment.SystemDirectory; // e.g. C:\Windows\System32
    yield return Path.Combine(sys, name);

    string? win = Path.GetDirectoryName(sys); // e.g. C:\Windows
    if (!string.IsNullOrEmpty(win)) {
      yield return Path.Combine(win, name);
      yield return Path.Combine(win, "SysWOW64", name);
    }
  }

  // Translate NT-kernel image paths (\SystemRoot\..., \??\C:\...) to real filesystem paths so
  // File.Exists can find the local binary. Drive-letter paths pass through unchanged.
  private static string? ResolveLocalImagePath(string? path) {
    if (string.IsNullOrEmpty(path)) {
      return path;
    }

    if (path.StartsWith(@"\SystemRoot", StringComparison.OrdinalIgnoreCase)) {
      string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
      return systemRoot + path.Substring(@"\SystemRoot".Length);
    }

    if (path.StartsWith(@"\??\", StringComparison.Ordinal)) {
      return path.Substring(@"\??\".Length);
    }

    return path;
  }

  // Null-object symbol locator for CreateForResolvedInput (no download path is exercised).
  private sealed class NullSymbolFileLocator : ISymbolFileLocator {
    public static readonly NullSymbolFileLocator Instance = new();

    public Task<string?> FindSymbolFileAsync(string pdbName, Guid guid, int age, CancellationToken ct = default) =>
      Task.FromResult<string?>(null);

    public Task<string?> FindBinaryFileAsync(string binaryName, int timeDateStamp, long imageSize,
                                             CancellationToken ct = default) =>
      Task.FromResult<string?>(null);
  }

  /// <summary>
  /// Generate hot lines from instruction weights only, without requiring
  /// the binary for Capstone disassembly. Avoids DIA COM calls to prevent
  /// AccessViolationException from cross-thread COM access.
  /// Uses the function's debug-info source file name if available.
  /// </summary>
  private AnnotatedAssembly? GetHotLinesWithoutBinary(FunctionProfileData function, long functionRva) {
    if (function.InstructionWeight.Count == 0) return null;

    var totalWeight = function.InstructionWeight.Values.Aggregate(TimeSpan.Zero, (sum, w) => sum + w);
    var hotLines = new List<HotLine>();
    var lines = new List<AssemblyLine>();
    var sb = new System.Text.StringBuilder();

    // Use source file from the function's debug info if available.
    string? sourceFile = function.FunctionDebugInfo.SourceFileName;

    foreach (var (offset, weight) in function.InstructionWeight.OrderByDescending(kv => kv.Value)) {
      double percent = totalWeight > TimeSpan.Zero
        ? weight.TotalMilliseconds / totalWeight.TotalMilliseconds * 100 : 0;

      if (percent < options_.MinHotLinePercent) continue;

      string text = $"[offset +0x{offset:X}]";

      var line = new AssemblyLine(
        address: functionRva + offset,
        rva: functionRva + offset,
        instructionText: text,
        weight: weight,
        percent: percent,
        sourceFile: sourceFile,
        sourceLine: null);
      lines.Add(line);

      sb.AppendLine($"{functionRva + offset:X}:    {text}    [Time(%): {percent:F2}%, Time: {weight.TotalMilliseconds:F2} ms]");

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
