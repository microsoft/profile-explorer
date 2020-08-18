// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;
using IRExplorerCore.Lexer;

namespace IRExplorerCore.GraphViz {
    public sealed class GraphvizReader {
        private static Dictionary<string, Keyword> keywordMap_ =
            new Dictionary<string, Keyword> {
                {"graph", Keyword.Graph},
                {"node", Keyword.Node},
                {"edge", Keyword.Edge},
                {"stop", Keyword.Stop}
            };

        private Dictionary<string, Node> nodeMap_;
        private Dictionary<string, IRElement> elementNameMap_;
        private Token current_;
        private LayoutGraph graph_;

        private GraphKind graphKind_;
        private Lexer.Lexer lexer_;
        private string sourceText_;

        public GraphvizReader(GraphKind kind, string text,
                              Dictionary<string, IRElement> elementNameMap) {
            graphKind_ = kind;
            elementNameMap_ = elementNameMap;
            sourceText_ = text;

            nodeMap_ = new Dictionary<string, Node>();
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

            // The Graphviz output doesn't seem to quote integers,
            // which also include negative values.
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
            graph_ = new LayoutGraph(graphKind_);

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
                    var node = ReadNode();

                    if (node != null) {
                        graph_.Nodes.Add(node);
                    }

                    SkipToLineStart();
                }

                while (ExpectAndSkipKeyword(Keyword.Edge)) {
                    var edge = ReadEdge();

                    if (edge != null) {
                        graph_.Edges.Add(edge);
                    }

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

            //? TODO: These commented-out values are currently not used anywhere.
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

            node.Label = label.ToString();
            //node.Style = style;
            //node.Shape = shape;
            //node.BorderColor = borderColor;
            //node.BackgroundColor = backgroundColor;

            nodeMap_[name.ToString()] = node;

            // Associate with IR objects.
            if (elementNameMap_.TryGetValue(name.ToString(), out var block)) {
                node.Element = block;
                graph_.ElementNodeMap.Add(block, node);
            }
            else {
                //Debug.Assert(false, "Could not find block");
                Debug.WriteLine("Failed 3");
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

            //edge.NodeNameFrom = fromNode;
            //edge.NodeNameTo = toNode;
            edge.LinePoints = new Tuple<double, double>[pointCont];

            for (int i = 0; i < pointCont; i++) {
                if (!ReadPoint(out double x, out double y)) {
                    Debug.WriteLine("Failed 3");
                    return null;
                }

                edge.LinePoints[i] = new Tuple<double, double>(x, y);
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

            //edge.Label = label;
            edge.LabelX = labelX;
            edge.LabelY = labelY;
            edge.Style = Edge.GetEdgeStyle(style);
            edge.Color = color;

            // Associate with IR objects.
            if (elementNameMap_.TryGetValue(fromNode.ToString(), out var fromBlock)) {
                var node = graph_.ElementNodeMap[fromBlock];
                edge.NodeFrom = node;
                node.OutEdges ??= new List<Edge>();
                node.OutEdges.Add(edge);
            }
            else {
                if (nodeMap_.TryGetValue(fromNode.ToString(), out var node)) {
                    edge.NodeFrom = node;
                    node.OutEdges ??= new List<Edge>();
                    node.OutEdges.Add(edge);
                }
                else {
                    Debug.WriteLine("Failed 6");
                }
            }

            if (elementNameMap_.TryGetValue(toNode.ToString(), out var toBlock)) {
                var node = graph_.ElementNodeMap[toBlock];
                edge.NodeTo = node;
                node.InEdges ??= new List<Edge>();
                node.InEdges.Add(edge);
            }
            else {
                if (nodeMap_.TryGetValue(toNode.ToString(), out var node)) {
                    edge.NodeTo = node;
                    node.InEdges ??= new List<Edge>();
                    node.InEdges.Add(edge);
                }
                else {
                    Debug.WriteLine("Failed 6");
                }
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