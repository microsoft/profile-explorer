// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProfileExplorer.Profiling.Profiling;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Profiling.Tests.Unit;

[TestClass]
[TestCategory("Unit")]
public class CounterAggregatorTests {
  private class TestCounterEvent : IPerformanceCounterEvent {
    public long InstructionPointer { get; init; }
    public TimeSpan Timestamp { get; init; }
    public int ProcessId { get; init; }
    public int ThreadId { get; init; }
    public short CounterId { get; init; }
  }

  private (IpResolver resolver, CounterAggregator aggregator) Setup() {
    var resolver = new IpResolver();
    resolver.AddImage("test.dll", 0x1000, 0x10000);
    resolver.SetFunctions("test.dll", [
      new FunctionDebugInfo("HotFunc", 0x100, 0x50)
    ]);
    return (resolver, new CounterAggregator(resolver));
  }

  [TestMethod]
  public void SingleCounter_AttributedToFunction() {
    var (_, aggregator) = Setup();

    aggregator.AddEvents([
      new TestCounterEvent { InstructionPointer = 0x1100, CounterId = 1, ProcessId = 1 }
    ]);

    var counters = aggregator.GetCounters("test.dll!HotFunc");
    Assert.IsNotNull(counters);
    Assert.AreEqual(1, counters.Count);
  }

  [TestMethod]
  public void MultipleCounters_SameInstruction() {
    var (_, aggregator) = Setup();

    aggregator.AddEvents([
      new TestCounterEvent { InstructionPointer = 0x1110, CounterId = 1 },
      new TestCounterEvent { InstructionPointer = 0x1110, CounterId = 2 },
      new TestCounterEvent { InstructionPointer = 0x1110, CounterId = 3 }
    ]);

    var counters = aggregator.GetCounters("test.dll!HotFunc");
    Assert.IsNotNull(counters);

    // All three counter types at same instruction offset.
    long offset = 0x110 - 0x100; // 0x10
    Assert.IsTrue(counters.ContainsKey(offset));
    Assert.AreEqual(1, counters[offset].GetCounterValue(1));
    Assert.AreEqual(1, counters[offset].GetCounterValue(2));
    Assert.AreEqual(1, counters[offset].GetCounterValue(3));
  }

  [TestMethod]
  public void DerivedMetric_ComputesCorrectly() {
    var metric = new PerformanceMetricInfo("CacheMissRate", "CacheReferences", "CacheMisses", true);

    Assert.AreEqual(0.25, metric.ComputeMetric(100, 25), 0.001);
    Assert.AreEqual(0.0, metric.ComputeMetric(0, 25), 0.001); // Division by zero → 0.
    Assert.AreEqual(1.0, metric.ComputeMetric(50, 100), 0.001); // Capped at 1.0 for percentage.
  }

  [TestMethod]
  public void UnresolvedIP_Skipped() {
    var resolver = new IpResolver(); // No images registered.
    var aggregator = new CounterAggregator(resolver);

    aggregator.AddEvents([
      new TestCounterEvent { InstructionPointer = 0xDEADBEEF, CounterId = 1 }
    ]);

    var counters = aggregator.GetCounters("anything");
    Assert.IsNull(counters);
  }

  [TestMethod]
  public void NoCountersRegistered_ReturnsNull() {
    var (_, aggregator) = Setup();
    var counters = aggregator.GetCounters("test.dll!HotFunc");
    Assert.IsNull(counters);
  }
}
