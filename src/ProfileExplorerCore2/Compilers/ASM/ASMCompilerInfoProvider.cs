// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using ProfileExplorerCore2.Binary;
using ProfileExplorerCore2.Compilers.Architecture;
using ProfileExplorerCore2.Diff;
using ProfileExplorerCore2.IR;
using ProfileExplorerCore2.Providers;
using ProfileExplorerCore2.Session;
using ProfileExplorerCore2.Settings;
using ProfileExplorerCore2.Utilities;

namespace ProfileExplorerCore2.Compilers.ASM;

public class ASMCompilerInfoProvider : ICompilerInfoProvider {
  private static Dictionary<DebugFileSearchResult, IDebugInfoProvider> loadedDebugInfo_ = new();
  private readonly ISession session_;
  private readonly ASMNameProvider names_ = new();
  private readonly ASMCompilerIRInfo ir_;

  public ASMCompilerInfoProvider(IRMode mode, ISession session) {
    session_ = session;
    ir_ = new ASMCompilerIRInfo(mode);
  }

  public virtual string CompilerIRName => "ASM";
  public virtual string CompilerDisplayName => "ASM " + ir_.Mode;
  public virtual string OpenFileFilter =>
    "ASM, Binary, Trace Files|*.asm;*.txt;*.log;*.exe;*.dll;*.sys;*.etl|All Files|*.*";
  public virtual string OpenDebugFileFilter => "Debug Files|*.pdb|All Files|*.*";
  public virtual string DefaultSyntaxHighlightingFile => (ir_.Mode == IRMode.ARM64 ? "ARM64" : "x86") + " ASM IR";
  public ISession Session => session_;
  public ICompilerIRInfo IR => ir_;
  public INameProvider NameProvider => names_;

  public virtual Task HandleLoadedDocument(ILoadedDocument document, string modulePath) {
    return Task.CompletedTask;
  }

  public async Task<bool> AnalyzeLoadedFunction(FunctionIR function, IRTextSection section,
                                                FunctionDebugInfo funcDebugInfo) {
    // Annotate the instructions with debug info (line numbers, source files)
    // if the debug file is specified and available.
    var debugInfo = await GetOrCreateDebugInfoProvider(section.ParentFunction);

    if (debugInfo != null) {
      await Task.Run(() => {
        if (funcDebugInfo != null) {
          return debugInfo.AnnotateSourceLocations(function, funcDebugInfo);
        }
        else {
          return debugInfo.AnnotateSourceLocations(function, section.ParentFunction);
        }
      });
    }

    return true;
  }

  public async Task<IDebugInfoProvider> GetOrCreateDebugInfoProvider(IRTextFunction function) {
    var loadedDoc = Session.FindLoadedDocument(function);

    lock (loadedDoc) {
      if (loadedDoc.DebugInfo != null) {
        return loadedDoc.DebugInfo;
      }
    }

    if (!loadedDoc.DebugInfoFileExists) {
      return null;
    }

    if (loadedDoc.DebugInfoFileExists) {
      var debugInfo = CreateDebugInfoProvider(loadedDoc.DebugInfoFile);

      if (debugInfo != null) {
        lock (loadedDoc) {
          loadedDoc.DebugInfo = debugInfo;
          return debugInfo;
        }
      }
    }

    return null;
  }

  public IDiffInputFilter CreateDiffInputFilter() {
    var filter = new ASMDiffInputFilter();
    filter.Initialize(CoreSettingsProvider.DiffSettings, IR);
    return filter;
  }

  public IDiffOutputFilter CreateDiffOutputFilter() {
    return new BasicDiffOutputFilter();
  }

  //? TODO: Debug/Binary related functs should not be part of CompilerInfoProvider,
  //? probably inside SessionState
  public IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile) {
    if (!debugFile.Found) {
      return null;
    }

    lock (this) {
      if (loadedDebugInfo_.TryGetValue(debugFile, out var provider)) {
        return provider;
      }

      var newProvider = new PDBDebugInfoProvider(CoreSettingsProvider.SymbolSettings);

      if (newProvider.LoadDebugInfo(debugFile, provider)) {
        loadedDebugInfo_[debugFile] = newProvider;
        provider?.Dispose();
        return newProvider;
      }

      return null;
    }
  }

  public async Task<DebugFileSearchResult> FindDebugInfoFileAsync(string imagePath,
                                                                  SymbolFileSourceSettings settings = null) {
    using var info = new PEBinaryInfoProvider(imagePath);

    if (!info.Initialize()) {
      return Utils.LocateDebugInfoFile(imagePath, ".json");
    }

    switch (info.BinaryFileInfo.FileKind) {
      case BinaryFileKind.Native: {
        if (settings == null) {
          // Make sure the binary directory is also included in the symbol search.
          settings = CoreSettingsProvider.SymbolSettings.Clone();
          settings.InsertSymbolPath(imagePath);
        }

        return await FindDebugInfoFileAsync(info.SymbolFileInfo, settings).ConfigureAwait(false);
      }
    }

    return DebugFileSearchResult.None;
  }

  public DebugFileSearchResult FindDebugInfoFile(string imagePath, SymbolFileSourceSettings settings = null) {
    using var info = new PEBinaryInfoProvider(imagePath);

    if (!info.Initialize()) {
      return Utils.LocateDebugInfoFile(imagePath, ".json");
    }

    switch (info.BinaryFileInfo.FileKind) {
      case BinaryFileKind.Native: {
        if (settings == null) {
          // Make sure the binary directory is also included in the symbol search.
          settings = CoreSettingsProvider.SymbolSettings;
          settings.InsertSymbolPath(imagePath);
        }

        return FindDebugInfoFile(info.SymbolFileInfo, settings);
      }
    }

    return DebugFileSearchResult.None;
  }

  public async Task<DebugFileSearchResult>
    FindDebugInfoFileAsync(SymbolFileDescriptor symbolFile, SymbolFileSourceSettings settings = null) {
    if (settings == null) {
      settings = CoreSettingsProvider.SymbolSettings;
    }

    return await PDBDebugInfoProvider.LocateDebugInfoFileAsync(symbolFile, settings).ConfigureAwait(false);
  }

  public DebugFileSearchResult
    FindDebugInfoFile(SymbolFileDescriptor symbolFile, SymbolFileSourceSettings settings = null) {
    if (settings == null) {
      settings = CoreSettingsProvider.SymbolSettings;
    }

    return PDBDebugInfoProvider.LocateDebugInfoFile(symbolFile, settings);
  }

  public async Task<BinaryFileSearchResult> FindBinaryFileAsync(BinaryFileDescriptor binaryFile,
                                                                SymbolFileSourceSettings settings = null) {
    if (settings == null) {
      // Make sure the binary directory is also included in the symbol search.
      settings = CoreSettingsProvider.SymbolSettings.Clone();
    }

    return await PEBinaryInfoProvider.LocateBinaryFileAsync(binaryFile, settings).ConfigureAwait(false);
  }

  public Task ReloadSettings() {
    return Task.CompletedTask;
  }
}