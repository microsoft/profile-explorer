// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.UI;

namespace ProfileExplorerUITests;
using ProfileExplorer.UI.Utilities;

[TestClass]
public class CollectionExtensionMethodsTests {
  [TestMethod]
  public void AreEqualAreEqualTest() {
    var list1 = new List<int> { 1, 2, 3 };
    var list2 = new List<int> { 1, 2, 3 };
    var list3 = new List<int> { 1, 2, 4 };
    var list4 = new List<int> { 1, 2 };

    Assert.IsTrue(list1.AreEqual(list2));
    Assert.IsFalse(list1.AreEqual(list3));
    Assert.IsFalse(list1.AreEqual(list4));
  }

  [TestMethod]
  public void CloneDictionaryTest() {
    var dict1 = new Dictionary<int, string> {
      { 1, "one" },
      { 2, "two" },
      { 3, "three" }
    };
    var dict2 = dict1.CloneDictionary();
    Assert.IsTrue(dict1.AreEqual(dict2));
    Assert.AreNotSame(dict1, dict2);
  }

  [TestMethod]
  public void CloneHashSetTest() {
    var hashSet1 = new HashSet<int> { 1, 2, 3 };
    var hashSet2 = hashSet1.CloneHashSet();
    Assert.IsTrue(hashSet1.AreEqual(hashSet2));
    Assert.AreNotSame(hashSet1, hashSet2);
  }

  [TestMethod]
  public void DictionaryAreEqualTest() {
    var dict1 = new Dictionary<int, string> {
      { 1, "one" },
      { 2, "two" },
      { 3, "three" }
    };
    var dict2 = new Dictionary<int, string> {
      { 1, "one" },
      { 2, "two" },
      { 3, "three" }
    };
    var dict3 = new Dictionary<int, string> {
      { 1, "one" },
      { 2, "two" },
      { 3, "four" }
    };
    var dict4 = new Dictionary<int, string> {
      { 1, "one" },
      { 2, "two" }
    };

    Assert.IsTrue(dict1.AreEqual(dict2));
    Assert.IsFalse(dict1.AreEqual(dict3));
    Assert.IsFalse(dict1.AreEqual(dict4));
  }

  [TestMethod]
  public void HashSetAreEqualTest() {
    var hashSet1 = new HashSet<int> { 1, 2, 3 };
    var hashSet2 = new HashSet<int> { 1, 2, 3 };
    var hashSet3 = new HashSet<int> { 1, 2, 4 };
    var hashSet4 = new HashSet<int> { 1, 2 };

    Assert.IsTrue(hashSet1.AreEqual(hashSet2));
    Assert.IsFalse(hashSet1.AreEqual(hashSet3));
    Assert.IsFalse(hashSet1.AreEqual(hashSet4));
  }

  [TestMethod]
  public void AccumulateValueIntTest() {
    var dict = new Dictionary<int, int>();

    for (int i = 0; i < 1000 * 4; i++) {
      dict.AccumulateValue(i % 4, 2);
    }

    Assert.AreEqual(2000, dict[0]);
    Assert.AreEqual(2000, dict[1]);
    Assert.AreEqual(2000, dict[2]);
    Assert.AreEqual(2000, dict[3]);
  }

  [TestMethod]
  public void AccumulateValueLongTest() {
    var dict = new Dictionary<int, long>();

    for (int i = 0; i < 1000 * 4; i++) {
      dict.AccumulateValue(i % 4, 2);
    }

    Assert.AreEqual(2000, dict[0]);
    Assert.AreEqual(2000, dict[1]);
    Assert.AreEqual(2000, dict[2]);
    Assert.AreEqual(2000, dict[3]);
  }

  [TestMethod]
  public void AccumulateValueTimespanTest() {
    var dict = new Dictionary<int, TimeSpan>();

    for (int i = 0; i < 1000 * 4; i++) {
      dict.AccumulateValue(i % 4, TimeSpan.FromTicks(100));
    }

    Assert.AreEqual(TimeSpan.FromTicks(100 * 1000), dict[0]);
    Assert.AreEqual(TimeSpan.FromTicks(100 * 1000), dict[1]);
    Assert.AreEqual(TimeSpan.FromTicks(100 * 1000), dict[2]);
    Assert.AreEqual(TimeSpan.FromTicks(100 * 1000), dict[3]);
  }
}