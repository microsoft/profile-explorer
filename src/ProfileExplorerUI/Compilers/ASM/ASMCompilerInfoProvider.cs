﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using ProfileExplorer.Core;
using ProfileExplorer.Core.ASM;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Binary;
using ProfileExplorer.UI.Compilers.Default;
using ProfileExplorer.UI.Diff;
using ProfileExplorer.UI.Profile;
using ProfileExplorer.UI.Query;

namespace ProfileExplorer.UI.Compilers.ASM;

public class ASMCompilerInfoProvider : ICompilerInfoProvider {
  private static Dictionary<DebugFileSearchResult, IDebugInfoProvider> loadedDebugInfo_ = new();
  private readonly ISession session_;
  private readonly ASMNameProvider names_ = new();
  private readonly DummySectionStyleProvider styles_ = new();
  private readonly DefaultRemarkProvider remarks_;
  private readonly ASMCompilerIRInfo ir_;

  public ASMCompilerInfoProvider(IRMode mode, ISession session) {
    session_ = session;
    remarks_ = new DefaultRemarkProvider(this);
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
  public ISectionStyleProvider SectionStyleProvider => styles_;
  public IRRemarkProvider RemarkProvider => remarks_;
  public List<QueryDefinition> BuiltinQueries => new();
  public List<FunctionTaskDefinition> BuiltinFunctionTasks => new();
  public List<FunctionTaskDefinition> ScriptFunctionTasks => new();

  public virtual Task HandleLoadedDocument(LoadedDocument document, string modulePath) {
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
    var loadedDoc = Session.SessionState.FindLoadedDocument(function);

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
      if (loadedDebugInfo_.TryGetValue(debugFile, out var provider)) {
        return provider;
      }

      var newProvider = new PDBDebugInfoProvider(App.Settings.SymbolSettings);

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
          settings = App.Settings.SymbolSettings.Clone();
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
          settings = App.Settings.SymbolSettings.Clone();
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
      settings = App.Settings.SymbolSettings;
    }

    return await PDBDebugInfoProvider.LocateDebugInfoFileAsync(symbolFile, settings).ConfigureAwait(false);
  }

  public DebugFileSearchResult
    FindDebugInfoFile(SymbolFileDescriptor symbolFile, SymbolFileSourceSettings settings = null) {
    if (settings == null) {
      settings = App.Settings.SymbolSettings;
    }

    return PDBDebugInfoProvider.LocateDebugInfoFile(symbolFile, settings);
  }

  public async Task<BinaryFileSearchResult> FindBinaryFileAsync(BinaryFileDescriptor binaryFile,
                                                                SymbolFileSourceSettings settings = null) {
    if (settings == null) {
      // Make sure the binary directory is also included in the symbol search.
      settings = App.Settings.SymbolSettings.Clone();
    }

    return await PEBinaryInfoProvider.LocateBinaryFileAsync(binaryFile, settings).ConfigureAwait(false);
  }

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

  public Task ReloadSettings() {
    return Task.CompletedTask;
  }

  private static void CreateBlockLabelOverlays(IRDocument document, FunctionIR function) {
    double overlayHeight = document.TextArea.TextView.DefaultLineHeight;
    var options = App.Settings.DocumentSettings.ProfileMarkerSettings;
    var blockPen = ColorPens.GetPen(options.BlockOverlayBorderColor,
                                    options.BlockOverlayBorderThickness);
    document.SuspendUpdate();

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

    document.ResumeUpdate();
  }
}