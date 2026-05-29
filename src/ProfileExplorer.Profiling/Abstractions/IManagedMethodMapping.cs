// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Profiling;

/// <summary>
/// A JIT-compiled managed method mapping from CLR events.
/// Consumers implement this to provide .NET method resolution data.
/// </summary>
public interface IManagedMethodMapping {
  /// <summary>Process ID this method belongs to.</summary>
  int ProcessId { get; }

  /// <summary>Fully qualified method name (e.g., "System.String.Concat").</summary>
  string MethodName { get; }

  /// <summary>Native (JIT-compiled) code start address.</summary>
  long NativeStartAddress { get; }

  /// <summary>Native code size in bytes.</summary>
  int NativeSize { get; }

  /// <summary>Metadata token for the method.</summary>
  int MethodToken { get; }

  /// <summary>Managed assembly name (e.g., "System.Private.CoreLib").</summary>
  string? ModuleName { get; }

  /// <summary>Portable PDB GUID for managed symbol resolution.</summary>
  Guid ManagedPdbGuid { get; }

  /// <summary>Portable PDB Age.</summary>
  int ManagedPdbAge { get; }

  /// <summary>Portable PDB file name.</summary>
  string? ManagedPdbName { get; }

  /// <summary>IL offset to native offset mappings. Optional.</summary>
  IReadOnlyList<ILToNativeMapping>? ILMappings { get; }
}

/// <summary>
/// Maps an IL offset range to a native code offset range within a JIT-compiled method.
/// </summary>
public readonly record struct ILToNativeMapping(int ILOffset, int NativeStartOffset, int NativeEndOffset);
