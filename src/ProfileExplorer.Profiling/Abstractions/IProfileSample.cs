// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Profiling;

/// <summary>
/// A single CPU sample with an instruction pointer and weight.
/// Consumers implement this to bridge their sample source (DataLayer, TraceEvent, etc.).
/// </summary>
public interface IProfileSample {
  /// <summary>Absolute instruction pointer (virtual address) where the sample was taken.</summary>
  long InstructionPointer { get; }

  /// <summary>Estimated duration this sample represents (typically ~1ms for 1kHz sampling).</summary>
  TimeSpan Weight { get; }

  /// <summary>Process ID that was running when this sample was taken.</summary>
  int ProcessId { get; }

  /// <summary>Thread ID that was running when this sample was taken.</summary>
  int ThreadId { get; }

  /// <summary>Name of the module/image that owns the instruction pointer (e.g., "ntdll.dll").</summary>
  string? ImageName { get; }

  /// <summary>Base address of the module in the process address space.</summary>
  long ImageBaseAddress { get; }

  /// <summary>
  /// Full stack frame instruction pointers, leaf-first.
  /// Optional — only needed for call tree construction and inclusive weight calculation.
  /// </summary>
  IReadOnlyList<long>? StackFrames { get; }
}
