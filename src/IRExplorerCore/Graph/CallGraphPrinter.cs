using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph {
    public class CallGraphPrinterOptions {
        public bool UseExternalNode { get; set; }
        public bool UseStraightLines { get; set; }
        public bool UseSingleIncomingEdge { get; set; }
        public bool TruncateLongNames { get; set; }
        public int NameLengthLimit { get; set; }
        public double VerticalDistanceFactor { get; set; }
    }

    public sealed class CallGraphPrinter : GraphVizPrinter {
        private const int ExternalNodeId = -1;
        private const int LargeGraphThresholdMin = 500;

        private const string StraightLinesSettings = @"
splines = ortho;
concentrate = true;
            ";
        private const string LargeGraphSettings = @"
splines = ortho;
concentrate = true;
maxiter=4;
mclimit=2;
nslimit=2;
        ";

        private CallGraphPrinterOptions options_;
        private CallGraph callGraph_;
        private Dictionary<string, TaggedObject> nodeNameMap_;
        private HashSet<CallGraphNode> incomingEdgeNodes_;

        public CallGraphPrinter(CallGraph callGraph, CallGraphPrinterOptions options) {
            callGraph_ = callGraph;
            options_ = options;
            nodeNameMap_ = new Dictionary<string, TaggedObject>();

            if (options_.UseSingleIncomingEdge) {
                incomingEdgeNodes_ = new HashSet<CallGraphNode>();
            }
        }

        private int EstimateEdgeCount() {
            int total = 0;

            foreach (var node in callGraph_.FunctionNodes) {
                total += node.HasCallees ? node.Callees.Count : 0;
            }

            return total;
        }

        protected override string GetExtraSettings() {
            var text = "";

            if (options_.UseStraightLines) {
                text = StraightLinesSettings;
            }

            // Increase the vertical distance between nodes the more there are
            // to make the graph somewhat easier to read.
            int nodeCount = callGraph_.FunctionNodes.Count;
            // double verticalDistance = Math.Clamp(0.3 * Math.Log(nodeCount), 0.3, 1);
            double verticalDistance = 1;
            text = $"{text}\nranksep ={verticalDistance};\n";

            int edgeCount = EstimateEdgeCount();
            int elements = Math.Max(edgeCount, nodeCount);
            return elements > LargeGraphThresholdMin ? $"{text}{LargeGraphSettings}" : text;
        }

        protected override void PrintGraph(StringBuilder builder) {
            if (options_.UseExternalNode) {
                CreateNode(ExternalNodeId, "EXTERNAL", builder);
            }

            foreach (var node in callGraph_.FunctionNodes) {
                CreateNode(node, builder);
            }

            foreach (var node in callGraph_.FunctionNodes) {
                foreach (var calleeNode in node.UniqueCallees) {
                    CreateEdge(node, calleeNode, builder);
                }

                if (options_.UseExternalNode && !node.HasCallers) {
                    CreateEdge(ExternalNodeId, node.Number, builder);
                }
            }
        }

        private void CreateNode(CallGraphNode node, StringBuilder builder) {
            //? TODO: Control through options
            double verticalMargin = 0.1;
            string label = node.FunctionName;

            if (label.Length > 25) {
                label = $"{label.Substring(0, 22)}...";
            }

            // Increase node weight so that text fits completely.
            double horizontalMargin = Math.Min(Math.Max(0.1, label.Length * 0.04), 1.0);

            var nodeName = CreateNodeWithMargins(node.Number, label, builder,
                                                 horizontalMargin, verticalMargin);
            nodeNameMap_[nodeName] = node;
        }

        private void CreateEdge(CallGraphNode node1, CallGraphNode node2, StringBuilder builder) {
            if (options_.UseSingleIncomingEdge) {
                if (!incomingEdgeNodes_.Add(node2)) {
                    return; // Node already has an edge.
                }
            }

            CreateEdge(node1.Number, node2.Number, builder);
        }

        public override Dictionary<string, TaggedObject> CreateNodeDataMap() {
            if (nodeNameMap_.Count > 0) {
                return nodeNameMap_;
            }

            var map = new Dictionary<string, TaggedObject>();

            foreach (var node in callGraph_.FunctionNodes) {
                map[GetNodeName((ulong)node.Number)] = node;
            }

            return map;
        }

        public override Dictionary<TaggedObject, List<TaggedObject>> CreateNodeDataGroupsMap() {
            return null;
        }
    }
}
