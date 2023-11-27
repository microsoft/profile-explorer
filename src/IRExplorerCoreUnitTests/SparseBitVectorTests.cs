// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Linq;
using IRExplorerCore.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IRExplorerCoreTests;

[TestClass]
public class SparseBitVectorTests {
  [TestMethod]
  public void NodeSetGetBits() {
    var v = new SparseBitvector.Node();

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode; i++) {
      v[i] = i % 2 != 0;
    }

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode; i++) {
      Assert.AreEqual(v[i], i % 2 != 0);
    }

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode; i++) {
      v[i] = true;
    }

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode; i++) {
      Assert.IsTrue(v[i]);
    }

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode; i++) {
      v[i] = false;
    }

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode; i++) {
      Assert.IsFalse(v[i]);
    }
  }

  [TestMethod]
  public void NodeBitCount() {
    var v = new SparseBitvector.Node();
    Assert.AreEqual(v.SetBitCount, 0);
    Assert.IsFalse(v.HasBitsSet);

    v[0] = true;
    Assert.AreEqual(v.SetBitCount, 1);
    Assert.IsTrue(v.HasBitsSet);

    v[0] = true;
    v[3] = true;
    v[64] = true;
    v[91] = true;
    v[150] = true;
    v[201] = true;
    v[255] = true;
    Assert.AreEqual(v.SetBitCount, 7);
    Assert.IsTrue(v.HasBitsSet);

    v.ResetAllBits();
    Assert.AreEqual(v.SetBitCount, 0);
    Assert.IsFalse(v.HasBitsSet);

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode; i++) {
      Assert.IsFalse(v[i]);
    }
  }

  [TestMethod]
  public void SetGetBits() {
    var v = new SparseBitvector();

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      v[i] = true;
    }

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      Assert.AreEqual(v[i], true);
    }

    Assert.AreEqual(v.SetBitCount, SparseBitvector.Node.BitsPerNode * 10);
    Assert.IsTrue(v.HasBitsSet);

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      v[i] = false;
    }

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      Assert.AreEqual(v[i], false);
    }

    Assert.AreEqual(v.SetBitCount, 0);
    Assert.IsFalse(v.HasBitsSet);
  }

  [TestMethod]
  public void SetGetBitsAlternate() {
    var v = new SparseBitvector();

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      v[i] = i % 2 == 0;
    }

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      Assert.AreEqual(v[i], i % 2 == 0);
    }

    Assert.AreEqual(v.SetBitCount, SparseBitvector.Node.BitsPerNode * 10 / 2);
    Assert.IsTrue(v.HasBitsSet);

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      v[i] = i % 2 != 0;
    }

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      Assert.AreEqual(v[i], i % 2 != 0);
    }

    Assert.AreEqual(v.SetBitCount, SparseBitvector.Node.BitsPerNode * 10 / 2);
    Assert.IsTrue(v.HasBitsSet);
  }

  [TestMethod]
  public void SetGetBitsReverseAlternate() {
    var v = new SparseBitvector();
    int set = 0;

    for (int i = SparseBitvector.Node.BitsPerNode * 10; i >= 0; i--) {
      v[i] = i % 2 == 0;
      set += i % 2 == 0 ? 1 : 0;
    }

    for (int i = SparseBitvector.Node.BitsPerNode * 10; i >= 0; i--) {
      Assert.AreEqual(v[i], i % 2 == 0);
    }

    Assert.AreEqual(v.SetBitCount, set);
    Assert.IsTrue(v.HasBitsSet);
  }

  [TestMethod]
  public void SetGetBitsRandom() {
    int n = SparseBitvector.Node.BitsPerNode * 100;

    foreach (int seed in new[] {7, 13, 31, 51, 123}) {
      var r = new Random(seed);
      int[] indices = Enumerable.Range(0, n).OrderBy(i => r.Next()).ToArray();

      var v = new SparseBitvector();

      for (int i = 0; i < n; i++) {
        v[indices[i]] = true;
      }

      for (int i = 0; i < n; i++) {
        Assert.AreEqual(v[indices[i]], true);
      }

      for (int i = 0; i < n; i++) {
        Assert.AreEqual(v[i], true);
      }

      Assert.AreEqual(v.SetBitCount, n);
      Assert.IsTrue(v.HasBitsSet);

      indices = Enumerable.Range(0, n).OrderBy(i => r.Next()).ToArray();

      for (int i = 0; i < n; i++) {
        v[indices[i]] = false;
      }

      for (int i = 0; i < n; i++) {
        Assert.AreEqual(v[i], false);
      }

      for (int i = 0; i < n; i++) {
        Assert.AreEqual(v[indices[i]], false);
      }

      Assert.AreEqual(v.SetBitCount, 0);
      Assert.IsFalse(v.HasBitsSet);
    }
  }

  [TestMethod]
  public void SetGetBitsRandomAlternate() {
    int n = SparseBitvector.Node.BitsPerNode * 100;

    foreach (int seed in new[] {7, 13, 31, 51, 123}) {
      var r = new Random(seed);
      int[] indices = Enumerable.Range(0, n).OrderBy(i => r.Next()).ToArray();

      var v = new SparseBitvector();

      for (int i = 0; i < n; i++) {
        v[indices[i]] = i % 2 == 0;
      }

      for (int i = 0; i < n; i++) {
        Assert.AreEqual(v[indices[i]], i % 2 == 0);
      }
    }
  }

  [TestMethod]
  public void And() {
    var v = new SparseBitvector();
    var v2 = new SparseBitvector();
    var v3 = new SparseBitvector();

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      v[i] = i % 2 == 0;
      v2[i] = i % 2 == 0;
      v3[i] = i % 2 != 0;
    }

    v2.And(v);

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      Assert.AreEqual(v2[i], i % 2 == 0);
    }

    v3.And(v);

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i++) {
      Assert.AreEqual(v3[i], false);
    }
  }

  [TestMethod]
  public void And2() {
    var v = new SparseBitvector();
    var v2 = new SparseBitvector();
    var v3 = new SparseBitvector();

    for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i += 64) {
      SetFromInt(v, 0xABCDEFABCDEFABCD, i);
      SetFromInt(v2, 0xF0F0F0F0F0F0F0F0, i);
      SetFromInt(v3, 0xA0C0E0A0C0E0A0C0, i);
    }

    v.And(v2);
    Assert.AreEqual(v, v3);
  }

  [TestMethod]
  public void And3() {
    foreach (int step in new[] {2, 3, 4}) {
      var v = new SparseBitvector();
      var v2 = new SparseBitvector();
      var v3 = new SparseBitvector();

      for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i += 64) {
        SetFromInt(v, 0xABCDEFABCDEFABCD, i);
      }

      for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i += 64 * step) {
        SetFromInt(v2, 0xF0F0F0F0F0F0F0F0, i);
        SetFromInt(v3, 0xA0C0E0A0C0E0A0C0, i);
      }

      v.And(v2);
      Assert.AreEqual(v, v3);
    }

    foreach (int step in new[] {2, 3, 4}) {
      var v = new SparseBitvector();
      var v2 = new SparseBitvector();
      var v3 = new SparseBitvector();

      for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i += 64 * step) {
        SetFromInt(v, 0xABCDEFABCDEFABCD, i);
        SetFromInt(v3, 0xA0C0E0A0C0E0A0C0, i);
      }

      for (int i = 0; i < SparseBitvector.Node.BitsPerNode * 10; i += 64) {
        SetFromInt(v2, 0xF0F0F0F0F0F0F0F0, i);
      }

      v.And(v2);
      Assert.AreEqual(v, v3);
    }
  }

  private void SetFromInt(SparseBitvector bv, ulong value, int startIndex = 0) {
    for (int i = 0; i < 64; i++) {
      bv[startIndex + i] = (value & 1ul << i) != 0;
    }
  }
}
