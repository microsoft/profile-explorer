// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Compilers.ASM;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Profile.Data;

public sealed class ProfileModuleBuilder : IDisposable {
#if DEBUG
  private static volatile int FuncQueries;
  private static volatile int FuncFoundByAddress;
  private static volatile int FuncFoundByFuncAddress;
  private static volatile int FuncFoundByFuncAddressLocked;
  private static volatile int FuncCreated;
#endif
  private ICompilerInfoProvider compilerInfo_;
  private IBinaryFileFinder binaryFileFinder_;
  private IDebugFileFinder debugFileFinder_;
  private IDebugInfoProviderFactory debugInfoProviderFactory_;
  private ICompilerIRInfo compilerIrInfo_;
  private INameProvider nameProvider_;
  private BinaryFileDescriptor binaryInfo_;
  private ConcurrentDictionary<long, (IRTextFunction, FunctionDebugInfo)> functionMap_;
  private ConcurrentDictionary<long, bool> loggedFuncAddresses_ = new();
  private ProfileDataReport report_;
  private ReaderWriterLockSlim lock_;
  private SemaphoreSlim binaryLoadLock_;
  private SymbolFileSourceSettings symbolSettings_;
  private volatile bool disposed_;

  public ProfileModuleBuilder(ProfileDataReport report, ICompilerInfoProvider compilerInfoProvider) {
    report_ = report;
    compilerInfo_ = compilerInfoProvider;
    binaryFileFinder_ = compilerInfoProvider.BinaryFileFinder;
    debugFileFinder_ = compilerInfoProvider.DebugFileFinder;
    debugInfoProviderFactory_ = compilerInfoProvider.DebugInfoProviderFactory;
    compilerIrInfo_ = compilerInfoProvider.IR;
    nameProvider_ = compilerInfoProvider.NameProvider;
    functionMap_ = new ConcurrentDictionary<long, (IRTextFunction, FunctionDebugInfo)>();
    lock_ = new ReaderWriterLockSlim();
    binaryLoadLock_ = new SemaphoreSlim(1, 1);
  }

  public IRTextSummary Summary { get; set; }
  public ILoadedDocument ModuleDocument { get; set; }
  public IDebugInfoProvider DebugInfo { get; set; }
  public bool HasDebugInfo { get; set; }
  public bool Initialized { get; set; }
  public bool IsManaged { get; set; }

  public async Task<bool> Initialize(BinaryFileDescriptor binaryInfo,
                                     SymbolFileSourceSettings symbolSettings,
                                     IDebugInfoProvider debugInfo,
                                     bool skipBinaryDownload = false) {
    if (Initialized) {
      return true;
    }

    binaryInfo_ = binaryInfo;
    symbolSettings_ = symbolSettings;
    string imageName = binaryInfo.ImageName;

    DiagnosticLogger.LogInfo($"[ModuleInit] Starting initialization for module: {imageName} (skipBinaryDownload={skipBinaryDownload})");
    DiagnosticLogger.LogInfo($"[ModuleInit] Binary info - Name: {binaryInfo.ImageName}, Path: {binaryInfo.ImagePath}, Architecture: {binaryInfo.Architecture}");

#if DEBUG
    Trace.WriteLine($"ModuleInfo init {imageName}");
#endif

    // LAZY BINARY LOADING: Skip binary download during trace loading.
    // Binaries are only needed for disassembly view, not for function name resolution.
    // They will be downloaded on-demand when user views assembly for a function.
    BinaryFileSearchResult binFile = null;
    if (skipBinaryDownload) {
      DiagnosticLogger.LogInfo($"[ModuleInit] Skipping binary download for {imageName} (lazy loading enabled)");
    }
    else if (symbolSettings.SourceServerEnabled) {
      binFile = await FindBinaryFilePath(symbolSettings).ConfigureAwait(false);
    }
    else {
      DiagnosticLogger.LogInfo($"[ModuleInit] Skipping binary lookup for {imageName} - symbol server disabled");
    }

    if (binFile == null || !binFile.Found) {
      if (skipBinaryDownload) {
        // Lazy loading: Binary will be downloaded on-demand.
        // Report as LazyLoadPending, not NotFound.
        DiagnosticLogger.LogInfo($"[ModuleInit] Binary lazy load pending for {imageName} - will download on-demand");
        report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.LazyLoadPending);
      }
      else if (binFile != null) {
        DiagnosticLogger.LogWarning($"[ModuleInit] Could not find local path for image {imageName}. Binary file missing.");
        Trace.TraceWarning($"Could not find local path for image {imageName}");
        report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.NotFound);
      }
      else {
        report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.NotFound);
      }
      CreateDummyDocument(binaryInfo);
      return true; // Try to continue just with debug info.
    }

    DiagnosticLogger.LogInfo($"[ModuleInit] Found binary file for {imageName}: {binFile.FilePath}");

    // Create a DisassemblerSectionLoader and LoadedDocument directly instead of calling through session
    bool isManagedImage = binFile.BinaryFile?.IsManagedImage ?? false;
    var loader = new DisassemblerSectionLoader(binFile.FilePath, compilerInfo_, debugInfo, false, isManagedImage);
    var loadedDoc = await CreateLoadedDocument(binFile.FilePath, binaryInfo.ImageName, loader).ConfigureAwait(false);

    if (loadedDoc == null) {
      DiagnosticLogger.LogError($"[ModuleInit] Failed to load document for image {imageName}");
      Trace.TraceWarning($"Failed to load document for image {imageName}");
      report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.Failed);
      CreateDummyDocument(binaryInfo);
      return false;
    }

    DiagnosticLogger.LogInfo($"[ModuleInit] Successfully loaded document for image {imageName}");

    loadedDoc.BinaryFile = BinaryFileSearchResult.Success(binFile.FilePath);
    loadedDoc.DebugInfo = debugInfo;

#if DEBUG
    Trace.TraceWarning($"  Loaded document for image {imageName}");
#endif
    report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.Loaded);

    ModuleDocument = loadedDoc;
    Summary = loadedDoc.Summary;

    // .Net debug info is passed in by the client.
    IsManaged = binFile.BinaryFile != null && binFile.BinaryFile.IsManagedImage;

    if (IsManaged && debugInfo != null) {
      DiagnosticLogger.LogInfo($"[ModuleInit] Module {imageName} has managed debug info");
      Trace.TraceInformation($"  Has managed debug {imageName}");
      DebugInfo = debugInfo;
      HasDebugInfo = true;
      loadedDoc.DebugInfo = debugInfo;
    } else if (IsManaged) {
      DiagnosticLogger.LogWarning($"[ModuleInit] Module {imageName} is managed but no debug info provided");
    } else {
      DiagnosticLogger.LogInfo($"[ModuleInit] Module {imageName} is native (not managed)");
    }

#if DEBUG
    Trace.TraceInformation($"Initialized image {imageName}");
#endif

    DiagnosticLogger.LogInfo($"[ModuleInit] Module initialization completed for {imageName}. HasDebugInfo={HasDebugInfo}, IsManaged={IsManaged}");
    Initialized = true;
    return true;
  }

  /// <summary>
  /// Loads the binary file on-demand for disassembly view.
  /// Call this when the user wants to view assembly for a function.
  /// </summary>
  public async Task<bool> EnsureBinaryLoaded() {
    // Check if disposed before attempting any operations
    if (disposed_) {
      return false;
    }

    // Already have a binary loaded - fast path check without lock
    if (ModuleDocument?.BinaryFile?.Found == true) {
      return true;
    }

    // Acquire the lock to ensure only one thread loads the binary
    // Check disposed again to avoid race with Dispose()
    if (disposed_) {
      return false;
    }

    try {
      await binaryLoadLock_.WaitAsync().ConfigureAwait(false);
    }
    catch (ObjectDisposedException) {
      // Semaphore was disposed between our check and WaitAsync
      return false;
    }
    try {
      // Double-check after acquiring lock - another thread may have loaded it
      if (ModuleDocument?.BinaryFile?.Found == true) {
        return true;
      }

      if (binaryInfo_ == null || symbolSettings_ == null) {
        return false;
      }

      string imageName = binaryInfo_.ImageName;
      DiagnosticLogger.LogInfo($"[LazyBinaryLoad] Loading binary on-demand for {imageName}");

      var binFile = await FindBinaryFilePath(symbolSettings_).ConfigureAwait(false);
      if (binFile == null || !binFile.Found) {
        DiagnosticLogger.LogWarning($"[LazyBinaryLoad] Could not find binary for {imageName}");
        return false;
      }

      DiagnosticLogger.LogInfo($"[LazyBinaryLoad] Found binary for {imageName}: {binFile.FilePath}");
      DiagnosticLogger.LogInfo($"[LazyBinaryLoad] DebugInfo available: {DebugInfo != null}, compilerInfo available: {compilerInfo_ != null}");

      if (DebugInfo == null) {
        DiagnosticLogger.LogError($"[LazyBinaryLoad] DebugInfo is null for {imageName} - disassembly will fail!");
      }

      // Create the disassembler loader with the binary.
      // Pass preloadFunctions=false since we'll register functions manually.
      bool isManagedImage = binFile.BinaryFile?.IsManagedImage ?? false;
      var loader = new DisassemblerSectionLoader(binFile.FilePath, compilerInfo_, DebugInfo, false, isManagedImage);

      // Initialize the loader's document with our existing summary.
      // This is important because functions have already been added to Summary
      // during profile loading and we need to preserve those references.
      await Task.Run(async () => {
        await loader.LoadDocument(null).ConfigureAwait(false);
      }).ConfigureAwait(false);

      // Verify the disassembler was initialized
      bool disassemblerReady = loader.IsDisassemblerInitialized;
      DiagnosticLogger.LogInfo($"[LazyBinaryLoad] Disassembler initialized: {disassemblerReady}");
      if (!disassemblerReady) {
        DiagnosticLogger.LogError($"[LazyBinaryLoad] Disassembler failed to initialize for {imageName}!");
      }

      // Re-register all existing functions with the new loader so it knows about them.
      // The functions were already created via GetOrCreateFunction during profile loading.
      int registeredCount = 0;
      foreach (var kvp in functionMap_) {
        var (func, debugInfo) = kvp.Value;
        loader.RegisterFunction(func, debugInfo);
        registeredCount++;
      }
      DiagnosticLogger.LogInfo($"[LazyBinaryLoad] Registered {registeredCount} functions with loader for {imageName}");

      // Update the existing document IN PLACE so the session state reference remains valid.
      // This is critical - creating a new document would break the reference that
      // MainWindowSession.LoadAndParseSection() holds.
      ModuleDocument.BinaryFile = BinaryFileSearchResult.Success(binFile.FilePath);
      ModuleDocument.DebugInfo = DebugInfo;

      // Dispose the old dummy loader and replace with the real one
      ModuleDocument.Loader?.Dispose();
      ModuleDocument.Loader = loader;

      // Update the module load state in the report
      report_.AddModuleInfo(binaryInfo_, binFile, ModuleLoadState.Loaded);
      IsManaged = binFile.BinaryFile != null && binFile.BinaryFile.IsManagedImage;

      DiagnosticLogger.LogInfo($"[LazyBinaryLoad] Successfully loaded binary for {imageName}");
      return true;
    }
    finally {
      binaryLoadLock_.Release();
    }
  }

  public async Task<bool> InitializeDebugInfo(DebugFileSearchResult debugInfoFile) {
    string imageName = binaryInfo_?.ImageName ?? "Unknown";
    DiagnosticLogger.LogInfo($"[DebugInfoInit] Initializing debug info for module {imageName}");
    
    if (DebugInfo != null) {
      DiagnosticLogger.LogInfo($"[DebugInfoInit] Debug info already loaded for module {imageName}");
      return HasDebugInfo;
    }

    ModuleDocument.DebugInfoFile = debugInfoFile;

    if (ModuleDocument.DebugInfoFile == null ||
        !ModuleDocument.DebugInfoFile.Found) {
      DiagnosticLogger.LogWarning($"[DebugInfoInit] Debug info file not found for module {imageName}. DebugInfoFile={ModuleDocument.DebugInfoFile?.SymbolFile?.FileName ?? "null"}");
      report_.AddDebugInfo(binaryInfo_, ModuleDocument.DebugInfoFile);
      return false;
    }

    DiagnosticLogger.LogInfo($"[DebugInfoInit] Found debug info file for module {imageName}: {ModuleDocument.DebugInfoFile.SymbolFile.FileName}");
    DiagnosticLogger.LogInfo($"[DebugInfoInit] Debug file path: {ModuleDocument.DebugInfoFile.FilePath}");

    DebugInfo = debugInfoProviderFactory_.CreateDebugInfoProvider(ModuleDocument.DebugInfoFile);
    HasDebugInfo = DebugInfo != null;

    if (HasDebugInfo) {
      // Also set on the document so GetOrCreateDebugInfoProvider can find it.
      ModuleDocument.DebugInfo = DebugInfo;
      DiagnosticLogger.LogInfo($"[DebugInfoInit] Successfully created debug info provider for module {imageName}");

      if (ModuleDocument.Loader is DisassemblerSectionLoader disassemblerSectionLoader) {
        disassemblerSectionLoader.Initialize(DebugInfo);
        DiagnosticLogger.LogInfo($"[DebugInfoInit] Initialized disassembler with debug info for module {imageName}");
      }
    }
    else {
      DiagnosticLogger.LogError($"[DebugInfoInit] Failed to create debug info provider for module {imageName}");
      Trace.TraceWarning($"Failed to load debug info: {ModuleDocument.DebugInfoFile}");
    }

    report_.AddDebugInfo(binaryInfo_, ModuleDocument.DebugInfoFile);
    return HasDebugInfo;
  }

  public async Task<BinaryFileSearchResult> FindBinaryFilePath(SymbolFileSourceSettings settings) {
    // Use the symbol server to locate the image,
    // this will also attempt to download it if not found locally.
    return await binaryFileFinder_.FindBinaryFileAsync(binaryInfo_, settings).ConfigureAwait(false);
  }

  public (IRTextFunction Function, FunctionDebugInfo DebugInfo)
    GetOrCreateFunction(long funcAddress) {
#if DEBUG
    Interlocked.Increment(ref FuncQueries);
#endif

    bool shouldLog = loggedFuncAddresses_.TryAdd(funcAddress, true); // Returns true if newly added
    string moduleName = binaryInfo_?.ImageName ?? "Unknown";

    // Try to get it form the concurrent dictionary first.
    if (functionMap_.TryGetValue(funcAddress, out var pair)) {
#if DEBUG
      Interlocked.Increment(ref FuncFoundByAddress);
#endif
      if (shouldLog) {
        DiagnosticLogger.LogInfo($"[FunctionResolution] Module: {moduleName}, Address: 0x{funcAddress:X}, Function: {pair.Item1.Name} (found in cache)");
      }
      return pair;
    }

    // Find function outside lock to reduce contention.
    FunctionDebugInfo debugInfo = null;
    long funcStartAddress = funcAddress;

    if (HasDebugInfo) {
      // Search for the function at this RVA.
      debugInfo = DebugInfo.FindFunctionByRVA(funcAddress);
      
      if (debugInfo != null) {
        if (shouldLog) {
          DiagnosticLogger.LogInfo($"[FunctionResolution] Module: {moduleName}, Address: 0x{funcAddress:X}, Function: {debugInfo.Name} (resolved via debug info)");
        }
      } else if (shouldLog) {
        DiagnosticLogger.LogWarning($"[FunctionResolution] Module: {moduleName}, Address: 0x{funcAddress:X}, Function: NOT_RESOLVED (debug info available but no symbol found)");
      }
    } else if (shouldLog) {
      DiagnosticLogger.LogWarning($"[FunctionResolution] Module: {moduleName}, Address: 0x{funcAddress:X}, Function: NOT_RESOLVED (no debug info available)");
    }

    if (debugInfo == null) {
      // Create a dummy debug entry for the missing function.
      string placeholderName = $"{funcAddress:X}";
      debugInfo = new FunctionDebugInfo(placeholderName, funcAddress, 0);
      if (shouldLog) {
        DiagnosticLogger.LogWarning($"[FunctionResolution] Module: {moduleName}, Address: 0x{funcAddress:X}, Function: {placeholderName} (created placeholder)");
      }
    }
    else {
      // Use the function start address from now on, this ensures
      // that a single instance of it is created.
      funcStartAddress = debugInfo.StartRVA;
    }

    // Check again under the write lock.
    if (functionMap_.TryGetValue(funcStartAddress, out pair)) {
#if DEBUG
      Interlocked.Increment(ref FuncFoundByFuncAddress);
#endif
      functionMap_.TryAdd(funcAddress, pair);
      return pair;
    }

    // Acquire write lock to create an entry for the function.
    lock_.EnterWriteLock();

    // Check again under the write lock.
    if (functionMap_.TryGetValue(funcStartAddress, out pair)) {
#if DEBUG
      Interlocked.Increment(ref FuncFoundByFuncAddressLocked);
#endif
      lock_.ExitWriteLock();
      return pair;
    }

    // Add the new function to the module and disassembler.
    var func = ModuleDocument.AddDummyFunction(debugInfo.Name);

    if (ModuleDocument.Loader is DisassemblerSectionLoader disassemblerSectionLoader) {
      disassemblerSectionLoader.RegisterFunction(func, debugInfo);
    }

#if DEBUG
    Interlocked.Increment(ref FuncCreated);
#endif
    // Cache RVA -> function mapping.
    pair = (func, debugInfo);

    if (funcStartAddress != funcAddress) {
      functionMap_.TryAdd(funcStartAddress, pair);
    }

    lock_.ExitWriteLock();

    functionMap_.TryAdd(funcAddress, pair);
    return pair;
  }

#if DEBUG
  public static void PrintStatistics() {
    Trace.WriteLine($"FuncQueries: {FuncQueries}");
    Trace.WriteLine($"FuncFoundByAddress: {FuncFoundByAddress}");
    Trace.WriteLine($"FuncFoundByFuncAddress: {FuncFoundByFuncAddress}");
    Trace.WriteLine($"FuncFoundByFuncAddressLocked: {FuncFoundByFuncAddressLocked}");
    Trace.WriteLine($"FuncCreated: {FuncCreated}");
  }
#endif

  private void CreateDummyDocument(BinaryFileDescriptor binaryInfo) {
    // Create a dummy document to represent the module,
    // AddPlaceholderFunction will populate it.
    ModuleDocument = LoadedDocument.CreateDummyDocument(binaryInfo.ImageName);
    Summary = ModuleDocument.Summary;

    // Set up lazy binary loading callback - this will be called when
    // user tries to view assembly/graph and binary hasn't been downloaded yet.
    ModuleDocument.EnsureBinaryLoaded = EnsureBinaryLoaded;
  }

  private async Task<ILoadedDocument> CreateLoadedDocument(string filePath, string modulePath, IRTextSectionLoader loader) {
    try {
      var result = await Task.Run(async () => {
        var result = new LoadedDocument(filePath, modulePath, Guid.NewGuid());
        result.Loader = loader;
        result.Summary = await result.Loader.LoadDocument(null);
        return result;
      });

      return result;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load document {filePath}: {ex}");
      return null;
    }
  }

  public void Dispose() {
    disposed_ = true;
    lock_?.Dispose();
    binaryLoadLock_?.Dispose();
  }
}