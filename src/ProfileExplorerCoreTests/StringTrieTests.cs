// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Collections;

namespace ProfileExplorerCoreTests;

[TestClass]
public class StringTrieTests {
  [TestMethod]
  public void StringValueTest() {
    var trie = new StringTrie<string>();
    var values = new Dictionary<string, string> {
      {"abc", "123"},
      {"def", "456"},
      {"abcd", "234"},
      {"abce", "235"},
      {"abcr", "236"},
      {"abcdef", "124"},
      {"abcdet", "125"},
      {"dxyz", "126"},
      {"dexyz", "127"}
    };

    trie.Build(values);

    foreach (var pair in values) {
      bool found = trie.TryGetValue(pair.Key, out string outValue);
      Assert.IsTrue(found);
      Assert.AreEqual(outValue, pair.Value);
    }
  }

  [TestMethod]
  public void StringEnumTest() {
    var trie = new StringTrie<TestEnum>();
    var values = new Dictionary<string, TestEnum> {
      {"abc", TestEnum.Value2},
      {"def", TestEnum.Value2},
      {"abcd", TestEnum.Value2},
      {"abce", TestEnum.Value2},
      {"abcr", TestEnum.Value2},
      {"abcdef", TestEnum.Value2},
      {"abcdet", TestEnum.Value2},
      {"dxyz", TestEnum.Value2},
      {"dexyz", TestEnum.Value2}
    };

    trie.Build(values);

    foreach (var pair in values) {
      bool found = trie.TryGetValue(pair.Key, out var outValue);
      Assert.IsTrue(found);
      Assert.AreEqual(outValue, pair.Value);
    }
  }

  [TestMethod]
  public void RandomStringEnumTest() {
    for (int length = 1; length < 102; length += 20) {
      var trie = new StringTrie<TestEnum>();
      var values = new Dictionary<string, TestEnum>();
      var random = new Random(31);

      for (int i = 0; i < 1000; i++) {
        string key = GenerateRandomString(length, random);
        var value = GenerateRandomEnum<TestEnum>(random);
        values[key] = value;
      }

      trie.Build(values);

      foreach (var pair in values) {
        bool found = trie.TryGetValue(pair.Key, out var outValue);
        Assert.IsTrue(found);
        Assert.AreEqual(outValue, pair.Value);
      }
    }
  }

  private string GenerateRandomString(int length, Random random) {
    string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    char[] stringChars = new char[length];

    for (int i = 0; i < stringChars.Length; i++) {
      stringChars[i] = chars[random.Next(chars.Length)];
    }

    return new string(stringChars);
  }

  private T GenerateRandomEnum<T>(Random random) where T : Enum {
    var values = Enum.GetValues(typeof(T));
    return (T)values.GetValue(random.Next(values.Length));
  }

  private enum TestEnum {
    Value1,
    Value2,
    Value3
  }
}