// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Diff;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Compilers.ASM;

public class ASMCompilerInfoProvider : ICompilerInfoProvider {
  private static Dictionary<DebugFileSearchResult, IDebugInfoProvider> loadedDebugInfo_ = new();
  private readonly ISession session_;
  private readonly ASMNameProvider names_ = new();
  private readonly ASMCompilerIRInfo ir_;
  private readonly IBinaryFileFinder binaryFileFinder_;
  private readonly IDebugFileFinder debugFileFinder_;
  private readonly IDebugInfoProviderFactory debugInfoProviderFactory_;

  public ASMCompilerInfoProvider(IRMode mode, ISession session) {
    session_ = session;
    ir_ = new ASMCompilerIRInfo(mode);
    binaryFileFinder_ = new ASMBinaryFileFinder();
    debugFileFinder_ = new ASMDebugFileFinder();
    debugInfoProviderFactory_ = new ASMDebugInfoProviderFactory();
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
  public IBinaryFileFinder BinaryFileFinder => binaryFileFinder_;
  public IDebugFileFinder DebugFileFinder => debugFileFinder_;
  public IDebugInfoProviderFactory DebugInfoProviderFactory => debugInfoProviderFactory_;

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
      var debugInfo = DebugInfoProviderFactory.CreateDebugInfoProvider(loadedDoc.DebugInfoFile);

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

  public Task ReloadSettings() {
    return Task.CompletedTask;
  }
}