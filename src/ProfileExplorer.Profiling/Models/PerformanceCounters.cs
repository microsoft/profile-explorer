// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Runtime.InteropServices;
using ProtoBuf;

namespace ProfileExplorer.Profiling;

/// <summary>
/// Per-instruction counter values for a single instruction offset.
/// Groups all PMU counter types for one instruction.
/// </summary>
[ProtoContract(SkipConstructor = true)]
public class InstructionCounterValues {
  [ProtoMember(1)]
  private List<CounterValue> counters_;

  public InstructionCounterValues() {
    counters_ = [];
  }

  /// <summary>Counter values keyed by counter ID.</summary>
  public IReadOnlyList<CounterValue> Counters => counters_;

  /// <summary>Get the accumulated value for a specific counter.</summary>
  public long GetCounterValue(int counterId) {
    int index = counters_.FindIndex(c => c.CounterId == counterId);
    return index != -1 ? counters_[index].Value : 0;
  }

  /// <summary>Add a sample to a specific counter.</summary>
  public void AddCounterSample(int counterId, long value) {
    int index = counters_.FindIndex(c => c.CounterId == counterId);
    var span = CollectionsMarshal.AsSpan(counters_);

    if (index != -1) {
      ref var counterRef = ref span[index];
      counterRef = new CounterValue(counterRef.CounterId, counterRef.Value + value);
    }
    else {
      // Keep sorted by counter ID.
      int insertAt = 0;
      for (int i = 0; i < counters_.Count; i++, insertAt++) {
        if (counters_[i].CounterId >= counterId)
          break;
      }

      counters_.Insert(insertAt, new CounterValue(counterId, value));
    }
  }

  /// <summary>Merge another set of counter values into this one.</summary>
  public void Add(InstructionCounterValues other) {
    foreach (var counter in other.counters_) {
      AddCounterSample(counter.CounterId, counter.Value);
    }
  }
}

/// <summary>
/// A single counter type's accumulated value.
/// </summary>
[ProtoContract(SkipConstructor = true)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct CounterValue(
  [property: ProtoMember(1)] int CounterId,
  [property: ProtoMember(2)] long Value);

/// <summary>
/// Describes a registered hardware performance counter source.
/// </summary>
public class PerformanceCounterInfo {
  public PerformanceCounterInfo(int id, string name, long frequency) {
    Id = id;
    Name = name;
    Frequency = frequency;
  }

  public int Id { get; }
  public string Name { get; }
  public long Frequency { get; }
  public int Index { get; set; }
}

/// <summary>
/// A derived metric computed from two base counters (e.g., cache miss rate = misses / references).
/// </summary>
public class PerformanceMetricInfo {
  public PerformanceMetricInfo(string name, string baseCounterName, string relativeCounterName, bool isPercentage) {
    Name = name;
    BaseCounterName = baseCounterName;
    RelativeCounterName = relativeCounterName;
    IsPercentage = isPercentage;
  }

  public string Name { get; }
  public string BaseCounterName { get; }
  public string RelativeCounterName { get; }
  public bool IsPercentage { get; }

  public double ComputeMetric(long baseValue, long relativeValue) {
    if (baseValue == 0) return 0;
    double result = relativeValue / (double)baseValue;
    return IsPercentage ? Math.Min(result, 1.0) : result;
  }
}
