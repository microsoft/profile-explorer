// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using IRExplorerCore;
using IRExplorerCore.ASM;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers.Default;
using IRExplorerUI.Diff;
using IRExplorerUI.Document;
using IRExplorerUI.Profile;
using IRExplorerUI.Query;

namespace IRExplorerUI.Compilers.ASM;

public class ASMCompilerInfoProvider : ICompilerInfoProvider {
  private static Dictionary<DebugFileSearchResult, IDebugInfoProvider> loadedDebugInfo_ =
    new Dictionary<DebugFileSearchResult, IDebugInfoProvider>();
  private readonly ISession session_;
  private readonly ASMNameProvider names_ = new ASMNameProvider();
  private readonly DummySectionStyleProvider styles_ = new DummySectionStyleProvider();
  private readonly DefaultRemarkProvider remarks_;
  private readonly ASMCompilerIRInfo ir_;
  private readonly SourceFileFinder sourceFileFinder_;

  public ASMCompilerInfoProvider(IRMode mode, ISession session) {
    session_ = session;
    remarks_ = new DefaultRemarkProvider(this);
    ir_ = new ASMCompilerIRInfo(mode);
    sourceFileFinder_ = new SourceFileFinder(session);
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
  public ISectionStyleProvider SectionStyleProvider => styles_;
  public IRRemarkProvider RemarkProvider => remarks_;
  public SourceFileFinder SourceFileFinder => sourceFileFinder_;
  public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>();
  public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>();
  public List<FunctionTaskDefinition> ScriptFunctionTasks => new List<FunctionTaskDefinition>();

  public virtual Task HandleLoadedDocument(LoadedDocument document, string modulePath) {
    return Task.CompletedTask;
  }

  public async Task<bool> AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
    // Annotate the instructions with debug info (line numbers, source files)
    // if the debug file is specified and available.
    var debugInfo = await GetOrCreateDebugInfoProvider(section.ParentFunction);

    if (debugInfo != null) {
      debugInfo.AnnotateSourceLocations(function, section.ParentFunction);
    }

    return true;
  }

  public async Task<IDebugInfoProvider> GetOrCreateDebugInfoProvider(IRTextFunction function) {
    var loadedDoc = Session.SessionState.FindLoadedDocument(function);

    lock (loadedDoc) {
      if (loadedDoc.DebugInfo != null) {
        if (loadedDoc.DebugInfo.CanUseInstance()) {
          // Used for managed binaries, where the debug info is constructed during profiling.
          return loadedDoc.DebugInfo;
        }
        else {
          loadedDoc.DebugInfo.Dispose();
          loadedDoc.DebugInfo = null;
        }
      }
    }

    if (!loadedDoc.DebugInfoFileExists) {
        return null;
    }

    if (loadedDoc.BinaryFileExists) {
      var debugInfo = CreateDebugInfoProvider(loadedDoc.DebugInfoFile);

      if (debugInfo != null) {
        lock (loadedDoc) {
          loadedDoc.DebugInfo = debugInfo;
          return debugInfo;
        }
      }
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
    filter.Initialize(App.Settings.DiffSettings, IR);
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
      if (loadedDebugInfo_.TryGetValue(debugFile, out var provider) &&
          provider.CanUseInstance()) {
        return provider;
      }

      var newProvider = new PDBDebugInfoProvider(App.Settings.SymbolSettings);

      if (newProvider.LoadDebugInfo(debugFile.FilePath, provider)) {
        loadedDebugInfo_[debugFile] = newProvider;
        provider?.Dispose();
        return newProvider;
      }

      return null;
    }
  }

  public async Task<DebugFileSearchResult> FindDebugInfoFile(string imagePath, SymbolFileSourceSettings settings = null) {
    using var info = new PEBinaryInfoProvider(imagePath);

    if (!info.Initialize()) {
      return Utils.LocateDebugInfoFile(imagePath, ".json");
    }

    switch (info.BinaryFileInfo.FileKind) {
      case BinaryFileKind.Native: {
        if (settings == null) {
          // Make sure the binary directory is also included in the symbol search.
          settings = App.Settings.SymbolSettings.Clone();
          settings.InsertSymbolPath(imagePath);
        }

        return await FindDebugInfoFile(info.SymbolFileInfo, settings).ConfigureAwait(false);
      }
    }

    return DebugFileSearchResult.None;
  }

  public async Task<DebugFileSearchResult>
    FindDebugInfoFile(SymbolFileDescriptor symbolFile, SymbolFileSourceSettings settings = null) {
    if (settings == null) {
      settings = App.Settings.SymbolSettings;
    }

    return await PDBDebugInfoProvider.LocateDebugInfoFile(symbolFile, settings).ConfigureAwait(false);
  }

  public async Task<BinaryFileSearchResult> FindBinaryFile(BinaryFileDescriptor binaryFile,
                                                           SymbolFileSourceSettings settings = null) {
    if (settings == null) {
      // Make sure the binary directory is also included in the symbol search.
      settings = App.Settings.SymbolSettings.Clone();
    }

    return await PEBinaryInfoProvider.LocateBinaryFile(binaryFile, settings).ConfigureAwait(false);
  }

  //? TODO: << Debug/Binary related functs should not be part of CompilerInfoProvider

  public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
    return new BasicBlockFoldingStrategy(function);
  }

  public async Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section) {
    // Since the ASM blocks don't have a number in the text,
    // attach an overlay label next to the first instr. in the block.
    CreateBlockLabelOverlays(document, function);

    // Annotate instrs. with source line numbers if debug info is available.
    var sourceMarker = new SourceDocumentMarker(App.Settings.DocumentSettings.SourceMarkerSettings, this);
    await sourceMarker.Mark(document, function);
  }

  private static void CreateBlockLabelOverlays(IRDocument document, FunctionIR function) {
    double overlayHeight = document.TextArea.TextView.DefaultLineHeight;
    var options = App.Settings.DocumentSettings.ProfileMarkerSettings;
    var blockPen = ColorPens.GetPen(options.BlockOverlayBorderColor,
                                    options.BlockOverlayBorderThickness);

    foreach (var block in function.Blocks) {
      if (block.Tuples.Count <= 0) {
        continue;
      }

      string label = $"B{block.Number}";
      var overlay = document.AddIconElementOverlay(block, null, 0, overlayHeight, label, null,
                                                   HorizontalAlignment.Left);
      overlay.MarginX = -8;
      overlay.Padding = 4;
      overlay.ShowOnMarkerBar = false;
      overlay.IsLabelPinned = true;
      overlay.AllowLabelEditing = false;
      overlay.TextWeight = FontWeights.Bold;
      overlay.TextColor = options.BlockOverlayTextColor.AsBrush();

      var backColor = block.HasEvenIndexInFunction ?
        App.Settings.DocumentSettings.BackgroundColor :
        App.Settings.DocumentSettings.AlternateBackgroundColor;
      overlay.Background = ColorBrushes.GetBrush(backColor);
      overlay.Border = blockPen;

      overlay.ShowBackgroundOnMouseOverOnly = false;
      overlay.ShowBorderOnMouseOverOnly = false;
      overlay.UseLabelBackground = true;
    }
  }

  public Task ReloadSettings() {
    return Task.CompletedTask;
  }
}