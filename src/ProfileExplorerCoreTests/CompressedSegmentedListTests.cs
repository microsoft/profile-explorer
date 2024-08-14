// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;
using ProfileExplorer.Core.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProfileExplorerCoreTests;

public struct TestObject {
  public int a, b, c, d;
  public static int Counter;

  public static TestObject Create() {
    var result = new TestObject {
      a = Counter,
      b = Counter + 1,
      c = Counter + 2,
      d = Counter + 3
    };

    Counter += 4;
    return result;
  }
}

public struct TestGuidObject {
  public Guid a, b;

  public static TestGuidObject Create() {
    var result = new TestGuidObject {
      a = Guid.NewGuid(),
      b = Guid.NewGuid()
    };
    return result;
  }
}

[TestClass]
public class CompressedSegmentedListTests {
  [TestMethod]
  public void TestAdd() {
    var list = new CompressedSegmentedList<TestObject>();
    int count = 100000;
    TestObject.Counter = 0;

    for (int i = 0; i < count; i++) {
      list.Add(TestObject.Create());
    }

    Assert.AreEqual(list.Count, count);
    int counter = 0;

    for (int i = 0; i < 10000; i++) {
      var item = list[i];
      Assert.AreEqual(item.a, counter);
      Assert.AreEqual(item.b, counter + 1);
      Assert.AreEqual(item.c, counter + 2);
      Assert.AreEqual(item.d, counter + 3);
      counter += 4;
    }
  }

  [TestMethod]
  public void TestEnumerator() {
    var list = new CompressedSegmentedList<TestObject>();
    int count = 100000;
    TestObject.Counter = 0;

    for (int i = 0; i < count; i++) {
      list.Add(TestObject.Create());
    }

    int counter = 0;

    foreach (var item in list) {
      Assert.AreEqual(item.a, counter);
      Assert.AreEqual(item.b, counter + 1);
      Assert.AreEqual(item.c, counter + 2);
      Assert.AreEqual(item.d, counter + 3);
      counter += 4;
    }
  }

  [TestMethod]
  public void TestContains() {
    var list = new CompressedSegmentedList<TestObject>();
    int count = 10000;
    TestObject.Counter = 0;

    for (int i = 0; i < count; i++) {
      list.Add(TestObject.Create());
    }

    TestObject.Counter = 0;

    for (int i = 0; i < count; i++) {
      Assert.IsTrue(list.Contains(TestObject.Create()));
    }
  }

  [TestMethod]
  public void TestIndexOf() {
    var list = new CompressedSegmentedList<TestObject>();
    int count = 10000;
    TestObject.Counter = 0;

    for (int i = 0; i < count; i++) {
      list.Add(TestObject.Create());
    }

    TestObject.Counter = 0;

    for (int i = 0; i < count; i++) {
      Assert.AreEqual(i, list.IndexOf(TestObject.Create()));
    }
  }

  [TestMethod]
  public void TestHuge() {
    var list = new CompressedSegmentedList<TestObject>();
    int count = 1000000;
    TestObject.Counter = 0;

    for (int i = 0; i < count; i++) {
      list.Add(TestObject.Create());
    }

    int counter = 0;

    foreach (var item in list) {
      Assert.AreEqual(item.a, counter);
      Assert.AreEqual(item.b, counter + 1);
      Assert.AreEqual(item.c, counter + 2);
      Assert.AreEqual(item.d, counter + 3);
      counter += 4;
    }
  }

  [TestMethod]
  public void TestRandom() {
    var list = new CompressedSegmentedList<TestObject>();
    int count = 1000000;
    TestObject.Counter = 0;
    var checkList = new List<TestObject>();
    var orderList = new List<int>();

    for (int i = 0; i < count; i++) {
      var obj = TestObject.Create();
      list.Add(obj);
      checkList.Add(obj);
      orderList.Add(i);
    }

    var random = new Random(31);
    orderList = orderList.OrderBy(x => random.Next()).ToList();

    for (int i = 0; i < count; i++) {
      int index = orderList[i];
      var item = list[index];
      var checkItem = checkList[index];
      Assert.AreEqual(item.a, checkItem.a);
      Assert.AreEqual(item.b, checkItem.b);
      Assert.AreEqual(item.c, checkItem.c);
      Assert.AreEqual(item.d, checkItem.d);
    }
  }

  [TestMethod]
  public void TestPrefetch() {
    var list = new CompressedSegmentedList<TestObject>(true, true, 10);
    int count = 1000000;
    TestObject.Counter = 0;

    for (int i = 0; i < count; i++) {
      list.Add(TestObject.Create());
    }

    int counter = 0;

    foreach (var item in list) {
      Assert.AreEqual(item.a, counter);
      Assert.AreEqual(item.b, counter + 1);
      Assert.AreEqual(item.c, counter + 2);
      Assert.AreEqual(item.d, counter + 3);
      counter += 4;
    }
  }

  [TestMethod]
  public void TestGuid() {
    var list = new CompressedSegmentedList<TestGuidObject>();
    int count = 1000000;
    var checkList = new List<TestGuidObject>();
    var orderList = new List<int>();

    for (int i = 0; i < count; i++) {
      var obj = TestGuidObject.Create();
      list.Add(obj);
      checkList.Add(obj);
      orderList.Add(i);
    }

    var random = new Random(31);
    //orderList = orderList.OrderBy(x => random.Next()).ToList();

    for (int i = 0; i < count; i++) {
      int index = orderList[i];
      var item = list[index];
      var checkItem = checkList[index];
      Assert.AreEqual(item.a, checkItem.a);
      Assert.AreEqual(item.b, checkItem.b);
    }
  }

  [TestMethod]
  public void TestGuidRandom() {
    var list = new CompressedSegmentedList<TestGuidObject>();
    int count = 1000000;
    var checkList = new List<TestGuidObject>();
    var orderList = new List<int>();

    for (int i = 0; i < count; i++) {
      var obj = TestGuidObject.Create();
      list.Add(obj);
      checkList.Add(obj);
      orderList.Add(i);
    }

    var random = new Random(31);
    orderList = orderList.OrderBy(x => random.Next()).ToList();

    for (int i = 0; i < count; i++) {
      int index = orderList[i];
      var item = list[index];
      var checkItem = checkList[index];
      Assert.AreEqual(item.a, checkItem.a);
      Assert.AreEqual(item.b, checkItem.b);
    }
  }
}