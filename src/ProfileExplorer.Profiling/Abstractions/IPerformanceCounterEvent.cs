// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Profiling;

/// <summary>
/// A hardware performance counter sample event (PMU/PMC).
/// </summary>
public interface IPerformanceCounterEvent {
  /// <summary>Instruction pointer where the counter event was captured.</summary>
  long InstructionPointer { get; }

  /// <summary>Timestamp of the counter event.</summary>
  TimeSpan Timestamp { get; }

  /// <summary>Process ID.</summary>
  int ProcessId { get; }

  /// <summary>Thread ID.</summary>
  int ThreadId { get; }

  /// <summary>Counter source identifier (e.g., InstructionsRetired, CacheMisses).</summary>
  short CounterId { get; }
}
