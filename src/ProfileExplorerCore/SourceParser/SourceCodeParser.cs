// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace ProfileExplorer.Core.SourceParser;

public enum SourceCodeLanguage {
  Cpp,
  CSharp,
  Rust
}

public class SourceCodeParser {
  [SuppressUnmanagedCodeSecurity]
  [DllImport("tree-sitter-cpp.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern IntPtr tree_sitter_cpp();

  [SuppressUnmanagedCodeSecurity]
  [DllImport("tree-sitter-c-sharp.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern IntPtr tree_sitter_c_sharp();

  [SuppressUnmanagedCodeSecurity]
  [DllImport("tree-sitter-rust.dll", CallingConvention = CallingConvention.Cdecl)]
  private static extern IntPtr tree_sitter_rust();

  private const int ParsingTimeoutSeconds = 3;
  private SourceCodeLanguage language_;

  public SourceCodeParser(SourceCodeLanguage language = SourceCodeLanguage.Cpp) {
    language_ = language;
  }

  private TSLanguage InitializeParserLanguage(SourceCodeLanguage language) {
    switch (language) {
      case SourceCodeLanguage.Cpp:    return new TSLanguage(tree_sitter_cpp());
      case SourceCodeLanguage.CSharp: return new TSLanguage(tree_sitter_c_sharp());
      case SourceCodeLanguage.Rust:   return new TSLanguage(tree_sitter_rust());
      default:                        throw new InvalidOperationException();
    }
  }

  public SourceSyntaxTree Parse(ReadOnlyMemory<char> text) {
    return Parse(text.ToString());
  }

  public SourceSyntaxTree Parse(string text) {
    SourceSyntaxTree tree = null;

    try {
      // Initialize parser.
      using var parser = new TSParser();
      parser.set_timeout_micros((ulong)TimeSpan.FromSeconds(ParsingTimeoutSeconds).TotalMicroseconds);

      using var language = InitializeParserLanguage(language_);
      parser.set_language(language);

      // Try to parse the text.
      using var parsedTree = parser.parse_string(null, text);

      if (parsedTree == null) {
        Trace.WriteLine($"Failed to parse the source code text using tree-sitter.");
        return null;
      }

      // Walked the parse tree and build a reduced syntax tree
      // of the main statement and expression nodes.
      tree = new SourceSyntaxTree();
      using var cursor = new TSCursor(parsedTree.root_node(), language);

      foreach (var node in WalkTreeNodes(cursor)) {
// #if DEBUG
//         int so = (int)cursor.current_node().start_offset();
//         int eo = (int)cursor.current_node().end_offset();
//         int sl = (int)cursor.current_node().start_point().row + 1;
//         int el = (int)cursor.current_node().end_point().row + 1;
//         var type = node.type();
//         var sym = node.symbol();
//         Trace.WriteLine($" - node type is {type}, startL {sl}, endL {el}");
// #endif

        bool accepted = true;
        var nodeKind = SourceSyntaxNodeKind.Other;

        switch (node.type()) {
          case "if_statement":
          case "if_expression": { // Rust
            nodeKind = SourceSyntaxNodeKind.If;
            break;
          }
          case "condition_clause": {
            nodeKind = SourceSyntaxNodeKind.Condition;
            break;
          }
          case "else_clause": {
            nodeKind = SourceSyntaxNodeKind.Else;
            break;
          }
          case "for_statement":
          case "for_range_loop":
          case "for_each_statement": // C#
          case "for_expression": { // Rust
            nodeKind = SourceSyntaxNodeKind.Loop;
            break;
          }
          case "while_statement":
          case "do_statement": {
            nodeKind = SourceSyntaxNodeKind.Loop;
            break;
          }
          case "switch_statement":
          case "match_expression": { // Rust
            nodeKind = SourceSyntaxNodeKind.Switch;
            break;
          }
          case "case_statement":
          case "switch_section": // C#
          case "match_arm": { // Rust
            nodeKind = SourceSyntaxNodeKind.SwitchCase;
            break;
          }
          case "compound_statement":
          case "block": { // C#
            nodeKind = SourceSyntaxNodeKind.Compound;
            break;
          }
          case "function_definition":
          case "method_declaration": // C#
          case "local_function_statement": // C#
          case "function_item": { // Rust
            nodeKind = SourceSyntaxNodeKind.Function;
            break;
          }
          case "call_expression":
          case "macro_invocation": { // Rust
            nodeKind = SourceSyntaxNodeKind.Call;
            break;
          }
          case "translation_unit":
          case "source_file": { // Rust
            nodeKind = SourceSyntaxNodeKind.Root;
            break;
          }
          default: {
            accepted = false;
            break;
          }
        }

        if (tree.RootNode == null || accepted) {
          var treeNode = tree.GetOrCreateNode(node.id.ToInt64());
          int startOffset = (int)cursor.current_node().start_offset();
          int endOffset = (int)cursor.current_node().end_offset();
          int startLine = (int)cursor.current_node().start_point().row + 1;
          int endLine = (int)cursor.current_node().end_point().row + 1;

          treeNode.Kind = nodeKind;
          treeNode.Start = new TextLocation(startOffset, startLine, 0);
          treeNode.End = new TextLocation(endOffset, endLine, 0);

          if (tree.RootNode == null) {
            tree.RootNode = treeNode;
          }

          else if (nodeKind == SourceSyntaxNodeKind.Function) {
            // Add all functions to the root node even if inide a class,
            // to make it easier to find a function by line.
            tree.RootNode.AddChild(treeNode);
          }
          else {
            // Because not all nodes are created in the reduce syntax tree,
            // look up for first ancestor node that is added and use it as parent.
            var parentNode = node.parent();

            while (parentNode.id != IntPtr.Zero) {
              var parentTreeNode = tree.GetNode(parentNode.id.ToInt64());

              if (parentTreeNode != null) {
                parentTreeNode.AddChild(treeNode);
                break;
              }

              parentNode = parentNode.parent();
            }
          }
        }
      }
    }
    catch (Exception ex) {
      Trace.WriteLine($"Exception parsing the source code text using tree-sitter: {ex.Message}");
    }

    return tree;
  }

  private IEnumerable<TSNode> WalkTreeNodes(TSCursor rootNode) {
    // Preorder traversal of the syntax tree, without using recursion.
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