// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph {
    public sealed class Node {
        //? Commented properties are currently not used.
        public ReadOnlyMemory<char> Name { get; set; }
        //public ReadOnlyMemory<char> Label { get; set; }
        public string Label { get; set; }
        //public ReadOnlyMemory<char> Style { get; set; }
        //public ReadOnlyMemory<char> Shape { get; set; }
        //public ReadOnlyMemory<char> BackgroundColor { get; set; }
        //public ReadOnlyMemory<char> BorderColor { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public TaggedObject Data { get; set; }
        public bool DataIsElement => Data is IRElement;
        public IRElement ElementData => Data as IRElement;
        public List<Edge> InEdges { get; set; }
        public List<Edge> OutEdges { get; set; }
        public object Tag { get; set; }
    }

    public sealed class Edge {
        public enum EdgeKind {
            Default,
            Dotted,
            Dashed
        }

        public Node NodeFrom { get; set; }
        public Node NodeTo { get; set; }

        //public ReadOnlyMemory<char> NodeNameFrom { get; set; }
        //public ReadOnlyMemory<char> NodeNameTo { get; set; }
        //public ReadOnlyMemory<char> Label { get; set; }
        public double LabelX { get; set; }
        public double LabelY { get; set; }
        public Tuple<double, double>[] LinePoints { get; set; }
        public EdgeKind Style { get; set; }
        public ReadOnlyMemory<char> Color { get; set; }

        public static EdgeKind GetEdgeStyle(ReadOnlyMemory<char> style) {
            if (style.Span.Equals("dotted", StringComparison.Ordinal)) {
                return EdgeKind.Dotted;
            }
            else if (style.Span.Equals("dashed", StringComparison.Ordinal)) {
                return EdgeKind.Dashed;
            }

            return EdgeKind.Default;
        }
    }

    public enum GraphKind {
        FlowGraph,
        DominatorTree,
        PostDominatorTree,
        ExpressionGraph,
        CallGraph
    }

    public sealed class Graph {
        public Graph(GraphKind kind) {
            Kind = kind;
            Nodes = new List<Node>();
            Edges = new List<Edge>();
            DataNodeMap = new Dictionary<TaggedObject, Node>();
        }

        public GraphKind Kind { get; set; }
        public object GraphOptions { get; set; }
        public List<Node> Nodes { get; set; }
        public List<Edge> Edges { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsEmpty => Nodes.Count == 0;

        //? TODO: Move below out so it's easy to discard them and free memory for large graphs
        public Dictionary<TaggedObject, Node> DataNodeMap { get; set; }
        public Dictionary<TaggedObject, List<TaggedObject>> DataNodeGroupsMap { get; set; }
    }
}
