// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.Graph;
using IRExplorerCore.MLIR;
using IRExplorerUI.Compilers.ASM;
using FunctionIR = IRExplorerCore.IR.FunctionIR;
using Graph = IRExplorerCore.Graph.Graph;

namespace IRExplorerUI.Compilers.MLIR {
    public class MLIRCompilerInfoProvider : ICompilerInfoProvider {
        private MLIRCompilerIRInfo ir_;
        private ISession session_;
        private INameProvider names_;
        private IRRemarkProvider remarks_;
        private ISectionStyleProvider styles_;

        public MLIRCompilerInfoProvider(ISession session) {
            session_ = session;
            ir_ = new MLIRCompilerIRInfo();
            styles_ = new UTCSectionStyleProvider();
            names_ = new UTCNameProvider();
            remarks_ = new DummyIRRemarkProvider();
        }

        //? TODO: Rename to MLIR
        public string CompilerIRName => "LLVM";
        public string CompilerDisplayName => "LLVM";
        public string DefaultSyntaxHighlightingFile => "LLVM IR";
        public string OpenFileFilter => "IR Files|*.mlir;*.log;*.ir;*.out;*.irx|IR Explorer Session Files|*.irx|All Files|*.*";
        public string OpenDebugFileFilter => "Debug Files|*.pdb|All Files|*.*";
        public ISession Session => session_;
        public ICompilerIRInfo IR => ir_;
        public INameProvider NameProvider => names_;
        public ISectionStyleProvider SectionStyleProvider => styles_;
        public IRRemarkProvider RemarkProvider => remarks_;

        public GraphPrinterNameProvider CreateGraphNameProvider(GraphKind graphKind) {
            return new MLIRGraphPrinterNameProvider();
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
                    new MLIRExpressionGraphStyleProvider(graph, (ExpressionGraphSettings)settings, this),
                GraphKind.CallGraph =>
                    new CallGraphStyleProvider(graph)
            };
        }

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function, IRTextSection section) {
            return new BasicBlockFoldingStrategy(function, section);
        }

        public IDiffInputFilter CreateDiffInputFilter() {
            return null;
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            return new UTCDiffOutputFilter();
        }

        public IDebugInfoProvider CreateDebugInfoProvider(string imagePath) {
            return new PDBDebugInfoProvider(App.Settings.SymbolOptions);
        }

        public IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile) {
            return null;
        }

        public async Task<DebugFileSearchResult> FindDebugInfoFile(string imagePath, SymbolFileSourceOptions options) {
            return Utils.LocateDebugInfoFile(imagePath, ".pdb");
        }

        public Task<BinaryFileSearchResult> FindBinaryFile(BinaryFileDescriptor binaryFile, SymbolFileSourceOptions options = null) {
            return null;
        }

        public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>() { };
        public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>() { };
        public List<FunctionTaskDefinition> ScriptFunctionTasks => new List<FunctionTaskDefinition>() { };

        public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
            //? TODO: var loopGraph = new LoopGraph(function);
            //loopGraph.FindLoops();

            return true;
        }

        public Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section) {
            return Task.CompletedTask;
        }

        public Task HandleLoadedDocument(LoadedDocument document, string modulePath) {
            return Task.CompletedTask;
        }

        public void ReloadSettings() {
        }
    }
}