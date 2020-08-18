// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerCore.GraphViz {
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

        public object Data { get; set; }
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

    public sealed class LayoutGraph {
        public LayoutGraph(GraphKind kind) {
            GraphKind = kind;
            Nodes = new List<Node>();
            Edges = new List<Edge>();
            DataNodeMap = new Dictionary<object, Node>();
        }

        public GraphKind GraphKind { get; set; }
        public List<Node> Nodes { get; set; }
        public List<Edge> Edges { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        //? TODO: Move below out so it's easy to discard them and free memory for large graphs
        public Dictionary<object, Node> DataNodeMap { get; set; }
        public Dictionary<object, List<object>> DataNodeGroupsMap { get; set; }
    }
}
