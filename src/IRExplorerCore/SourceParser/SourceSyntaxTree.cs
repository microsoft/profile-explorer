using System;
using System.Collections.Generic;

namespace IRExplorerCore.SourceParser;

public enum SourceSyntaxNodeKind {
  Function,
  IfElse,
  Loop,
  Switch,
  Compound,
  Call,
}

public class SourceSyntaxNode {
  public SourceSyntaxNode(SourceSyntaxNodeKind kind) {
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
}

public class SourceSyntaxTree {
  private SourceSyntaxNode RootNode { get; set; }

  public List<SourceSyntaxNode> FindNodes(int startLine, int endLine) {
    return null;
  }

  public List<(int StartOffset, int EndOffset)> FindBracePairs() {
    return null;
  }
}
