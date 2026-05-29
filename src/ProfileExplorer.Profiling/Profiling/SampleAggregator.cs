// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Concurrent;
using ProfileExplorer.Profiling.Symbols;

namespace ProfileExplorer.Profiling.Profiling;

/// <summary>
/// Aggregates CPU samples into per-function and per-instruction profiles.
/// Thread-safe — processes samples in parallel chunks.
/// </summary>
internal class SampleAggregator {
  private readonly IpResolver ipResolver_;
  private readonly ConcurrentDictionary<string, FunctionProfileBuilder> builders_ = new(StringComparer.OrdinalIgnoreCase);
  private TimeSpan totalWeight_;
  private readonly object totalWeightLock_ = new();

  public SampleAggregator(IpResolver ipResolver) {
    ipResolver_ = ipResolver;
  }

  /// <summary>
  /// Add a batch of samples. Thread-safe.
  /// </summary>
  public void AddSamples(IEnumerable<IProfileSample> samples) {
    TimeSpan batchWeight = TimeSpan.Zero;

    foreach (var sample in samples) {
      if (string.IsNullOrEmpty(sample.ImageName)) continue;

      var resolved = ipResolver_.Resolve(sample.InstructionPointer);
      if (resolved == null) continue;

      string key = $"{resolved.ModuleName}!{resolved.FunctionName ?? $"<unknown+0x{resolved.Rva:X}>"}";

      var builder = builders_.GetOrAdd(key, _ => new FunctionProfileBuilder(
        resolved.ModuleName, resolved.FunctionName ?? $"<unknown+0x{resolved.Rva:X}>",
        resolved.Rva, resolved.FunctionSize, resolved.IsManaged));

      builder.AddSample(resolved.InstructionOffset, sample.Weight);
      batchWeight += sample.Weight;

      // Handle inclusive weight via stack frames.
      if (sample.StackFrames is { Count: > 1 }) {
        // Stack is leaf-first. Skip index 0 (leaf — already counted as exclusive).
        for (int i = 1; i < sample.StackFrames.Count; i++) {
          var callerResolved = ipResolver_.Resolve(sample.StackFrames[i]);
          if (callerResolved == null) continue;

          string callerKey = $"{callerResolved.ModuleName}!{callerResolved.FunctionName ?? $"<unknown+0x{callerResolved.Rva:X}>"}";

          var callerBuilder = builders_.GetOrAdd(callerKey, _ => new FunctionProfileBuilder(
            callerResolved.ModuleName, callerResolved.FunctionName ?? $"<unknown+0x{callerResolved.Rva:X}>",
            callerResolved.Rva, callerResolved.FunctionSize, callerResolved.IsManaged));

          callerBuilder.AddInclusiveWeight(sample.Weight);
        }
      }
    }

    lock (totalWeightLock_) {
      totalWeight_ += batchWeight;
    }
  }

  /// <summary>
  /// Build the final function profiles.
  /// </summary>
  public IReadOnlyList<FunctionProfile> Build(string? processName = null, int? processId = null) {
    var profiles = new List<FunctionProfile>(builders_.Count);
    double totalMs = totalWeight_.TotalMilliseconds;

    foreach (var (_, builder) in builders_) {
      var exclusiveWeight = builder.GetExclusiveWeight();
      var inclusiveWeight = builder.GetInclusiveWeight();
      double exclusivePercent = totalMs > 0 ? exclusiveWeight.TotalMilliseconds / totalMs * 100 : 0;
      double inclusivePercent = totalMs > 0 ? inclusiveWeight.TotalMilliseconds / totalMs * 100 : 0;

      profiles.Add(new FunctionProfile(
        moduleName: builder.ModuleName,
        functionName: builder.FunctionName,
        functionRva: builder.FunctionRva,
        functionSize: builder.FunctionSize,
        inclusiveWeight: inclusiveWeight,
        exclusiveWeight: exclusiveWeight,
        inclusivePercent: inclusivePercent,
        exclusivePercent: exclusivePercent,
        sourceFile: null,  // Populated later by symbol resolution.
        sourceLine: null,
        isManaged: builder.IsManaged,
        instructionWeights: builder.GetInstructionWeights()));
    }

    return profiles;
  }

  public TimeSpan TotalWeight => totalWeight_;
}
