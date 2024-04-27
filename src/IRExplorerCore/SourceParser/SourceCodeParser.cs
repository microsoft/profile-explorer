using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GitHub.TreeSitter;

namespace IRExplorerCore.SourceParser;

public class SourceCodeParser {
  [DllImport("tree-sitter-cpp.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern IntPtr tree_sitter_cpp();

  public SourceCodeParser() {

  }

  public SourceSyntaxTree Parse(string text) {
    using var parser = new TSParser();
    var language = new TSLanguage(tree_sitter_cpp());
    parser.set_language(language);

    using var parsedTree = parser.parse_string(null, text);

    if (parsedTree == null) {
      return null;
    }

    var tree = new SourceSyntaxTree();
    using var cursor = new TSCursor(parsedTree.root_node(), language);

    foreach (var node in WalkTreeNodes(cursor)) {

      {
        int so = (int)cursor.current_node().start_offset();
        int eo = (int)cursor.current_node().end_offset();
        int sl = (int)cursor.current_node().start_point().row + 1;
        int el = (int)cursor.current_node().end_point().row + 1;
        var type = node.type();
        var sym = node.symbol();
        Trace.WriteLine($"    node type is {type}, startL {sl}, endL {el}");
      }
      bool accepted = true;
      SourceSyntaxNodeKind nodeKind = SourceSyntaxNodeKind.Compound;

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
        int so = (int)cursor.current_node().start_offset();
        int eo = (int)cursor.current_node().end_offset();
        int sl = (int)cursor.current_node().start_point().row + 1;
        int el = (int)cursor.current_node().end_point().row + 1;

        treeNode.Kind = nodeKind;
        treeNode.Start = new TextLocation(so, sl, 0);
        treeNode.End = new TextLocation(eo, el, 0);
        treeNode.Value = text.Substring(so, eo - so);

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

    Trace.WriteLine("======================================\n");
    Trace.WriteLine(tree.Print());
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