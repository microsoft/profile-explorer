// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public class GraphLayoutCache {
        private ICompilerInfoProvider compilerInfo_;
        private GraphKind graphKind_;
        private Dictionary<IRTextSection, CompressedString> graphLayout_;
        private Dictionary<byte[], CompressedString> shapeGraphLayout_;
        private ReaderWriterLockSlim rwLock_;

        public GraphLayoutCache(GraphKind graphKind, ICompilerInfoProvider compilerInfo) {
            graphKind_ = graphKind;
            compilerInfo_ = compilerInfo;
            shapeGraphLayout_ = new Dictionary<byte[], CompressedString>();
            graphLayout_ = new Dictionary<IRTextSection, CompressedString>();
            rwLock_ = new ReaderWriterLockSlim();
        }

        public Graph GenerateGraph<T, U>(T element, IRTextSection section, CancelableTask task,
                                         U options = null) where T : class where U : class {
            GraphVizPrinter printer = null;
            string graphText;

            //? TODO: Currently only FunctionIR graphs (flow, dominator, etc) are cached.
            bool useCache = typeof(T) == typeof(FunctionIR);

            try {
                rwLock_.EnterUpgradeableReadLock();

                if (useCache && graphLayout_.TryGetValue(section, out var graphData)) {
                    Trace.TraceInformation($"Graph cache: Loading cached section graph for {section}");
                    graphText = graphData.ToString();
                }
                else {
                    // Check if the same Graphviz input was used before, since
                    // the resulting graph will be identical even though the function is not.
                    printer = CreateInstance(graphKind_, element, options);
                    string inputText = printer.PrintGraph();

                    if (string.IsNullOrEmpty(inputText)) {
                        // Printing the graph failed for some reason, like running out of memory.
                        return null;
                    }

                    // The input text is looked up using a SHA256 hash that basically makes each
                    // input unique, use justs 32 bytes of memory and faster to look up.
                    byte[] inputTextHash = null;

                    if (useCache) {
                        inputTextHash = CompressionUtils.CreateSHA256(inputText);
                    }

                    if (useCache && shapeGraphLayout_.TryGetValue(inputTextHash, out var shapeGraphData)) {
                        Trace.TraceInformation($"Graph cache: Loading cached graph layout for {section}");
                        // Associate graph layout with the section.
                        graphText = shapeGraphData.ToString(); // Decompress.
                        CacheGraphLayoutAndShape(section, shapeGraphData);
                    }
                    else {
                        // This is a new graph layout that must be computed through Graphviz.
                        Trace.TraceInformation($"Graph cache: Compute new graph layout for {section}");
                        graphText = printer.CreateGraph(inputText, task);

                        if (string.IsNullOrEmpty(graphText)) {
                            Trace.TraceWarning($"Graph cache: Failed to create graph for {section}");
                            return null; // Failed or canceled by user.
                        }


                    }

                    if (useCache) {
                        CacheGraphLayoutAndShape(section, graphText, inputTextHash);
                    }
                }
            }
            finally {
                rwLock_.ExitUpgradeableReadLock();
            }

            // Parse the graph layout output from Graphviz to build
            // the actual Graph object with nodes and edges.
            printer ??= CreateInstance(graphKind_, element, options);

            if (useCache) {
                printer.PrintGraph(); // Force creation of node/group maps.
            }

            var blockNodeMap = printer.CreateNodeDataMap();
            var blockNodeGroupsMap = printer.CreateNodeDataGroupsMap();
            var graphReader = new GraphvizReader(graphKind_, graphText, blockNodeMap);
            var layoutGraph = graphReader.ReadGraph();

            if (layoutGraph != null) {
                layoutGraph.GraphOptions = options;
                layoutGraph.DataNodeGroupsMap = blockNodeGroupsMap;
            }

            return layoutGraph;
        }

        public GraphVizPrinter CreateInstance<T, U>(
            GraphKind kind, T element, U options) where T : class where U : class {
            if (typeof(T) == typeof(FunctionIR)) {
                return kind switch {
                    GraphKind.FlowGraph => new FlowGraphPrinter(element as FunctionIR, compilerInfo_.CreateGraphNameProvider(kind)),
                    GraphKind.DominatorTree => new DominatorTreePrinter(element as FunctionIR,
                                                                        DominatorAlgorithmOptions.Dominators,
                                                                        compilerInfo_.CreateGraphNameProvider(kind)),
                    GraphKind.PostDominatorTree => new DominatorTreePrinter(element as FunctionIR,
                                                                            DominatorAlgorithmOptions.PostDominators,
                                                                            compilerInfo_.CreateGraphNameProvider(kind)),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
                };
            }
            else if (typeof(T) == typeof(IRElement)) {
                switch (kind) {
                    case GraphKind.ExpressionGraph: {
                        return new ExpressionGraphPrinter(element as IRElement,
                                                          options as ExpressionGraphPrinterOptions,
                                                          compilerInfo_.CreateGraphNameProvider(kind));
                    }
                }
            }
            else if (typeof(T) == typeof(IRTextSummary)) {
                switch (kind) {
                    case GraphKind.CallGraph: {
                        return new CallGraphPrinter(element as CallGraph,
                                                    options as CallGraphPrinterOptions,
                                                    compilerInfo_.CreateGraphNameProvider(kind));
                    }
                }
            }

            throw new NotImplementedException("Unsupported graph type");
        }

        private void CacheGraphLayoutAndShape(IRTextSection section, CompressedString shapeGraphData) {
            // Acquire the write lock before updating shared data.
            try {
                rwLock_.EnterWriteLock();
                graphLayout_.Add(section, shapeGraphData);
            }
            finally {
                rwLock_.ExitWriteLock();
            }
        }

        private void CacheGraphLayoutAndShape(IRTextSection section, string graphText, byte[] inputTextHash) {
            var compressedGraphText = new CompressedString(graphText);

            // Acquire the write lock before updating shared data.
            try {
                rwLock_.EnterWriteLock();
                graphLayout_[section] = compressedGraphText;
                shapeGraphLayout_[inputTextHash] = compressedGraphText;
            }
            finally {
                rwLock_.ExitWriteLock();
            }
        }

        public void ClearCache() {
            graphLayout_.Clear();
            shapeGraphLayout_.Clear();
        }
    }
}