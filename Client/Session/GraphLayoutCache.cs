// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using Core;
using Core.Graph;
using Core.GraphViz;
using Core.IR;

namespace Client {
    public class GraphLayoutCache {
        public struct CachedGraphLayout {
            public string Text;
            public Dictionary<string, BlockIR> BlockNameMap;

            public CachedGraphLayout(string text, Dictionary<string, BlockIR> blockNameMap) {
                Text = text;
                BlockNameMap = blockNameMap;
            }
        }

        private GraphKind graphKind_;
        private Dictionary<string, byte[]> shapeGraphLayout_;
        private Dictionary<IRTextSection, byte[]> graphLayout_;

        public GraphLayoutCache(GraphKind graphKind) {
            graphKind_ = graphKind;
            shapeGraphLayout_ = new Dictionary<string, byte[]>();
            graphLayout_ = new Dictionary<IRTextSection, byte[]>();
        }

        public LayoutGraph GenerateGraph(FunctionIR function, IRTextSection section,
                                         CancelableTaskInfo task) {
            var printer = GraphPrinterFactory.CreateInstance(graphKind_, function);
            string graphText;

            lock (this) {
                if (graphLayout_.TryGetValue(section, out var graphData)) {
                    Trace.TraceInformation($"Graph cache: Loading cached section graph for {section}");
                    graphText = CompressionUtils.DecompressString(graphData);
                    task.Completed();
                }
                else {
                    // Check if the same Graphviz input was used before, since
                    // the resulting graph will be identical even though the function is not.
                    var inputText = printer.PrintGraph();

                    if (shapeGraphLayout_.TryGetValue(inputText, out var shapeGraphData)) {
                        Trace.TraceInformation($"Graph cache: Loading cached graph layout for {section}");
                        graphText = CompressionUtils.DecompressString(shapeGraphData);
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
                        if (!string.IsNullOrEmpty(graphText)) {
                            var compresedGraphText = CompressionUtils.CompressString(graphText);
                            graphLayout_.Add(section, compresedGraphText);
                            shapeGraphLayout_.Add(inputText, compresedGraphText);
                        }
                    }
                }
            }

            var graphReader = new GraphvizReader(graphKind_, graphText, printer.CreateBlockNodeMap());
            return graphReader.ReadGraph();
        }

        public void ClearCache() {
            graphLayout_.Clear();
        }
    }
}
