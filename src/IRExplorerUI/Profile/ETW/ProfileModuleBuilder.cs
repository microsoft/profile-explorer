// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerUI.Compilers;

namespace IRExplorerUI.Profile;

public sealed class ProfileModuleBuilder {
  private ISession session_;
  private BinaryFileDescriptor binaryInfo_;
  private Dictionary<long, (IRTextFunction, FunctionDebugInfo)> functionMap_;
  private ProfileDataReport report_;
  private ReaderWriterLockSlim lock_;
  private SymbolFileSourceSettings symbolSettings_;

  public ProfileModuleBuilder(ProfileDataReport report, ISession session) {
    report_ = report;
    session_ = session;
    functionMap_ = new Dictionary<long, (IRTextFunction, FunctionDebugInfo)>();
    lock_ = new ReaderWriterLockSlim();
  }

  public IRTextSummary Summary { get; set; }
  public LoadedDocument ModuleDocument { get; set; }
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
    Trace.WriteLine($"ModuleInfo init {imageName}");

    var binFile = await FindBinaryFilePath(symbolSettings).ConfigureAwait(false);

    if (binFile == null || !binFile.Found) {
      Trace.TraceWarning($"  Could not find local path for image {imageName}");
      report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.NotFound);
      CreateDummyDocument(binaryInfo);
      return true; // Try to continue just with debug info.
    }

    var loadedDoc = await session_.LoadProfileBinaryDocument(binFile.FilePath, binaryInfo.ImageName, debugInfo).
      ConfigureAwait(false);

    if (loadedDoc == null) {
      Trace.TraceWarning($"  Failed to load document for image {imageName}");
      report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.Failed);
      CreateDummyDocument(binaryInfo);
      return false;
    }

    Trace.TraceWarning($"  Loaded document for image {imageName}");
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

    Trace.TraceInformation($"Initialized image {imageName}");
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

    DebugInfo = session_.CompilerInfo.CreateDebugInfoProvider(ModuleDocument.DebugInfoFile);
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
    return await session_.CompilerInfo.FindBinaryFileAsync(binaryInfo_, settings).ConfigureAwait(false);
  }

  public (IRTextFunction Function, FunctionDebugInfo DebugInfo)
    GetOrCreateFunction(long funcAddress) {
    try {
      lock_.EnterUpgradeableReadLock();

      if (functionMap_.TryGetValue(funcAddress, out var pair)) {
        return pair;
      }

      try {
        lock_.EnterWriteLock();

        if (functionMap_.TryGetValue(funcAddress, out pair)) {
          return pair;
        }

        FunctionDebugInfo debugInfo = null;

        if (HasDebugInfo) {
          debugInfo = DebugInfo.FindFunctionByRVA(funcAddress);
        }

        if (debugInfo == null) {
          string placeholderName = $"{funcAddress:X}";
          debugInfo = new FunctionDebugInfo(placeholderName, funcAddress, 0);
        }

        var func = ModuleDocument.AddDummyFunction(debugInfo.Name);

        if (ModuleDocument.Loader is DisassemblerSectionLoader disassemblerSectionLoader) {
          disassemblerSectionLoader.RegisterFunction(func, debugInfo);
        }

        pair = (func, debugInfo);
        functionMap_[funcAddress] = pair;
        return pair;
      }
      finally {
        lock_.ExitWriteLock();
      }
    }
    finally {
        lock_.ExitUpgradeableReadLock();
    }
  }

  private void CreateDummyDocument(BinaryFileDescriptor binaryInfo) {
    // Create a dummy document to represent the module,
    // AddPlaceholderFunction will populate it.
    ModuleDocument = LoadedDocument.CreateDummyDocument(binaryInfo.ImageName);
    Summary = ModuleDocument.Summary;
  }
}