// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using IRExplorerCore;
using IRExplorerCore.ASM;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers.Default;
using IRExplorerUI.Diff;
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
  public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>();
  public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>();
  public List<FunctionTaskDefinition> ScriptFunctionTasks => new List<FunctionTaskDefinition>();

  public virtual Task HandleLoadedDocument(LoadedDocument document, string modulePath) {
    return Task.CompletedTask;
  }

  public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
    // Annotate the instructions with debug info (line numbers, source files)
    // if the debug file is specified and available.
    var loadedDoc = Session.SessionState.FindLoadedDocument(section);
    var debugFile = loadedDoc.DebugInfoFile;

    if (loadedDoc.DebugInfo != null) {
      // Used for managed methods.
      loadedDoc.DebugInfo.AnnotateSourceLocations(function, section.ParentFunction);
    }
    else if (debugFile != null && debugFile.Found) {
      using var debugInfo = CreateDebugInfoProvider(loadedDoc.BinaryFile.FilePath);

      if (debugInfo.LoadDebugInfo(debugFile)) {
        debugInfo.AnnotateSourceLocations(function, section.ParentFunction);
      }
    }

    return true;
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
  public IDebugInfoProvider CreateDebugInfoProvider(string imagePath) {
    using var info = new PEBinaryInfoProvider(imagePath);

    if (!info.Initialize()) {
      return new JsonDebugInfoProvider();
    }

    switch (info.BinaryFileInfo.FileKind) {
      case BinaryFileKind.Native: {
        return new PDBDebugInfoProvider(App.Settings.SymbolOptions);
      }
      case BinaryFileKind.DotNetR2R:
      case BinaryFileKind.DotNet: {
        return new JsonDebugInfoProvider();
      }
      default: {
        throw new InvalidOperationException();
      }
    }
  }

  public IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile) {
    if (!debugFile.Found) {
      return null;
    }

    lock (this) {
      if (loadedDebugInfo_.TryGetValue(debugFile, out var provider)) {
        return provider;
      }

      provider = new PDBDebugInfoProvider(App.Settings.SymbolOptions);

      if (provider.LoadDebugInfo(debugFile.FilePath)) {
        loadedDebugInfo_[debugFile] = provider;
        return provider;
      }

      return null;
    }
  }

  public async Task<DebugFileSearchResult> FindDebugInfoFile(string imagePath, SymbolFileSourceOptions options = null) {
    using var info = new PEBinaryInfoProvider(imagePath);

    if (!info.Initialize()) {
      return Utils.LocateDebugInfoFile(imagePath, ".json");
    }

    switch (info.BinaryFileInfo.FileKind) {
      case BinaryFileKind.Native: {
        if (options == null) {
          // Make sure the binary directory is also included in the symbol search.
          options = App.Settings.SymbolOptions.Clone();
          options.InsertSymbolPath(imagePath);
        }

        return await PDBDebugInfoProvider.LocateDebugInfoFile(info.SymbolFileInfo, options).ConfigureAwait(false);
      }
    }

    return DebugFileSearchResult.None;
  }

  public async Task<BinaryFileSearchResult> FindBinaryFile(BinaryFileDescriptor binaryFile,
                                                           SymbolFileSourceOptions options = null) {
    if (options == null) {
      // Make sure the binary directory is also included in the symbol search.
      options = App.Settings.SymbolOptions.Clone();
    }

    return await PEBinaryInfoProvider.LocateBinaryFile(binaryFile, options).ConfigureAwait(false);
  }

  //? TODO: << Debug/Binary related functs should not be part of CompilerInfoProvider

  public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
    return new BasicBlockFoldingStrategy(function);
  }

  public async Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section) {
    // Since the ASM blocks don't have a number in the text,
    // attach an overlay label next to the first instr. in the block.
    CreateBlockLabelOverlays(document, function);

    // Check if there is profile info and annotate the instrs. with timing info.
    var profile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

    if (profile != null) {
      var profileOptions = ProfileDocumentMarkerSettings.Default;
      var profileMarker = new ProfileDocumentMarker(profile, Session.ProfileData, profileOptions, this);
      await profileMarker.Mark(document, function, section.ParentFunction);

      // Redraw the flow graphs, may have loaded before the marker set the node tags.
      Session.RedrawPanels();
    }

    // Annotate instrs. with source line numbers if debug info is available.
    var markerOptions = ProfileDocumentMarkerSettings.Default;
    var sourceMarker = new SourceDocumentMarker(markerOptions, this);
    await sourceMarker.Mark(document, function);
  }

  private static void CreateBlockLabelOverlays(IRDocument document, FunctionIR function) {
    double overlayHeight = document.TextArea.TextView.DefaultLineHeight;
    var options = ProfileDocumentMarkerSettings.Default; //? TODO: Use App.Settings...
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
      overlay.TextWeight = FontWeights.Bold;
      overlay.TextColor = options.BlockOverlayTextColor;

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
