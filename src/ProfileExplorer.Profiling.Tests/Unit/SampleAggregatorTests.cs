// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Profiling;
using ProfileExplorer.Profiling.Symbols;
using ProfileExplorer.Profiling.Tests.Helpers;

namespace ProfileExplorer.Profiling.Tests.Unit;

[TestClass]
[TestCategory("Unit")]
public class SampleAggregatorTests {
  private IpResolver CreateResolverWithFunction(string module, long moduleBase, int moduleSize,
                                                 string funcName, long funcRva, uint funcSize) {
    var resolver = new IpResolver();
    resolver.AddImage(module, moduleBase, moduleSize);
    var functions = new List<FunctionDebugInfo> {
      new(funcName, funcRva, funcSize)
    };
    resolver.SetFunctions(module, functions);
    return resolver;
  }

  [TestMethod]
  public void SingleSample_CreatesOneFunctionProfile() {
    var resolver = CreateResolverWithFunction("test.dll", 0x1000, 0x10000, "Foo", 0x100, 0x50);
    var aggregator = new SampleAggregator(resolver);
    var samples = SyntheticSampleBuilder.CreateUniform(1, "test.dll", 0x1100, TimeSpan.FromMilliseconds(1));

    aggregator.AddSamples(samples);
    var profiles = aggregator.Build();

    Assert.AreEqual(1, profiles.Count);
    Assert.AreEqual("Foo", profiles[0].FunctionName);
    Assert.AreEqual(1.0, profiles[0].ExclusiveWeight.TotalMilliseconds, 0.01);
  }

  [TestMethod]
  public void MultipleSamples_SameFunction_AggregatesWeight() {
    var resolver = CreateResolverWithFunction("test.dll", 0x1000, 0x10000, "Foo", 0x100, 0x50);
    var aggregator = new SampleAggregator(resolver);
    var samples = SyntheticSampleBuilder.CreateUniform(100, "test.dll", 0x1100, TimeSpan.FromMilliseconds(1));

    aggregator.AddSamples(samples);
    var profiles = aggregator.Build();

    Assert.AreEqual(1, profiles.Count);
    Assert.AreEqual(100.0, profiles[0].ExclusiveWeight.TotalMilliseconds, 0.01);
  }

  [TestMethod]
  public void MultipleSamples_DifferentFunctions_SeparateProfiles() {
    var resolver = new IpResolver();
    resolver.AddImage("test.dll", 0x1000, 0x10000);
    resolver.SetFunctions("test.dll", [
      new FunctionDebugInfo("Foo", 0x100, 0x50),
      new FunctionDebugInfo("Bar", 0x200, 0x50),
      new FunctionDebugInfo("Baz", 0x300, 0x50)
    ]);

    var aggregator = new SampleAggregator(resolver);
    var weight = TimeSpan.FromMilliseconds(1);
    aggregator.AddSamples([
      new SyntheticSample(0x1100, weight, 1, 1, "test.dll", 0x1000),
      new SyntheticSample(0x1200, weight, 1, 1, "test.dll", 0x1000),
      new SyntheticSample(0x1300, weight, 1, 1, "test.dll", 0x1000)
    ]);

    var profiles = aggregator.Build();
    Assert.AreEqual(3, profiles.Count);
  }

  [TestMethod]
  public void InstructionWeights_AggregatesPerOffset() {
    var resolver = CreateResolverWithFunction("test.dll", 0x1000, 0x10000, "Foo", 0x100, 0x50);
    var aggregator = new SampleAggregator(resolver);
    var weight = TimeSpan.FromMilliseconds(1);

    // 10 samples each at 5 different offsets within the function.
    var samples = new List<IProfileSample>();
    for (int offset = 0; offset < 5; offset++) {
      for (int i = 0; i < 10; i++) {
        samples.Add(new SyntheticSample(0x1100 + offset * 4, weight, 1, 1, "test.dll", 0x1000));
      }
    }

    aggregator.AddSamples(samples);
    var profiles = aggregator.Build();

    Assert.AreEqual(1, profiles.Count);
    Assert.AreEqual(5, profiles[0].InstructionWeights.Count);
    Assert.AreEqual(50.0, profiles[0].ExclusiveWeight.TotalMilliseconds, 0.01);
  }

  [TestMethod]
  public void EmptySamples_ReturnsEmptyProfiles() {
    var resolver = new IpResolver();
    var aggregator = new SampleAggregator(resolver);

    aggregator.AddSamples([]);
    var profiles = aggregator.Build();

    Assert.AreEqual(0, profiles.Count);
  }

  [TestMethod]
  public void SamplesWithNoImage_SkippedGracefully() {
    var resolver = new IpResolver();
    var aggregator = new SampleAggregator(resolver);
    var samples = new List<IProfileSample> {
      new SyntheticSample(0x1000, TimeSpan.FromMilliseconds(1), 1, 1, null, 0)
    };

    aggregator.AddSamples(samples);
    var profiles = aggregator.Build();

    Assert.AreEqual(0, profiles.Count);
  }

  [TestMethod]
  public void PercentCalculation_RelativeToTotalWeight() {
    var resolver = new IpResolver();
    resolver.AddImage("test.dll", 0x1000, 0x10000);
    resolver.SetFunctions("test.dll", [
      new FunctionDebugInfo("Foo", 0x100, 0x50),
      new FunctionDebugInfo("Bar", 0x200, 0x50)
    ]);

    var aggregator = new SampleAggregator(resolver);
    var weight = TimeSpan.FromMilliseconds(1);

    // 75 samples to Foo, 25 to Bar.
    var samples = new List<IProfileSample>();
    for (int i = 0; i < 75; i++)
      samples.Add(new SyntheticSample(0x1100, weight, 1, 1, "test.dll", 0x1000));
    for (int i = 0; i < 25; i++)
      samples.Add(new SyntheticSample(0x1200, weight, 1, 1, "test.dll", 0x1000));

    aggregator.AddSamples(samples);
    var profiles = aggregator.Build();

    var foo = profiles.First(p => p.FunctionName == "Foo");
    var bar = profiles.First(p => p.FunctionName == "Bar");

    Assert.AreEqual(75.0, foo.ExclusivePercent, 0.1);
    Assert.AreEqual(25.0, bar.ExclusivePercent, 0.1);
  }
}
