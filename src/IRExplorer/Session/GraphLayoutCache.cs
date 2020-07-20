// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using IRExplorerCore;
using IRExplorerCore.Graph;
using IRExplorerCore.GraphViz;
using IRExplorerCore.IR;

namespace IRExplorer {
    public class GraphLayoutCache {
        private GraphKind graphKind_;
        private Dictionary<IRTextSection, CompressedString> graphLayout_;
        private Dictionary<byte[], CompressedString> shapeGraphLayout_;

        public GraphLayoutCache(GraphKind graphKind) {
            graphKind_ = graphKind;
            shapeGraphLayout_ = new Dictionary<byte[], CompressedString>();
            graphLayout_ = new Dictionary<IRTextSection, CompressedString>();
        }

        public LayoutGraph GenerateGraph<T, U>(T element, IRTextSection section, CancelableTaskInfo task,
                                               U options = null) where T : class where U : class {
            var printer = GraphPrinterFactory.CreateInstance(graphKind_, element, options);
            string graphText;

            lock (this) {
                bool useCache = typeof(T) == typeof(FunctionIR);

                if (useCache && graphLayout_.TryGetValue(section, out var graphData)) {
                    Trace.TraceInformation($"Graph cache: Loading cached section graph for {section}");
                    graphText = graphData.ToString();
                    task.Completed();
                }
                else {
                    // Check if the same Graphviz input was used before, since
                    // the resulting graph will be identical even though the function is not.
                    string inputText = printer.PrintGraph();

                    if (string.IsNullOrEmpty(inputText)) {
                        // Printing the graph failed for some reason, like running out of memory.
                        return null;
                    }

                    // The input text is looked up using a SHA256 hash that basically makes each
                    // input unique, use just 32 bytes of memory and faster to look up.
                    var inputTextHash = CompressionUtils.CreateSHA256(inputText);

                    if (shapeGraphLayout_.TryGetValue(inputTextHash, out var shapeGraphData)) {
                        Trace.TraceInformation($"Graph cache: Loading cached graph layout for {section}");
                        graphText = shapeGraphData.ToString();
                        graphLayout_.Add(section, shapeGraphData);
                        task.Completed();
                    }
                    else {
                        Trace.TraceInformation($"Graph cache: Compute new graph layout for {section}");
                        graphText = printer.CreateGraph(inputText, task);

                        if (graphText == null) {
                            Trace.TraceWarning($"Graph cache: Failed to create graph for {section}");
                            return null; // Failed or canceled by user.
                        }

                        // Cache the text only if there wasn't a failure for some reason.
                        if (useCache && !string.IsNullOrEmpty(graphText)) {
                            //? TODO: Compression should be done as a Task to reduce latency
                            var compressedGraphText = new CompressedString(graphText);
                            graphLayout_[section] = compressedGraphText;
                            shapeGraphLayout_[inputTextHash] = compressedGraphText;
                        }
                    }
                }
            }

            var blockNodeMap = printer.CreateBlockNodeMap();
            var blockNodeGroupsMap = printer.CreateBlockNodeGroupsMap();
            var graphReader = new GraphvizReader(graphKind_, graphText, blockNodeMap);
            var layoutGraph = graphReader.ReadGraph();

            if (layoutGraph != null) {
                layoutGraph.ElementNodeGroupsMap = blockNodeGroupsMap;
            }

            return layoutGraph;
        }

        public void ClearCache() {
            graphLayout_.Clear();
        }
    }
}
