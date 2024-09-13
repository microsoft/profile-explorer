// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using ProfileExplorer.Core.Collections;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Lexer;

namespace ProfileExplorer.Core.Graph;

public sealed class GraphvizReader {
  private static readonly Dictionary<string, Keyword> keywordMap_ =
    new() {
      {"graph", Keyword.Graph},
      {"node", Keyword.Node},
      {"edge", Keyword.Edge},
      {"stop", Keyword.Stop}
    };
  private static readonly StringTrie<Keyword> keywordTrie_ = new(keywordMap_);
  private readonly Dictionary<string, Node> nodeMap_;
  private readonly Dictionary<string, TaggedObject> dataNameMap_;
  private Token current_;
  private Graph graph_;
  private GraphKind graphKind_;
  private Lexer.Lexer lexer_;

  public GraphvizReader(GraphKind kind, string text,
                        Dictionary<string, TaggedObject> dataNameMap) {
    graphKind_ = kind;
    dataNameMap_ = dataNameMap;

    nodeMap_ = new Dictionary<string, Node>();
    lexer_ = new Lexer.Lexer();
    lexer_.Initialize(text);
    current_ = lexer_.NextToken();
  }

  public Graph ReadGraph() {
    graph_ = new Graph(graphKind_);

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

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool IsToken(TokenKind kind) {
    return current_.Kind == kind;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private Keyword TokenKeyword() {
    if (current_.IsIdentifier() &&
        keywordTrie_.TryGetValue(TokenData(), out var keyword)) {
      return keyword;
    }

    return Keyword.None;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private ReadOnlySpan<char> TokenStringData() {
    return current_.Data.Span;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private ReadOnlyMemory<char> TokenData() {
    return current_.Data;
  }

  private bool ReadTokenIntNumber(out int value) {
    bool result = int.TryParse(TokenStringData(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    if (result) {
      SkipToken();
    }

    return result;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool ReadFloatNumber(out double value) {
    bool isNegated = false;

    if (IsToken(TokenKind.Minus)) {
      SkipToken();
      isNegated = true;
    }

    bool result = double.TryParse(TokenStringData(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);

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

    value = default(ReadOnlyMemory<char>);
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

    value = default(ReadOnlyMemory<char>);
    return false;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool ReadPoint(out double x, out double y) {
    if (ReadFloatNumber(out x) && ReadFloatNumber(out y)) {
      y = graph_.Height - y;
      return true;
    }

    x = y = 0;
    return false;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool IsString() {
    return current_.IsIdentifier() || current_.IsString();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool NextTokenIs(TokenKind kind) {
    return lexer_.PeekToken().Kind == kind;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private void SkipToken() {
    current_ = lexer_.NextToken();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool ExpectAndSkipKeyword(Keyword keyword) {
    if (TokenKeyword() == keyword) {
      SkipToken();
      return true;
    }

    return false;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

  private Node ReadNode() {
    var node = new Node();

    if (!ReadString(out var name) ||
        !ReadPoint(out double x, out double y) ||
        !ReadFloatNumber(out double width) ||
        !ReadFloatNumber(out double height)) {
      Trace.TraceError("Failed to parse Graphviz output node");
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
      Trace.TraceError("Failed to parse Graphviz output node properties");
      return null;
    }

    node.Label = label.ToString();
    nodeMap_[name.ToString()] = node;

    //? TODO: These commented-out values are currently not used anywhere.
    //node.Style = style;
    //node.Shape = shape;
    //node.BorderColor = borderColor;
    //node.BackgroundColor = backgroundColor;

    // Associate with IR objects.
    if (dataNameMap_.TryGetValue(name.ToString(), out var data)) {
      node.Data = data;
      graph_.DataNodeMap.Add(data, node);
    }
    else {
      Trace.TraceError($"Could not find Graphviz output block {name}");
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
        return null;
      }
    }

    if (!ReadString(out var style) || !ReadString(out var color)) {
      return null;
    }

    //edge.Label = label;
    edge.LabelX = labelX;
    edge.LabelY = labelY;
    edge.Style = Edge.GetEdgeStyle(style);
    edge.Color = color;

    // Associate with IR objects.
    if (dataNameMap_.TryGetValue(fromNode.ToString(), out var fromBlock)) {
      var node = graph_.DataNodeMap[fromBlock];
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
        Debug.Assert(false, $"Could not find block {fromNode}");
        Trace.TraceError($"Could not find Graphviz output block {fromNode}");
      }
    }

    if (dataNameMap_.TryGetValue(toNode.ToString(), out var toBlock)) {
      var node = graph_.DataNodeMap[toBlock];
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
        Debug.Assert(false, $"Could not find block {fromNode}");
        Trace.TraceError($"Could not find Graphviz output block {fromNode}");
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