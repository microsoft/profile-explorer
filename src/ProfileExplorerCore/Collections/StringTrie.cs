// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Collections;

// A compact representation of a Trie<T> using only arrays, which makes it
// more cache friendly and much faster (>10x for large tries) than a simple trie
// that uses node objects for each letter and lists of successor nodes.
//
// A significant advantage vs. using a Dictionary<string, T> is that it can be queried
// with a Span<char>, which avoids creating a temporary string just for the query
// in code that uses spans, like the lexer/parsers.
public class StringTrie<T> {
  private List<Tuple<int, int>> nodes_; // first child index, count
  private List<Tuple<char, int>> childLetters_; // letter, child index
  private List<Optional<T>> terminalNodes_;
  private int lastNodeId_;
  private int lastChildId_;

  public StringTrie() {
    nodes_ = new List<Tuple<int, int>>();
    childLetters_ = new List<Tuple<char, int>>();
    terminalNodes_ = new List<Optional<T>>();
  }

  public StringTrie(Dictionary<string, T> values) : this() {
    Build(values);
  }

  public void Build(List<Tuple<string, T>> values) {
    // The words must be added in layers: first the first letter from all words,
    // then the second letter, and so on, until nu suffix part remains.
    // This algorithm needs the words to be sorted lexicographically.
    int layer = 0;
    bool changed = true;
    int rootNode = AddNode();

    values.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.Ordinal));

    while (changed) {
      changed = false;
      layer++;

      foreach (var pair in values) {
        if (layer <= pair.Item1.Length) {
          changed = true;
          int newNodeId = AddWord(pair.Item1, 0, layer, rootNode);

          if (layer == pair.Item1.Length) {
            // Mark as terminal if the entire word has been processed.
            SetTerminalNode(newNodeId, pair.Item2);
          }
        }
      }
    }
  }

  public void Build(Dictionary<string, T> values) {
    var list = new List<Tuple<string, T>>(values.Count);

    foreach (var pair in values) {
      list.Add(new Tuple<string, T>(pair.Key, pair.Value));
    }

    Build(list);
  }

  public bool TryGetValue(string value, out T outValue, bool caseInsensitive = false) {
    int nodeId = 0;

    for (int i = 0; i < value.Length; i++) {
      char letter = value[i];

      if (caseInsensitive) {
        letter = char.ToLowerInvariant(letter);
      }

      int childId = FindChildForLetter(nodeId, letter, caseInsensitive);

      if (childId == -1) {
        outValue = default(T);
        return false;
      }

      nodeId = childId;
    }

    return IsTerminalNode(nodeId, out outValue);
  }

  public bool TryGetValue(ReadOnlySpan<char> value, out T outValue, bool caseInsensitive = false) {
    int nodeId = 0;

    for (int i = 0; i < value.Length; i++) {
      char letter = value[i];

      if (caseInsensitive) {
        letter = char.ToLowerInvariant(letter);
      }

      int childId = FindChildForLetter(nodeId, letter, caseInsensitive);

      if (childId == -1) {
        outValue = default(T);
        return false;
      }

      nodeId = childId;
    }

    return IsTerminalNode(nodeId, out outValue);
  }

  public bool TryGetValue(ReadOnlyMemory<char> value, out T outValue, bool caseInsensitive = false) {
    return TryGetValue(value.Span, out outValue, caseInsensitive);
  }

  public bool Contains(string value, bool caseInsensitive = false) {
    return TryGetValue(value, out _, caseInsensitive);
  }

  public bool Contains(ReadOnlyMemory<char> value, bool caseInsensitive = false) {
    return TryGetValue(value, out _, caseInsensitive);
  }

  private int AddNode() {
    nodes_.Add(new Tuple<int, int>(-1, 0));
    return lastNodeId_++;
  }

  private void AddChildNode(int parentNodeId, char letter, int childNodeId) {
    childLetters_.Add(new Tuple<char, int>(letter, childNodeId));
    var parentChildInfo = nodes_[parentNodeId];

    if (parentChildInfo.Item1 == -1) {
      // First child being added.
      nodes_[parentNodeId] = new Tuple<int, int>(lastChildId_, 1);
    }
    else {
      // Increment number of children.
      nodes_[parentNodeId] = new Tuple<int, int>(parentChildInfo.Item1,
                                                 parentChildInfo.Item2 + 1);
    }

    lastChildId_++;
  }

  private void SetTerminalNode(int nodeId, T value) {
    while (terminalNodes_.Count <= nodeId) {
      terminalNodes_.Add(new Optional<T>());
    }

    terminalNodes_[nodeId] = value;
  }

  private int AddWord(string word, int position, int maxPosition, int nodeId) {
    if (position == maxPosition) {
      return nodeId;
    }

    char letter = word[position];
    int childNodeId = FindChildForLetter(nodeId, letter);

    if (childNodeId == -1) {
      // First time this letter is inserted as a child.
      childNodeId = AddNode();
      AddChildNode(nodeId, letter, childNodeId);
    }

    return AddWord(word, position + 1, maxPosition, childNodeId);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private int FindChildForLetter(int nodeId, char letter, bool caseInsensitive = false) {
    var childInfo = nodes_[nodeId];

    for (int i = 0; i < childInfo.Item2; i++) {
      var letterInfo = childLetters_[childInfo.Item1 + i];

      if (letterInfo.Item1 == letter) {
        return letterInfo.Item2;
      }

      if (caseInsensitive && char.ToLowerInvariant(letterInfo.Item1) == letter) {
        return letterInfo.Item2;
      }
    }

    return -1;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool IsTerminalNode(int nodeId, out T value) {
    if (terminalNodes_[nodeId].HasValue) {
      value = (T)terminalNodes_[nodeId];
      return true;
    }

    value = default(T);
    return false;
  }
}