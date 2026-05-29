// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Profiling;

namespace ProfileExplorer.Profiling.Tests.Unit;

[TestClass]
[TestCategory("Unit")]
public class FunctionProfileBuilderTests {
  [TestMethod]
  public void AddWeight_AccumulatesCorrectly() {
    var builder = new FunctionProfileBuilder("test.dll", "Foo", 0x100, 0x50, false);
    var weight = TimeSpan.FromMilliseconds(1);

    builder.AddSample(0, weight);
    builder.AddSample(0, weight);
    builder.AddSample(0, weight);

    Assert.AreEqual(3.0, builder.GetExclusiveWeight().TotalMilliseconds, 0.01);
  }

  [TestMethod]
  public void InstructionWeightMap_DistinctOffsets() {
    var builder = new FunctionProfileBuilder("test.dll", "Foo", 0x100, 0x50, false);
    var weight = TimeSpan.FromMilliseconds(1);

    for (int i = 0; i < 10; i++) {
      builder.AddSample(i * 4, weight);
    }

    var weights = builder.GetInstructionWeights();
    Assert.AreEqual(10, weights.Count);
  }

  [TestMethod]
  public void InstructionWeightMap_SameOffset_Merges() {
    var builder = new FunctionProfileBuilder("test.dll", "Foo", 0x100, 0x50, false);
    var weight = TimeSpan.FromMilliseconds(2);

    for (int i = 0; i < 5; i++) {
      builder.AddSample(0x10, weight);
    }

    var weights = builder.GetInstructionWeights();
    Assert.AreEqual(1, weights.Count);
    Assert.AreEqual(10.0, weights[0x10].TotalMilliseconds, 0.01);
  }

  [TestMethod]
  public void ExclusiveWeight_EqualsInstructionWeightSum() {
    var builder = new FunctionProfileBuilder("test.dll", "Foo", 0x100, 0x50, false);

    builder.AddSample(0, TimeSpan.FromMilliseconds(5));
    builder.AddSample(4, TimeSpan.FromMilliseconds(3));
    builder.AddSample(8, TimeSpan.FromMilliseconds(7));

    var weights = builder.GetInstructionWeights();
    double sumMs = weights.Values.Sum(w => w.TotalMilliseconds);

    Assert.AreEqual(15.0, builder.GetExclusiveWeight().TotalMilliseconds, 0.01);
    Assert.AreEqual(builder.GetExclusiveWeight().TotalMilliseconds, sumMs, 0.01);
  }

  [TestMethod]
  public void InclusiveWeight_IncludesCallerWeight() {
    var builder = new FunctionProfileBuilder("test.dll", "Foo", 0x100, 0x50, false);

    builder.AddSample(0, TimeSpan.FromMilliseconds(10)); // Exclusive (leaf)
    builder.AddInclusiveWeight(TimeSpan.FromMilliseconds(25)); // From callee stacks

    Assert.AreEqual(10.0, builder.GetExclusiveWeight().TotalMilliseconds, 0.01);
    Assert.AreEqual(35.0, builder.GetInclusiveWeight().TotalMilliseconds, 0.01);
  }

  [TestMethod]
  public void GetInstructionWeights_ReturnsSnapshot() {
    var builder = new FunctionProfileBuilder("test.dll", "Foo", 0x100, 0x50, false);
    builder.AddSample(0, TimeSpan.FromMilliseconds(1));

    var snapshot1 = builder.GetInstructionWeights();
    builder.AddSample(4, TimeSpan.FromMilliseconds(1));
    var snapshot2 = builder.GetInstructionWeights();

    // Snapshot1 should not be modified by later adds.
    Assert.AreEqual(1, snapshot1.Count);
    Assert.AreEqual(2, snapshot2.Count);
  }
}
