// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;
using IRExplorerCore.MLIR;
using IRExplorerUI.Compilers.ASM;

namespace IRExplorerUI.Compilers.LLVM {
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

            if (section.OutputBefore != null) {

            }

            return true;
        }

        public sealed class RawIRGraphPrinter : GraphVizPrinter {
            private IRExplorerCore.RawIRModel.Graph graph_;
            private StringBuilder builder_;
            private FunctionIR func_;

            public RawIRGraphPrinter(IRExplorerCore.RawIRModel.Graph graph, FunctionIR func, GraphPrinterNameProvider nameProvider) : base(nameProvider) {
                graph_ = graph;
                func_ = func;
            }

            protected override void PrintGraph(StringBuilder builder) {
                builder_ = builder;
                // rootElement_ = CreateFakeIRElement();
                // CreateNode(rootElement_, null, "ROOT");
                // CreateEdge(rootElement_, exprNode);

                PrintNodes();
                PrintEdges();
            }

            public override Dictionary<string, TaggedObject> CreateNodeDataMap() {
                return new Dictionary<string, TaggedObject>();
            }

            public override Dictionary<TaggedObject, List<TaggedObject>> CreateNodeDataGroupsMap() {
                return new Dictionary<TaggedObject, List<TaggedObject>>();
            }

            private void PrintEdges() {
                foreach (var node in graph_.Nodes) {
                    foreach (var edge in node.Edges) {
                        CreateEdge((ulong)edge.FromNodeId, (ulong)edge.ToNodeId, builder_);
                    }
                }
            }

            private void PrintNodes() {
                foreach (var node in graph_.Nodes) {
                    string label = "";

                    if (!string.IsNullOrEmpty(node.Operation)) {
                        if (node.Operation.Length > 20) {
                            label = $"{node.Name.ToString()}\\n{node.Operation.Substring(0, 20)}";
                        }
                        else {
                            label = $"{node.Name.ToString()}\\n{node.Operation}";
                        }
                    }
                    else {
                        label = $"{node.Name.ToString()}\\nfunc.entry";
                    }

                    bool isMultiline = label.Contains("\\n");
                    double verticalMargin = isMultiline ? 0.12 : 0.06;
                    double horizontalMargin = Math.Min(Math.Max(0.1, label.Length * (isMultiline ? 0.02 : 0.03)), 2.0);

                    var nodeName = CreateNodeWithMargins((ulong)node.Id, label, builder_, horizontalMargin, verticalMargin);
                }
            }
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