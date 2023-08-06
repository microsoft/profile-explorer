// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.ASM;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using IRExplorerUI.Profile;
using IRExplorerCore.IR.Tags;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using IRExplorerCore.Graph;
using static IRExplorerUI.ModuleReportPanel;

namespace IRExplorerUI.Compilers.ASM;

public class ASMCompilerInfoProvider : ICompilerInfoProvider {
    private readonly ISession session_;
    private readonly ASMNameProvider names_ = new ASMNameProvider();
    private readonly DummySectionStyleProvider styles_ = new DummySectionStyleProvider();
    private readonly UTCRemarkProvider remarks_;
    private readonly ASMCompilerIRInfo ir_;

    public ASMCompilerInfoProvider(IRMode mode, ISession session) {
        session_ = session;
        remarks_ = new UTCRemarkProvider(this);
        ir_ = new ASMCompilerIRInfo(mode);
    }

    public ISession Session => session_;

    public virtual string CompilerIRName => "ASM";

    public virtual string CompilerDisplayName => "ASM " + ir_.Mode.ToString();

    public virtual string OpenFileFilter => "ASM and Binary Files|*.asm;*.txt;*.log;*.exe;*.dll;*.sys|All Files|*.*";
    public virtual string OpenDebugFileFilter => "Debug Files|*.pdb|All Files|*.*";

    public virtual string DefaultSyntaxHighlightingFile => (ir_.Mode == IRMode.ARM64 ?  "ARM64" : "x86") + " ASM IR";

    public ICompilerIRInfo IR => ir_;

    public INameProvider NameProvider => names_;

    public ISectionStyleProvider SectionStyleProvider => styles_;

    public IRRemarkProvider RemarkProvider => remarks_;

    public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>();

    public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>();

    public List<FunctionTaskDefinition> ScriptFunctionTasks => new List<FunctionTaskDefinition>();

    public GraphPrinterNameProvider CreateGraphNameProvider(GraphKind graphKind) {
        return new GraphPrinterNameProvider();
    }

    public IGraphStyleProvider CreateGraphStyleProvider(Graph graph, GraphSettings settings) {
        return graph.Kind switch {
            GraphKind.FlowGraph =>
                new FlowGraphStyleProvider(graph, (FlowGraphSettings)settings),
            GraphKind.DominatorTree =>
                new FlowGraphStyleProvider(graph, (FlowGraphSettings)settings),
            GraphKind.PostDominatorTree =>
                new FlowGraphStyleProvider(graph, (FlowGraphSettings)settings),
            GraphKind.ExpressionGraph =>
                new ExpressionGraphStyleProvider(graph, (ExpressionGraphSettings)settings, this),
            GraphKind.CallGraph =>
                new CallGraphStyleProvider(graph)
        };
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

    //? TODO: Debug/Binary related functs should not be part of CompilerInfoProvider
    public IDebugInfoProvider CreateDebugInfoProvider(string imagePath) {
        using var info = new PEBinaryInfoProvider(imagePath);

        if (!info.Initialize()) {
            return new JsonDebugInfoProvider();
        }

        //? Cache

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

    private static Dictionary<DebugFileSearchResult, IDebugInfoProvider> loadedDebugInfo_ = new();

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

                var result = await PDBDebugInfoProvider.LocateDebugInfoFile(info.SymbolFileInfo, options).ConfigureAwait(false);

                if (result != null) {
                    return result;
                }

                //? TODO: Shouldn't be needed anymore
                // Do a simple search otherwise.
                return Utils.LocateDebugInfoFile(imagePath, ".pdb");
            }
        }

        return DebugFileSearchResult.None;
    }

    public async Task<BinaryFileSearchResult> FindBinaryFile(BinaryFileDescriptor binaryFile, SymbolFileSourceOptions options = null) {
        if (options == null) {
            // Make sure the binary directory is also included in the symbol search.
            options = App.Settings.SymbolOptions.Clone();
        }

        return await PEBinaryInfoProvider.LocateBinaryFile(binaryFile, options).ConfigureAwait(false);
    }

    //? TODO: << Debug/Binary related functs should not be part of CompilerInfoProvider

    public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function, IRTextSection section) {
        return new BasicBlockFoldingStrategy(function, section);
    }

    public virtual Task HandleLoadedDocument(LoadedDocument document, string modulePath) {
        //? TODO: This could assign the FunctionDebugInfo to each IRTextFunction
        //? instead of attaching it to FunctionProfileData
        return Task.CompletedTask;
    }

    public async Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section) {
        // Since the ASM blocks don't have a number in the text,
        // attach an overlay label next to the first instr. in the block.
        var overlayHeight = document.TextArea.TextView.DefaultLineHeight;
        var options = ProfileDocumentMarkerOptions.Default; //? TODO: App.Settings...
        var blockPen = ColorPens.GetPen(options.BlockOverlayBorderColor,
            options.BlockOverlayBorderThickness);

        foreach (var block in function.Blocks) {
            if (block.Tuples.Count <= 0) {
                continue;
            }

            var label = $"B{block.Number}";
            var overlay = document.AddIconElementOverlay(block, null, 0, overlayHeight, label, null,
                HorizontalAlignment.Left, VerticalAlignment.Center);
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

        // Check if there is profile info and annotate the instrs. with timing info.
        var profile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

        if(profile != null) {
            var profileOptions = ProfileDocumentMarkerOptions.Default;
            var profileMarker = new ProfileDocumentMarker(profile, Session.ProfileData, profileOptions, this);
            document.ColumnData = await profileMarker.Mark(document, function, section.ParentFunction);

            // Redraw the flow graphs, may have loaded before the marker set the node tags.
            Session.RedrawPanels();
        }

        // Annotate instrs. with source line numbers if debug info is available.
        var markerOptions = ProfileDocumentMarkerOptions.Default;
        var sourceMarker = new SourceDocumentMarker(markerOptions, ir_);
        await sourceMarker.Mark(document, function);
    }

    public void ReloadSettings() {

    }
}