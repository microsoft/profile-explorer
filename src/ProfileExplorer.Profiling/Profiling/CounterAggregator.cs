// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Profiling.Profiling;

/// <summary>
/// Aggregates hardware performance counter events into per-function/instruction counter values.
/// </summary>
internal class CounterAggregator {
  private readonly IpResolver ipResolver_;
  private readonly Dictionary<string, Dictionary<long, InstructionCounterValues>> countersByFunction_ = new(StringComparer.OrdinalIgnoreCase);
  private readonly object lock_ = new();

  public CounterAggregator(IpResolver ipResolver) {
    ipResolver_ = ipResolver;
  }

  /// <summary>
  /// Add a batch of performance counter events.
  /// </summary>
  public void AddEvents(IEnumerable<IPerformanceCounterEvent> events) {
    foreach (var evt in events) {
      var resolved = ipResolver_.Resolve(evt.InstructionPointer);
      if (resolved?.FunctionName == null) continue;

      string key = $"{resolved.ModuleName}!{resolved.FunctionName}";

      lock (lock_) {
        if (!countersByFunction_.TryGetValue(key, out var instrCounters)) {
          instrCounters = [];
          countersByFunction_[key] = instrCounters;
        }

        if (!instrCounters.TryGetValue(resolved.InstructionOffset, out var counterSet)) {
          counterSet = new InstructionCounterValues();
          instrCounters[resolved.InstructionOffset] = counterSet;
        }

        counterSet.AddCounterSample(evt.CounterId, 1);
      }
    }
  }

  /// <summary>
  /// Get the per-instruction counter values for a specific function.
  /// </summary>
  public IReadOnlyDictionary<long, InstructionCounterValues>? GetCounters(string qualifiedFunctionName) {
    lock (lock_) {
      return countersByFunction_.TryGetValue(qualifiedFunctionName, out var counters)
        ? new Dictionary<long, InstructionCounterValues>(counters)
        : null;
    }
  }
}
