// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProfileExplorer.Profiling.Tests.Unit;

[TestClass]
[TestCategory("Unit")]
public class ProfilerOptionsValidationTests {
  [TestMethod]
  public void DefaultOptions_WithSymbolPaths_AreValid() {
    var options = new ProfilerOptions {
      SymbolPaths = ["srv*C:\\Symbols*https://symbolserver.example.com"]
    };

    options.Validate(); // Should not throw.
  }

  [TestMethod]
  [ExpectedException(typeof(ArgumentException))]
  public void EmptySymbolPaths_Throws() {
    var options = new ProfilerOptions { SymbolPaths = [] };
    options.Validate();
  }

  [TestMethod]
  [ExpectedException(typeof(ArgumentOutOfRangeException))]
  public void NegativeTimeout_Throws() {
    var options = new ProfilerOptions {
      SymbolPaths = ["srv*https://example.com"],
      SymbolTimeoutSeconds = -1
    };
    options.Validate();
  }

  [TestMethod]
  public void MinSelfPercent_ClampedToValidRange() {
    var options = new ProfilerOptions {
      SymbolPaths = ["srv*https://example.com"],
      MinSelfPercent = 150 // Over 100
    };

    options.Validate();
    Assert.AreEqual(100, options.MinSelfPercent);
  }

  [TestMethod]
  public void MinSelfPercent_NegativeClamped() {
    var options = new ProfilerOptions {
      SymbolPaths = ["srv*https://example.com"],
      MinSelfPercent = -5
    };

    options.Validate();
    Assert.AreEqual(0, options.MinSelfPercent);
  }
}
