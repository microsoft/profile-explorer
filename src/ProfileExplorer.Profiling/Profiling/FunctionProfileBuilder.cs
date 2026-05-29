// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Profiling.Profiling;

/// <summary>
/// Accumulates per-instruction weights for a single function.
/// Thread-safe — uses locking for concurrent sample addition.
/// </summary>
internal class FunctionProfileBuilder {
  private readonly Dictionary<long, TimeSpan> instructionWeights_ = [];
  private TimeSpan exclusiveWeight_;
  private TimeSpan additionalInclusiveWeight_;
  private readonly object lock_ = new();

  public FunctionProfileBuilder(string moduleName, string functionName, long functionRva, int functionSize, bool isManaged) {
    ModuleName = moduleName;
    FunctionName = functionName;
    FunctionRva = functionRva;
    FunctionSize = functionSize;
    IsManaged = isManaged;
  }

  public string ModuleName { get; }
  public string FunctionName { get; }
  public long FunctionRva { get; }
  public int FunctionSize { get; }
  public bool IsManaged { get; }

  /// <summary>
  /// Add a sample at a specific instruction offset (exclusive/self weight).
  /// </summary>
  public void AddSample(long instructionOffset, TimeSpan weight) {
    lock (lock_) {
      exclusiveWeight_ += weight;

      if (instructionWeights_.TryGetValue(instructionOffset, out var existing)) {
        instructionWeights_[instructionOffset] = existing + weight;
      }
      else {
        instructionWeights_[instructionOffset] = weight;
      }
    }
  }

  /// <summary>
  /// Add inclusive weight from a stack frame where this function is a caller (not the leaf).
  /// </summary>
  public void AddInclusiveWeight(TimeSpan weight) {
    lock (lock_) {
      additionalInclusiveWeight_ += weight;
    }
  }

  /// <summary>
  /// Get the exclusive (self) weight — sum of all instruction weights.
  /// </summary>
  public TimeSpan GetExclusiveWeight() {
    lock (lock_) {
      return exclusiveWeight_;
    }
  }

  /// <summary>
  /// Get the inclusive (total) weight — exclusive + caller-attributed inclusive.
  /// </summary>
  public TimeSpan GetInclusiveWeight() {
    lock (lock_) {
      return exclusiveWeight_ + additionalInclusiveWeight_;
    }
  }

  /// <summary>
  /// Get a snapshot of per-instruction-offset weights.
  /// </summary>
  public Dictionary<long, TimeSpan> GetInstructionWeights() {
    lock (lock_) {
      return new Dictionary<long, TimeSpan>(instructionWeights_);
    }
  }
}
