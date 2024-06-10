﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.FileFormat;
using IRExplorerUI.Compilers;
using ProtoBuf;

namespace IRExplorerUI.Profile;

public sealed class ProfileModuleBuilder {
  [ProtoContract]
  private class SymbolFileCache {
    [ProtoMember(1)]
    public SymbolFileDescriptor SymbolFile { get; set; }
    [ProtoMember(2)]
    public List<FunctionDebugInfo> FunctionList { get; set; }
    public static string DefaultCacheDirectoryPath => Path.Combine(Path.GetTempPath(), "irexplorer", "symcache");

    public static bool Serialize(SymbolFileCache symCache, string directoryPath) {
      try {
        var outStream = new MemoryStream();
        Serializer.Serialize(outStream, symCache);
        outStream.Position = 0;

        if (!Directory.Exists(directoryPath)) {
          Directory.CreateDirectory(directoryPath);
        }

        var cacheFile = MakeCacheFilePath(symCache.SymbolFile);
        var cachePath = Path.Combine(directoryPath, cacheFile);

        //? TODO: Convert everything to RunSync or add the support in the FileArchive
        return FileArchive.CreateFromStreamAsync(outStream, cacheFile, cachePath).ConfigureAwait(false).
          GetAwaiter().GetResult();
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to save symbol file cache: {ex.Message}");
        return false;
      }
    }

    public static SymbolFileCache Deserialize(SymbolFileDescriptor symbolFile, string directoryPath) {
      try {
        var cacheFile = MakeCacheFilePath(symbolFile);
        var cachePath = Path.Combine(directoryPath, cacheFile);

        if (!File.Exists(cachePath)) {
          return null;
        }

        using var archive = FileArchive.LoadAsync(cachePath).ConfigureAwait(false).GetAwaiter().GetResult();

        if (archive != null) {
          using var stream = Utils.RunSync<MemoryStream>(() => archive.ExtractFileToMemoryAsync(cacheFile));

          if (stream != null) {
            var symCache = Serializer.Deserialize<SymbolFileCache>(stream);

            if (symCache.SymbolFile.Equals(symbolFile)) {
              return symCache;
            }

            Trace.WriteLine($"Symbol file mismatch in deserialized symbol file cache");
            Trace.WriteLine($"  actual: {symCache.SymbolFile} vs expected {symbolFile}");
          }
        }
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to load symbol file cache: {ex.Message}");
      }

      return null;
    }

    private static string MakeCacheFilePath(SymbolFileDescriptor symbolFile) {
      return $"{Utils.TryGetFileName(symbolFile.FileName)}-{symbolFile.Id}-{symbolFile.Age}.cache";
    }
  }

  private ISession session_;
  private BinaryFileDescriptor binaryInfo_;
  private ConcurrentDictionary<long, (IRTextFunction, FunctionDebugInfo)> functionMap_;
  private ProfileDataReport report_;
  private ReaderWriterLockSlim lock_;
  private SymbolFileSourceSettings symbolSettings_;
  private List<FunctionDebugInfo> functionListCache_;

  public ProfileModuleBuilder(ProfileDataReport report, ISession session) {
    report_ = report;
    session_ = session;
    functionMap_ = new ConcurrentDictionary<long, (IRTextFunction, FunctionDebugInfo)>();
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

    var loadedDoc = await session_.LoadProfileBinaryDocument(binFile.FilePath, binaryInfo.ImageName, debugInfo).
      ConfigureAwait(false);

    if (loadedDoc == null) {
      Trace.TraceWarning($"Failed to load document for image {imageName}");
      report_.AddModuleInfo(binaryInfo, binFile, ModuleLoadState.Failed);
      CreateDummyDocument(binaryInfo);
      return false;
    }

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

    DebugInfo = session_.CompilerInfo.CreateDebugInfoProvider(ModuleDocument.DebugInfoFile);
    HasDebugInfo = DebugInfo != null;

#if true
    if (HasDebugInfo) {
      var sw = Stopwatch.StartNew();
      var cacheDirPath = Path.Combine(Path.GetTempPath(), "irexplorer", "symcache");
      var symCache = SymbolFileCache.Deserialize(debugInfoFile.SymbolFile, cacheDirPath);

      if (symCache != null) {
        functionListCache_ = symCache.FunctionList;
        Trace.WriteLine($"PDB cache load for {debugInfoFile.SymbolFile.FileName}: {sw.Elapsed}");
      }
      else {
        functionListCache_ = DebugInfo.GetSortedFunctions();
        symCache = new SymbolFileCache() {
          SymbolFile = debugInfoFile.SymbolFile,
          FunctionList = functionListCache_
        };
        SymbolFileCache.Serialize(symCache, cacheDirPath);
        Trace.WriteLine($"PDB cache create for {debugInfoFile.SymbolFile.FileName}: {sw.Elapsed}");
      }
    }
#endif

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
    // Try to get it form the concurrent dictionary.
    //? TODO: Ideally a range tree would be used to capture all
    //? the RVAs of a function, which would avoid multiple queries
    //? of the debug info under the write lock.
    if (functionMap_.TryGetValue(funcAddress, out var pair)) {
      return pair;
    }

    try {
      lock_.EnterWriteLock();

      // Check again under the write lock.
      if (functionMap_.TryGetValue(funcAddress, out pair)) {
        return pair;
      }

      FunctionDebugInfo debugInfo = null;

      if (HasDebugInfo) {
        if (functionListCache_ != null) {
          debugInfo = FunctionDebugInfo.BinarySearch(functionListCache_, funcAddress);
        }
        else {
          // Search for the function at this RVA.
          debugInfo = DebugInfo.FindFunctionByRVA(funcAddress);
        }
      }

      if (debugInfo == null) {
        // Create a dummy debug entry for the missing function.
        string placeholderName = $"{funcAddress:X}";
        debugInfo = new FunctionDebugInfo(placeholderName, funcAddress, 0);
      }

      // Add the new function to the module and disassembler.
      var func = ModuleDocument.AddDummyFunction(debugInfo.Name);

      if (ModuleDocument.Loader is DisassemblerSectionLoader disassemblerSectionLoader) {
        disassemblerSectionLoader.RegisterFunction(func, debugInfo);
      }

      // Cache RVA -> function mapping.
      pair = (func, debugInfo);
      functionMap_[funcAddress] = pair;
      return pair;
    }
    finally {
      lock_.ExitWriteLock();
    }
  }

  private void CreateDummyDocument(BinaryFileDescriptor binaryInfo) {
    // Create a dummy document to represent the module,
    // AddPlaceholderFunction will populate it.
    ModuleDocument = LoadedDocument.CreateDummyDocument(binaryInfo.ImageName);
    Summary = ModuleDocument.Summary;
  }
}