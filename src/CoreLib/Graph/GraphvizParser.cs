// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using CoreLib.Graph;
using CoreLib.IR;
using CoreLib.Lexer;

namespace CoreLib.GraphViz {
    public sealed class Node {
        public ReadOnlyMemory<char> Name { get; set; }
        public ReadOnlyMemory<char> Label { get; set; }
        public ReadOnlyMemory<char> Style { get; set; }
        public ReadOnlyMemory<char> Shape { get; set; }
        public ReadOnlyMemory<char> BackgroundColor { get; set; }
        public ReadOnlyMemory<char> BorderColor { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public IRElement Element { get; set; }
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

        public Edge() {
            LinePoints = new List<Tuple<double, double>>();
        }

        public Node NodeFrom { get; set; }
        public Node NodeTo { get; set; }

        public ReadOnlyMemory<char> NodeNameFrom { get; set; }
        public ReadOnlyMemory<char> NodeNameTo { get; set; }
        public ReadOnlyMemory<char> Label { get; set; }
        public double LabelX { get; set; }
        public double LabelY { get; set; }
        public List<Tuple<double, double>> LinePoints { get; set; }
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
            BlockNodeMap = new Dictionary<IRElement, Node>();
        }

        public GraphKind GraphKind { get; set; }
        public List<Node> Nodes { get; set; }
        public List<Edge> Edges { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public Dictionary<IRElement, Node> BlockNodeMap { get; set; }
        public Dictionary<string, IRElement> BlockNameMap { get; set; }
        public Dictionary<IRElement, List<IRElement>> BlockNodeGroupsMap { get; set; }
        public string SourceText { get; set; }
    }

    public sealed class GraphvizReader {
        private static Dictionary<string, Keyword> keywordMap_ =
            new Dictionary<string, Keyword> {
                {"graph", Keyword.Graph},
                {"node", Keyword.Node},
                {"edge", Keyword.Edge},
                {"stop", Keyword.Stop}
            };
        private Dictionary<string, IRElement> blockNameMap_;
        private Token current_;
        private LayoutGraph graph_;

        private GraphKind graphKind_;
        private Lexer.Lexer lexer_;
        private string sourceText_;

        public GraphvizReader(GraphKind kind, string text,
                              Dictionary<string, IRElement> blockNameMap) {
            graphKind_ = kind;
            blockNameMap_ = blockNameMap;
            sourceText_ = text;
            lexer_ = new Lexer.Lexer(text);
            current_ = lexer_.NextToken();
        }

        private bool IsToken(TokenKind kind) {
            return current_.Kind == kind;
        }

        private Keyword TokenKeyword() {
            if (current_.IsIdentifier()) {
                if (keywordMap_.TryGetValue(TokenString(), out var keyword)) {
                    return keyword;
                }
            }

            return Keyword.None;
        }

        private string TokenString() {
            return current_.Data.ToString();
        }

        private ReadOnlySpan<char> TokenStringData() {
            return current_.Data.Span;
        }

        private ReadOnlyMemory<char> TokenData() {
            return current_.Data;
        }

        private bool ReadTokenIntNumber(out int value) {
            bool result = int.TryParse(TokenStringData(), out value);

            if (result) {
                SkipToken();
            }

            return result;
        }

        private bool ReadFloatNumber(out double value) {
            bool isNegated = false;

            if (IsToken(TokenKind.Minus)) {
                SkipToken();
                isNegated = true;
            }

            bool result = double.TryParse(TokenStringData(), out value);

            if (result) {
                SkipToken();
            }

            unchecked {
                value = isNegated ? -value : value;
            }

            return result;
        }

        private bool ReadString(out ReadOnlyMemory<char> value) {
            if (current_.IsIdentifier() || current_.IsString()) {
                value = TokenData();
                SkipToken();
                return true;
            }

            value = default;
            return false;
        }

        private bool ReadLabel(out ReadOnlyMemory<char> value) {
            if (current_.IsIdentifier() || current_.IsString()) {
                value = TokenData();
                SkipToken();
                return true;
            }

            // The Graphviz output doesn't seem to quite integers at least,
            // which also includes negative values.
            if (IsToken(TokenKind.Minus)) {
                SkipToken();
            }

            if (current_.IsNumber()) {
                value = TokenData();
                SkipToken();
                return true;
            }

            value = default;
            return false;
        }

        private bool ReadPoint(out double x, out double y) {
            if (ReadFloatNumber(out x) && ReadFloatNumber(out y)) {
                y = graph_.Height - y;
                return true;
            }

            x = y = 0;
            return false;
        }

        private bool IsString() {
            return current_.IsIdentifier() || current_.IsString();
        }

        private bool NextTokenIs(TokenKind kind) {
            return lexer_.PeekToken().Kind == kind;
        }

        private void SkipToken() {
            current_ = lexer_.NextToken();
        }

        private bool ExpectAndSkipKeyword(Keyword keyword) {
            if (TokenKeyword() == keyword) {
                SkipToken();
                return true;
            }

            return false;
        }

        private bool IsEOF() {
            return current_.IsEOF();
        }

        private void SkipToLineEnd() {
            while (!current_.IsLineEnd() && !current_.IsEOF()) {
                SkipToken();
            }
        }

        private void SkipToLineStart() {
            SkipToLineEnd();
            SkipToken();
        }

        public LayoutGraph ReadGraph() {
            graph_ = new LayoutGraph(graphKind_) {
                SourceText = sourceText_
            };

            if (!ExpectAndSkipKeyword(Keyword.Graph)) {
                return null;
            }

            SkipToken(); // Ignored.

            if (!ReadFloatNumber(out double width) || !ReadFloatNumber(out double height)) {
                return null;
            }

            graph_.Width = width;
            graph_.Height = height;

            while (!IsEOF()) {
                SkipToLineStart();

                while (ExpectAndSkipKeyword(Keyword.Node)) {
                    graph_.Nodes.Add(ReadNode());
                    SkipToLineStart();
                }

                while (ExpectAndSkipKeyword(Keyword.Edge)) {
                    graph_.Edges.Add(ReadEdge());
                    SkipToLineStart();
                }

                if (ExpectAndSkipKeyword(Keyword.Stop)) {
                    break;
                }
            }

            return graph_;
        }

        private Node ReadNode() {
            var node = new Node();

            if (!ReadString(out var name) ||
                !ReadPoint(out double x, out double y) ||
                !ReadFloatNumber(out double width) ||
                !ReadFloatNumber(out double height)) {
                Debug.WriteLine("Failed 1");
                return null;
            }

            node.Name = name;
            node.CenterX = x;
            node.CenterY = y;
            node.Width = width;
            node.Height = height;

            if (!ReadLabel(out var label) ||
                !ReadString(out var style) ||
                !ReadString(out var shape) ||
                !ReadString(out var borderColor) ||
                !ReadString(out var backgroundColor)) {
                Debug.WriteLine("Failed 2");
                return null;
            }

            node.Label = label;
            node.Style = style;
            node.Shape = shape;
            node.BorderColor = borderColor;
            node.BackgroundColor = backgroundColor;

            // Associate with IR objects.
            if (blockNameMap_.TryGetValue(name.ToString(), out var block)) {
                node.Element = block;
                graph_.BlockNodeMap.Add(block, node);
            }
            else {
                Debug.Assert(false, "Could not find block");
            }

            return node;
        }

        private Edge ReadEdge() {
            var edge = new Edge();

            if (!ReadString(out var fromNode) ||
                !ReadString(out var toNode) ||
                !ReadTokenIntNumber(out int pointCont)) {
                return null;
            }

            edge.NodeNameFrom = fromNode;
            edge.NodeNameTo = toNode;

            for (int i = 0; i < pointCont; i++) {
                if (!ReadPoint(out double x, out double y)) {
                    Debug.WriteLine("Failed 3");
                    return null;
                }

                edge.LinePoints.Add(new Tuple<double, double>(x, y));
            }

            ReadOnlyMemory<char> label = default;
            double labelX = 0;
            double labelY = 0;

            if (IsString() && NextTokenIs(TokenKind.Number)) {
                // Edge has a label.
                if (!ReadString(out label) || !ReadPoint(out labelX, out labelY)) {
                    Debug.WriteLine("Failed 4");
                    return null;
                }
            }

            if (!ReadString(out var style) || !ReadString(out var color)) {
                Debug.WriteLine("Failed 5");
                return null;
            }

            edge.Label = label;
            edge.LabelX = labelX;
            edge.LabelY = labelY;
            edge.Style = Edge.GetEdgeStyle(style);
            edge.Color = color;

            // Associate with IR objects.
            if (blockNameMap_.TryGetValue(fromNode.ToString(), out var fromBlock)) {
                var node = graph_.BlockNodeMap[fromBlock];
                edge.NodeFrom = node;
                node.OutEdges ??= new List<Edge>();
                node.OutEdges.Add(edge);
            }
            else {
                //Debug.Assert(false, "Could not find from block");
            }

            if (blockNameMap_.TryGetValue(toNode.ToString(), out var toBlock)) {
                var node = graph_.BlockNodeMap[toBlock];
                edge.NodeTo = node;
                node.InEdges ??= new List<Edge>();
                node.InEdges.Add(edge);
            }
            else {
                //Debug.Assert(false, "Could not find to block");
            }

            return edge;
        }

        private enum Keyword {
            Graph,
            Node,
            Edge,
            Stop,
            None
        }
    }
}
