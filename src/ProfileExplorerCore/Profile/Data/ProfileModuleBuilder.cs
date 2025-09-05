﻿// Copyright (c) Microsoft Corporation.
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

namespace ProfileExplorer.Core.Profile.Data;

public sealed class ProfileModuleBuilder {
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
  private ProfileDataReport report_;
  private ReaderWriterLockSlim lock_;
  private SymbolFileSourceSettings symbolSettings_;

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
  }

  public IRTextSummary Summary { get; set; }
  public ILoadedDocument ModuleDocument { get; set; }
  public IDebugInfoProvider DebugInfo { get; set; }
  public bool HasDebugInfo { get; set; }
  public bool Initialized { get; set; }
  public bool IsManaged { get; set; }

  public async Task<bool> Initialize(BinaryFileDescriptor binaryInfo,
                                     SymbolFileSourceSettings symbolSettings,
                                     IDebugInfoProvider debugInfo) {
    if (Initialized) {
      return true;
    }

    binaryInfo_ = binaryInfo;
    symbolSettings_ = symbolSettings;
    string imageName = binaryInfo.ImageName;
#if DEBUG
    Trace.WriteLine($"ModuleInfo init {imageName}");
#endif

    var binFile = await FindBinaryFilePath(symbolSettings).ConfigureAwait(false);

    if (binFile == null || !binFile.Found) {
      Trace.TraceWarning($"Could not find local path for image {imageName}");
      report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.NotFound);
      CreateDummyDocument(binaryInfo);
      return true; // Try to continue just with debug info.
    }

    // Create a DisassemblerSectionLoader and LoadedDocument directly instead of calling through session
    var loader = new DisassemblerSectionLoader(binFile.FilePath, compilerInfo_, debugInfo, false);
    var loadedDoc = await CreateLoadedDocument(binFile.FilePath, binaryInfo.ImageName, loader).ConfigureAwait(false);

    if (loadedDoc == null) {
      Trace.TraceWarning($"Failed to load document for image {imageName}");
      report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.Failed);
      CreateDummyDocument(binaryInfo);
      return false;
    }

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
      Trace.TraceInformation($"  Has managed debug {imageName}");
      DebugInfo = debugInfo;
      HasDebugInfo = true;
      loadedDoc.DebugInfo = debugInfo;
    }

#if DEBUG
    Trace.TraceInformation($"Initialized image {imageName}");
#endif

    Initialized = true;
    return true;
  }

  public async Task<bool> InitializeDebugInfo(DebugFileSearchResult debugInfoFile) {
    if (DebugInfo != null) {
      return HasDebugInfo;
    }

    ModuleDocument.DebugInfoFile = debugInfoFile;

    if (ModuleDocument.DebugInfoFile == null ||
        !ModuleDocument.DebugInfoFile.Found) {
      report_.AddDebugInfo(binaryInfo_, ModuleDocument.DebugInfoFile);
      return false;
    }

    DebugInfo = debugInfoProviderFactory_.CreateDebugInfoProvider(ModuleDocument.DebugInfoFile);
    HasDebugInfo = DebugInfo != null;

    if (HasDebugInfo) {
      if (ModuleDocument.Loader is DisassemblerSectionLoader disassemblerSectionLoader) {
        disassemblerSectionLoader.Initialize(DebugInfo);
      }
    }
    else {
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

    // Try to get it form the concurrent dictionary first.
    if (functionMap_.TryGetValue(funcAddress, out var pair)) {
#if DEBUG
      Interlocked.Increment(ref FuncFoundByAddress);
#endif
      return pair;
    }

    // Find function outside lock to reduce contention.
    FunctionDebugInfo debugInfo = null;
    long funcStartAddress = funcAddress;

    if (HasDebugInfo) {
      // Search for the function at this RVA.
      debugInfo = DebugInfo.FindFunctionByRVA(funcAddress);
    }

    if (debugInfo == null) {
      // Create a dummy debug entry for the missing function.
      string placeholderName = $"{funcAddress:X}";
      debugInfo = new FunctionDebugInfo(placeholderName, funcAddress, 0);
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
}