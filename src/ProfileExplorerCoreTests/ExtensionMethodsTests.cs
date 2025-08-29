// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.CoreTests;

[TestClass]
public class ExtensionMethodsTests {
  [TestMethod]
  public void CountLines_EmptyString_ReturnsZero() {
    string emptyString = "";
    int result = emptyString.CountLines();
    Assert.AreEqual(0, result);
  }

  [TestMethod]
  public void CountLines_SingleLineString_ReturnsOne() {
    string singleLineString = "This is a single line.";
    int result = singleLineString.CountLines();
    Assert.AreEqual(1, result);
  }

  [TestMethod]
  public void CountLines_MultiLineString_ReturnsCorrectCount() {
    string multiLineString = "Line 1\nLine 2\nLine 3";
    int result = multiLineString.CountLines();
    Assert.AreEqual(3, result);
  }

  [TestMethod]
  public void CountLines_MultiLineString_ReturnsCorrectCount2() {
    string multiLineString = "Line 1\r\nLine 2\nLine 3\rLine4";
    int result = multiLineString.CountLines();
    Assert.AreEqual(4, result);
  }

  [TestMethod]
  public void CountLines_MultiLineString_ReturnsCorrectCount3() {
    string multiLineString = "Line 1\r\nLine 2\nLine 3\r\nLine4";
    int result = multiLineString.CountLines();
    Assert.AreEqual(4, result);
  }

  [TestMethod]
  public void CountLines_MultiLineString_ReturnsCorrectCount4() {
    string multiLineString = "Line 1\r\nLine 2\nLine 3\rLine4\n\rLine6";
    int result = multiLineString.CountLines();
    Assert.AreEqual(6, result);
  }
}