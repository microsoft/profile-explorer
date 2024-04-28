﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GitHub.TreeSitter;

namespace IRExplorerCore.SourceParser;

public enum SourceCodeLanguage {
  Cpp,
  CSharp,
  Rust
}

public class SourceCodeParser {
  [DllImport("tree-sitter-cpp.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern IntPtr tree_sitter_cpp();

  [DllImport("tree-sitter-c-sharp.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern IntPtr tree_sitter_c_sharp();

  [DllImport("tree-sitter-rust.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern IntPtr tree_sitter_rust();

  private SourceCodeLanguage language_;

  public SourceCodeParser(SourceCodeLanguage language = SourceCodeLanguage.Cpp) {
    language_ = language;
  }

  private TSLanguage InitializeParserLanguage(SourceCodeLanguage language) {
    switch (language) {
      case SourceCodeLanguage.Cpp: return new TSLanguage(tree_sitter_cpp());
      case SourceCodeLanguage.CSharp: return new TSLanguage(tree_sitter_c_sharp());
      case SourceCodeLanguage.Rust: return new TSLanguage(tree_sitter_rust());
      default: throw new InvalidOperationException();
    }
  }

  public SourceSyntaxTree Parse(string text) {
    // Initialize parser.
    using var parser = new TSParser();
    using var language = InitializeParserLanguage(language_);
    parser.set_language(language);

    // Try to parse the text.
    using var parsedTree = parser.parse_string(null, text);

    if (parsedTree == null) {
      return null;
    }

    // Walked the parse tree and build a reduced syntax tree
    // of the main statement and expression nodes.
    var tree = new SourceSyntaxTree();
    using var cursor = new TSCursor(parsedTree.root_node(), language);

    foreach (var node in WalkTreeNodes(cursor)) {
#if DEBUG
        int so = (int)cursor.current_node().start_offset();
        int eo = (int)cursor.current_node().end_offset();
        int sl = (int)cursor.current_node().start_point().row + 1;
        int el = (int)cursor.current_node().end_point().row + 1;
        var type = node.type();
        var sym = node.symbol();
        Trace.WriteLine($"    node type is {type}, startL {sl}, endL {el}");
      }
#endif

      bool accepted = true;
      var nodeKind = SourceSyntaxNodeKind.Compound;

      switch (node.type()) {
        case "if_statement":
          nodeKind = SourceSyntaxNodeKind.If;
          break;
        case "condition_clause":
          nodeKind = SourceSyntaxNodeKind.Condition;
          break;
        case "else_clause":
          nodeKind = SourceSyntaxNodeKind.Else;
          break;
        case "for_statement":
        case "for_range_loop": {
          nodeKind = SourceSyntaxNodeKind.Loop;
          break;
        }
        case "while_statement":
        case "do_statement": {
          nodeKind = SourceSyntaxNodeKind.Loop;
          break;
        }
        case "compound_statement": {
          nodeKind = SourceSyntaxNodeKind.Compound;
          break;
        }
        case "function_definition": {
          nodeKind = SourceSyntaxNodeKind.Function;
          break;
        }
        case "call_expression": {
          nodeKind = SourceSyntaxNodeKind.Call;
          break;
        }
        case "translation_unit": {
          nodeKind = SourceSyntaxNodeKind.Root;
          break;
        }
        default: {
          accepted = false;
          break;
        }
      }

      if (accepted) {
        var treeNode = tree.GetOrCreateNode(node.id.ToInt64());
        int startOffset = (int)cursor.current_node().start_offset();
        int endOffset = (int)cursor.current_node().end_offset();
        int startLine = (int)cursor.current_node().start_point().row + 1;
        int endLine = (int)cursor.current_node().end_point().row + 1;

        treeNode.Kind = nodeKind;
        treeNode.Start = new TextLocation(startOffset, startLine, 0);
        treeNode.End = new TextLocation(endOffset, endLine, 0);
        treeNode.Value = text.Substring(startOffset, endOffset - startOffset);

        if (tree.RootNode == null) {
          tree.RootNode = treeNode;
        }

        var parentNode = node.parent();

        while (parentNode.id != IntPtr.Zero) {
          var parentTreeNode = tree.GetNode(parentNode.id.ToInt64());

          if (parentTreeNode != null) {
            parentTreeNode.AddChild(treeNode);

            if (nodeKind == SourceSyntaxNodeKind.Compound) {
              switch (parentTreeNode.Kind) {
                case SourceSyntaxNodeKind.If: {
                  treeNode.Kind = SourceSyntaxNodeKind.Body;
                  break;
                }
                case SourceSyntaxNodeKind.Loop: {
                  treeNode.Kind = SourceSyntaxNodeKind.Body;
                  break;
                }
              }
            }

            break;
          }

          parentNode = parentNode.parent();
        }
      }
    }

    return tree;
  }

  private IEnumerable<TSNode> WalkTreeNodes(TSCursor rootNode) {
    var cursor = rootNode;
    bool reachedRoot = false;

    while (!reachedRoot) {
      yield return cursor.current_node();

      if (cursor.current_node().child_count() > 0 &&
          cursor.goto_first_child()) {
        continue;
      }

      if (cursor.current_node().next_sibling().id != IntPtr.Zero &&
          cursor.goto_next_sibling()) {
        continue;
      }

      bool retracting = true;

      while (retracting) {
        if (!cursor.goto_parent()) {
          retracting = false;
          reachedRoot = true;
        }

        if (cursor.current_node().next_sibling().id != IntPtr.Zero &&
            cursor.goto_next_sibling()) {
          retracting = false;
        }
      }
    }
  }
}