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
        private Dictionary<string, object> nodeNameMap_;

        public CallGraphPrinter(CallGraph callGraph) {
            callGraph_ = callGraph;
            nodeNameMap_ = new Dictionary<string, object>();
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
            //? TODO: Control through options
            double verticalMargin = 0.075;
            string label = node.FunctionName;

            if(label.Length > 20) {
                label = $"{label.Substring(0, 18)}...";
            }

            // Increase node weight so that text fits completely.
            double horizontalMargin = Math.Min(Math.Max(0.1, label.Length * 0.04), 1.0);

            var nodeName = CreateNodeWithMargins((ulong)node.Number, label, builder,
                                                 horizontalMargin, verticalMargin);
            nodeNameMap_[nodeName] = node;
        }

        private void CreateEdge(CallGraphNode node1, CallGraphNode node2, StringBuilder builder) {
            CreateEdge((ulong)node1.Number, (ulong)node2.Number, builder);
        }

        public override Dictionary<string, object> CreateNodeDataMap() {
            if (nodeNameMap_.Count > 0) {
                return nodeNameMap_;
            }

            var map = new Dictionary<string, object>();

            foreach (var node in callGraph_.FunctionNodes) {
                map[GetNodeName((ulong)node.Number)] = node;
            }

            return map;
        }

        public override Dictionary<object, List<object>> CreateNodeDataGroupsMap() {
            return null;
        }
    }
}
