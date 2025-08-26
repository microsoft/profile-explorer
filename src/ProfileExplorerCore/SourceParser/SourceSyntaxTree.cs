// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text;

namespace ProfileExplorerCore.SourceParser;

public enum SourceSyntaxNodeKind {
  Root,
  Function,
  If,
  Else,
  ElseIf,
  Loop,
  Switch,
  SwitchCase,
  Compound,
  Condition,
  Call,
  Other
}

public class SourceSyntaxNode {
  public SourceSyntaxNode(SourceSyntaxNodeKind kind = SourceSyntaxNodeKind.Other) {
    Kind = kind;
  }

  public SourceSyntaxNodeKind Kind { get; set; }
  public SourceSyntaxNode ParentNode { get; set; }
  public List<SourceSyntaxNode> ChildNodes { get; set; }
  public TextLocation Start { get; set; }
  public TextLocation End { get; set; }
  public object Tag { get; set; }
  public int Length => End.Offset - Start.Offset;
  public bool SpansMultipleLines => End.Line != Start.Line;
  public bool HasChildren => ChildNodes is {Count: > 0};

  public void AddChild(SourceSyntaxNode node) {
    ChildNodes ??= new List<SourceSyntaxNode>();
    ChildNodes.Add(node);
    node.ParentNode = this;
  }

  public SourceSyntaxNode GetChildOfKind(SourceSyntaxNodeKind kind) {
    if (ChildNodes != null) {
      foreach (var child in ChildNodes) {
        if (child.Kind == kind) {
          return child;
        }
      }
    }

    return null;
  }

  public string GetText(ReadOnlyMemory<char> text) {
    if (text.Length > 0 &&
        End.Offset < text.Length) {
      return text.Slice(Start.Offset, Length).ToString();
    }

    return null;
  }

  public void WalkNodes(Func<SourceSyntaxNode, int, bool> action,
                        SourceSyntaxNodeKind kindFilter = SourceSyntaxNodeKind.Other) {
    WalkNodes(this, action, kindFilter);
  }

  private bool WalkNodes(SourceSyntaxNode node, Func<SourceSyntaxNode, int, bool> action,
                         SourceSyntaxNodeKind kindFilter = SourceSyntaxNodeKind.Other,
                         int depth = 0) {
    // Do a pre-order traversal of the tree.
    if (node.Kind == kindFilter || kindFilter == SourceSyntaxNodeKind.Other) {
      if (!action(node, depth)) {
        return false;
      }
    }

    if (node.HasChildren) {
      foreach (var childNode in node.ChildNodes) {
        if (!WalkNodes(childNode, action, kindFilter, depth + 1)) {
          return false;
        }
      }
    }

    return true;
  }

  public string Print() {
    var sb = new StringBuilder();
    Print(sb, 0);
    return sb.ToString();
  }

  public void Print(StringBuilder sb, int level) {
    sb.Append(new string('\t', level));
    sb.AppendLine($"Kind: {Kind}, Start: {Start}, End: {End}");

    if (ChildNodes != null) {
      foreach (var child in ChildNodes) {
        child.Print(sb, level + 1);
      }
    }
  }
}

public class SourceSyntaxTree {
  private Dictionary<long, SourceSyntaxNode> nodeMap_;

  public SourceSyntaxTree() {
    nodeMap_ = new Dictionary<long, SourceSyntaxNode>();
  }

  public SourceSyntaxNode RootNode { get; set; }

  public SourceSyntaxNode GetOrCreateNode(long id) {
    if (!nodeMap_.TryGetValue(id, out var node)) {
      node = new SourceSyntaxNode();
      nodeMap_[id] = node;
    }

    return node;
  }

  public SourceSyntaxNode GetNode(long id) {
    return nodeMap_.GetValueOrDefault(id);
  }

  public SourceSyntaxNode FindFunctionNode(int startLine) {
    if (RootNode is not {HasChildren: true}) {
      return null;
    }

    foreach (var node in RootNode.ChildNodes) {
      if (node.Kind == SourceSyntaxNodeKind.Function) {
        if (Math.Abs(node.Start.Line - startLine) <= 1) {
          return node;
        }

        // Check the compound node of the function,
        // the start line in debug info is usually this line.
        var childNode = node.GetChildOfKind(SourceSyntaxNodeKind.Compound);

        if (childNode != null && Math.Abs(childNode.Start.Line - startLine) <= 1) {
          return node;
        }
      }
    }

    return null;
  }

  public string Print() {
    var sb = new StringBuilder();
    RootNode?.Print(sb, 0);
    return sb.ToString();
  }
}