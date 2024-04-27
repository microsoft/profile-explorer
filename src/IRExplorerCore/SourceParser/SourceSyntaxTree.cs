using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.SourceParser;

public enum SourceSyntaxNodeKind {
  Root,
  Function,
  If,
  Else,
  Loop,
  Switch,
  Compound,
  Condition,
  Body,
  Call,
  Other,
}

public class SourceSyntaxNode {
  public SourceSyntaxNode(SourceSyntaxNodeKind kind = SourceSyntaxNodeKind.Other) {
    Kind = kind;
  }

  public SourceSyntaxNodeKind Kind { get; set; }
  public string Value { get; set; }
  public TextLocation Start { get; set; }
  public TextLocation End { get; set; }
  public List<SourceSyntaxNode> ChildNodes;

  public void AddChild(SourceSyntaxNode node) {
    ChildNodes ??= new();
    ChildNodes.Add(node);
  }

  public string GetText(ReadOnlyMemory<char> text) {
    return null;
  }

  public void WalkNodes(Func<SourceSyntaxNode, int, bool> action,
                        SourceSyntaxNodeKind kindFilter = SourceSyntaxNodeKind.Other) {
    WalkNodes(this, action, kindFilter);
  }

  private bool WalkNodes(SourceSyntaxNode node, Func<SourceSyntaxNode, int, bool> action,
                        SourceSyntaxNodeKind kindFilter = SourceSyntaxNodeKind.Other,
                        int depth = 0) {
    if (node.Kind == kindFilter || kindFilter == SourceSyntaxNodeKind.Other) {
      if (!action(node, depth)) {
        return false;
      }
    }

    if (node.ChildNodes != null) {
      foreach (var childNode in node.ChildNodes) {
        if (!WalkNodes(childNode, action, kindFilter, depth + 1)) {
          return false;
        }
      }
    }

    return true;
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
    if (RootNode == null) {
      return null;
    }

    foreach (var node in RootNode.ChildNodes) {
      if (node.Kind == SourceSyntaxNodeKind.Function &&
          Math.Abs(node.Start.Line - startLine) <= 2) {
        return node;
      }
    }

    return null;
  }

  public List<SourceSyntaxNode> FindNodes(int startLine, int endLine) {
    return null;
  }

  public List<(int StartOffset, int EndOffset)> FindBracePairs() {
    return null;
  }

  public string Print() {
    var sb = new StringBuilder();
    RootNode?.Print(sb, 0);
    return sb.ToString();
  }
}