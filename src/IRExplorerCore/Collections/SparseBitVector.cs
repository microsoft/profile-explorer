// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace IRExplorerCore.Collections;

public class SparseBitvector : IEquatable<SparseBitvector> {
  private Node startNode_;
  private Node lastNode_;

  public bool HasBitsSet {
    get {
      var node = startNode_;

      while (node != null) {
        if (node.HasBitsSet) return true;
        node = node.NextNode;
      }

      return false;
    }
  }

  public int SetBitCount {
    get {
      int sum = 0;
      var node = startNode_;

      while (node != null) {
        sum += node.SetBitCount;
        node = node.NextNode;
      }

      return sum;
    }
  }

  public bool this[int index] {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => TestBit(index);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => SetBitState(index, value);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetBit(int bit) {
    SetBitState(bit, true);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void ResetBit(int bit) {
    SetBitState(bit, false);
  }

  public void ResetAllBits() {
    startNode_ = null;
    lastNode_ = null;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void SetBitState(int bit, bool state) {
    var node = TryUseLastNode(bit);

    if (node != null) {
      node[bit] = state;
      return;
    }

    node = FindOrCreateNode(bit);
    node[bit] = state;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TestBit(int bit) {
    var node = TryUseLastNode(bit);

    if (node != null) {
      return node[bit];
    }

    Node insertionNode = null;
    node = TryFindNode(bit, ref insertionNode);
    return node != null && node[bit];
  }

  public void And(SparseBitvector other) {
    var a = startNode_;
    var b = other.startNode_;
    Node prevA = null;

    if (b == null) {
      ResetAllBits();
      return;
    }

    while (a != null && b != null) {
      if (a.StartBit == b.StartBit) {
        // Bits in both a and b.
        a.And(b);
        prevA = a;
        a = a.NextNode;
        b = b.NextNode;
      }
      else if (a.StartBit > b.StartBit) {
        // Bits in b but not in a.
        b = b.NextNode;
      }
      else {
        var nextNode = a.NextNode;
        RemoveNode(a, prevA);
        a = nextNode;
      }
    }

    // All the ranges that remain need to be removed (no ranges in 'other' match).
    while (a != null) {
      var nextNode = a.NextNode;
      RemoveNode(a, prevA);
      a = nextNode;
    }
  }

  public void ForEachSetBit(Func<int, bool> action) {
    var node = startNode_;

    while (node != null) {
      if (!node.ForEachSetBit(action)) {
        return;
      }

      node = node.NextNode;
    }
  }

  public override bool Equals(object? obj) {
    if (ReferenceEquals(null, obj)) return false;
    if (ReferenceEquals(this, obj)) return true;
    if (obj.GetType() != GetType()) return false;
    return Equals((SparseBitvector)obj);
  }

  public override int GetHashCode() {
    int hash = 0;
    var node = startNode_;

    while (node != null) {
      hash = HashCode.Combine(hash, node.GetHashCode());
    }

    return hash;
  }

  public bool Equals(SparseBitvector other) {
    var a = startNode_;
    var b = other.startNode_;

    while (a != null && b != null) {
      if (!a.Equals(b)) {
        return false;
      }

      a = a.NextNode;
      b = b.NextNode;
    }

    return a == null && b == null;
  }

  private Node FindOrCreateNode(int bit) {
    Node insertionNode = null;
    var node = TryFindNode(bit, ref insertionNode);

    if (node == null) {
      node = AllocateNode(bit);
      InsertNode(node, insertionNode);
    }

    return node;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private Node TryUseLastNode(int bit) {
    if (lastNode_ != null && lastNode_.IsBitInRange(bit)) {
      return lastNode_;
    }

    return null;
  }

  private Node TryFindNode(int bit, ref Node insertionNode) {
    var node = startNode_;

    if (lastNode_ != null && bit >= lastNode_.EndBit) {
      node = lastNode_;
    }

    Node prevNode = null;

    while (node != null) {
      if (node.IsBitInRange(bit)) {
        lastNode_ = node;
        return node;
      }

      if (node.StartBit > bit) {
        insertionNode = prevNode;
        lastNode_ = prevNode;
        return null;
      }

      prevNode = node;
      node = node.NextNode;
    }

    insertionNode = prevNode;
    return null;
  }

  private Node AllocateNode(int bit) {
    int startBit = bit / Node.BitsPerNode * Node.BitsPerNode;
    return new Node(startBit); //? TODO: pool
  }

  private void FreeNode(Node node) {
    node.NextNode = null;
  }

  private void InsertNode(Node node, Node prevNode) {
    if (prevNode != null) {
      node.NextNode = prevNode.NextNode;
      prevNode.NextNode = node;

      if (prevNode == lastNode_) {
        lastNode_ = node;
      }
    }
    else {
      node.NextNode = startNode_;
      startNode_ = node;
    }
  }

  private void RemoveNode(Node node, Node prevNode) {
    if (prevNode != null) {
      prevNode.NextNode = node.NextNode;
    }
    else {
      startNode_ = node.NextNode;
    }

    FreeNode(node);
  }

  public class Node : IEquatable<Node> {
    public const int BitsPerNode = 256;
    private const int ValuesPerNode = BitsPerNode / 64;
    private const int DivShift = 6;
    private const int RemMask = (1 << DivShift) - 1;
    private readonly ulong[] data_;

    public Node() {
      data_ = new ulong[ValuesPerNode];
    }

    public Node(int startBit) {
      data_ = new ulong[ValuesPerNode];
      StartBit = startBit;
    }

    public int StartBit { get; set; }
    public Node NextNode { get; set; }
    public int EndBit => StartBit + BitsPerNode;

    public int SetBitCount {
      get {
        int sum = 0;

        for (int i = 0; i < ValuesPerNode; i++) {
          sum += BitOperations.PopCount(data_[i]);
        }

        return sum;
      }
    }

    public bool HasBitsSet {
      get {
        var v = AsVector();
        var v0 = Vector<ulong>.Zero;

        for (int i = 0; i < v.Length; i++) {
          if (!v[i].Equals(v0)) {
            return true;
          }
        }

        return false;
      }
    }

    public static bool operator ==(Node? left, Node? right) {
      return Equals(left, right);
    }

    public static bool operator !=(Node? left, Node? right) {
      return !Equals(left, right);
    }

    public bool this[int index] {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get {
        index -= StartBit;
        return (data_[index >> DivShift] & 1ul << (index & RemMask)) != 0;
      }
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      set {
        index -= StartBit;

        if (value) {
          data_[index >> DivShift] |= 1ul << (index & RemMask);
        }
        else {
          data_[index >> DivShift] &= ~(1ul << (index & RemMask));
        }
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void And(Node other) {
      var v1 = AsVector();
      var v2 = other.AsVector();

      for (int i = 0; i < v1.Length; i++) {
        v1[i] &= v2[i];
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Or(Node other) {
      var v1 = AsVector();
      var v2 = other.AsVector();

      for (int i = 0; i < v1.Length; i++) {
        v1[i] |= v2[i];
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Xor(Node other) {
      var v1 = AsVector();
      var v2 = other.AsVector();

      for (int i = 0; i < v1.Length; i++) {
        v1[i] ^= v2[i];
      }
    }

    public void ResetAllBits() {
      var v = AsVector();

      for (int i = 0; i < v.Length; i++) {
        v[i] ^= v[i];
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsBitInRange(int bit) {
      return bit >= StartBit && bit < StartBit + BitsPerNode;
    }

    public bool ForEachSetBit(Func<int, bool> action) {
      for (int i = StartBit; i < EndBit; i++) {
        if (this[i] && !action(i)) {
          return false;
        }
      }

      return true;
    }

    public override bool Equals(object obj) {
      return Equals((Node)obj);
    }

    public override int GetHashCode() {
      return data_.GetHashCode();
    }

    public override string ToString() {
      var sb = new StringBuilder();

      foreach (ulong value in data_) {
        for (int i = 0; i < 64; i++) {
          if ((value & 1ul << i) != 0) {
            sb.Append(1);
          }
          else sb.Append(0);
        }

        sb.Append(' ');
      }

      return sb.ToString();
    }

    public bool Equals(Node other) {
      return StartBit == other.StartBit &&
             data_.SequenceEqual(other.data_);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<Vector<ulong>> AsVector() {
      return MemoryMarshal.Cast<ulong, Vector<ulong>>(data_);
    }
  }
}
