using System;
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;
using IRExplorerCore.RawIRModel;
using FunctionIR = IRExplorerCore.IR.FunctionIR;

namespace IRExplorerCore.Graph;

public sealed class RawIRGraphPrinter : GraphVizPrinter {
    private IRExplorerCore.RawIRModel.Graph graph_;
    private Dictionary<IRExplorerCore.RawIRModel.GraphNode, IRElement> nodeElementMap_;
    private Dictionary<IRExplorerCore.RawIRModel.GraphEdge, IRElement> edgeElementMap_;
    private Dictionary<string, TaggedObject> nodeDataMap_;
    private Dictionary<(string, string), TaggedObject> edgeDataMap_;
    private Dictionary<long, string> nodeNameMap_;
    private StringBuilder builder_;
    private FunctionIR func_;

    public RawIRGraphPrinter(IRExplorerCore.RawIRModel.Graph graph, FunctionIR func,
        Dictionary<IRExplorerCore.RawIRModel.GraphNode, IRElement> nodeElementMap,
        Dictionary<IRExplorerCore.RawIRModel.GraphEdge, IRElement> edgeElementMap,
        GraphPrinterNameProvider nameProvider) : base(nameProvider) {
        graph_ = graph;
        func_ = func;
        nodeElementMap_ = nodeElementMap;
        edgeElementMap_ = edgeElementMap;
        nodeDataMap_ = new Dictionary<string, TaggedObject>();
        edgeDataMap_ = new Dictionary<(string, string), TaggedObject>();
        nodeNameMap_ = new Dictionary<long, string>();
    }

    protected override void PrintGraph(StringBuilder builder) {
        builder_ = builder;
        PrintNodes();
        PrintEdges();
    }

    public override Dictionary<string, TaggedObject> CreateNodeDataMap() {
        return nodeDataMap_;
    }

    public override Dictionary<(string, string), TaggedObject> CreateEdgeDataMap() {
        return edgeDataMap_;
    }

    private void PrintEdges() {
        foreach (var node in graph_.Nodes) {
            foreach (var edge in node.Edges) {
                if (!string.IsNullOrEmpty(edge.Operation)) {
                    if (edge.Label == "BackEdge") {
                        CreateEdgeWithLabelAndStyle((ulong)edge.FromNodeId, (ulong)edge.ToNodeId,
                            ShortenString(edge.Operation, 20), "dashed", builder_);
                    }
                    else {
                        CreateEdgeWithLabel((ulong)edge.FromNodeId, (ulong)edge.ToNodeId, ShortenString(edge.Operation, 20), builder_);
                    }
                }
                else {
                    if (edge.Label == "BackEdge") {
                        CreateEdgeWithStyle((ulong)edge.FromNodeId, (ulong)edge.ToNodeId, "dashed", builder_);
                    }
                    else {
                        CreateEdge((ulong)edge.FromNodeId, (ulong)edge.ToNodeId, builder_);
                    }
                }

                if (edgeElementMap_.TryGetValue(edge, out var element)) {
                    edgeDataMap_[(nodeNameMap_[edge.FromNodeId], nodeNameMap_[edge.ToNodeId])] = element;
                }
            }
        }
    }

    private void PrintNodes() {
        foreach (var node in graph_.Nodes) {
            PrintNode(node);
        }
    }

    private void PrintNode(IRExplorerCore.RawIRModel.GraphNode node) {
        string label = "";

        if (!string.IsNullOrEmpty(node.Operation)) {
            label = $"{ShortenString(node.Label, 30)}\\n{ShortenString(node.Operation, 30)}";
        }
        else {
            label = $"{node.Label.ToString()}\\nfunc.entry";
        }

        bool isMultiline = label.Contains("\\n");
        double verticalMargin = isMultiline ? 0.12 : 0.06;
        double horizontalMargin = Math.Min(Math.Max(0.1, label.Length * 0.02), 2.0);

        var nodeName = CreateNodeWithMargins((ulong)node.Id, label, builder_, horizontalMargin, verticalMargin);
        nodeNameMap_[node.Id] = nodeName;

        if (nodeElementMap_.TryGetValue(node, out var element)) {
            nodeDataMap_[nodeName] = element;
        }
    }

    private string ShortenString(string str, int maxLength) {
        if (str.Length > maxLength) {
            int colonIndex = str.LastIndexOf(':');

            if (colonIndex != -1) {
                return ShortenString(str.Substring(0, colonIndex).Trim(), maxLength);
            }

            return str.Substring(0, maxLength);
        }

        return str;
    }
}