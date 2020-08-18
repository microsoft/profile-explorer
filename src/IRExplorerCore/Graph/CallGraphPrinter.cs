using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.GraphViz {
    public class CallGraphPrinter : GraphVizPrinter {
        private const int LargeGraphThresholdMin = 1000;
        private const int LargeGraphThresholdMax = 5000;
        private const string LargeGraphSettings = @"
maxiter=8;
        ";
        private static readonly string HugeGraphSettings = @"
maxiter=4;
mclimit=2;
nslimit=2;
        ";

        private CallGraph callGraph_;
        private Dictionary<CallGraphNode, uint> nodeToId_;

        public CallGraphPrinter(CallGraph callGraph) {
            callGraph_ = callGraph;
            nodeToId_ = new Dictionary<CallGraphNode, uint>();
        }

        private int EstimateEdgeCount() {
            int total = 0;

            foreach(var node in callGraph_.FunctionNodes) {
                total += node.HasCallees ? node.Callees.Count : 0;
            }

            return total;
        }

        protected override string GetExtraSettings() {
            // Increase the vertical distance between nodes the more there are
            // to make the graph somewhat easier to read.
            int nodeCount = callGraph_.FunctionNodes.Count;
            double verticalDistance = Math.Min(8, 1.15 * Math.Log10(nodeCount));
            var text = $"ranksep={verticalDistance};\n";

            int edgeCount = EstimateEdgeCount();
            int elements = Math.Max(edgeCount, nodeCount);

            if (elements > LargeGraphThresholdMin) {
                if (elements < LargeGraphThresholdMax) {
                    return $"{text}{LargeGraphSettings}";
                }
                else {
                    return $"{text}{HugeGraphSettings}";
                }
            }

            return text;
        }

        protected override void PrintGraph(StringBuilder builder) {
            foreach(var node in callGraph_.FunctionNodes) {
                CreateNode(node, builder);
            }

            foreach (var node in callGraph_.FunctionNodes) {
                foreach(var calleeNode in node.UniqueCallees) {
                    CreateEdge(node, calleeNode, builder);
                }
            }
        }

        private void CreateNode(CallGraphNode node, StringBuilder builder) {
            double verticalMargin = 0.055;
            string label = node.FunctionName;

            if(label.Length > 20) {
                label = $"{label.Substring(0, 18)}...";
            }

            double horizontalMargin = Math.Min(Math.Max(0.1, label.Length * 0.04), 1.0);

            CreateNodeWithMargins((ulong)node.Number, label, builder,
                                   horizontalMargin, verticalMargin);
        }

        private void CreateEdge(CallGraphNode node1, CallGraphNode node2, StringBuilder builder) {
            base.CreateEdge((ulong)node1.Number, (ulong)node2.Number, builder);
        }
    }
}
