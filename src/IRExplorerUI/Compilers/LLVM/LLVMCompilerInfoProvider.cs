// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;
using IRExplorerCore.LLVM;
using IRExplorerUI.Compilers.ASM;

namespace IRExplorerUI.Compilers.LLVM {
    public class LLVMCompilerInfoProvider : ICompilerInfoProvider {
        private LLVMCompilerIRInfo ir_;
        private ISession session_;
        private INameProvider names_;
        private IRRemarkProvider remarks_;
        private ISectionStyleProvider styles_;

        public LLVMCompilerInfoProvider() {
            ir_ = new LLVMCompilerIRInfo();
            styles_ = new UTCSectionStyleProvider();
            names_ = new UTCNameProvider();
            remarks_ = new DummyIRRemarkProvider();
        }

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

        public GraphVizPrinterNameProvider CreateGraphNameProvider(GraphKind graphKind) {
            return new GraphVizPrinterNameProvider();
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

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            return new BasicBlockFoldingStrategy(function);
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