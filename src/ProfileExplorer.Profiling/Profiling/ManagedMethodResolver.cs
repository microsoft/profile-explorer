// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.Profiling.Profiling;

/// <summary>
/// Resolves instruction pointers to managed (.NET) JIT-compiled methods via binary search.
/// </summary>
internal class ManagedMethodResolver {
  private readonly List<ManagedMethodEntry> methods_ = [];
  private bool sorted_;
  private readonly object lock_ = new();

  /// <summary>
  /// Register a managed method mapping.
  /// </summary>
  public void AddMethod(IManagedMethodMapping mapping) {
    lock (lock_) {
      methods_.Add(new ManagedMethodEntry(
        mapping.ProcessId,
        mapping.MethodName,
        mapping.NativeStartAddress,
        mapping.NativeSize,
        mapping.MethodToken,
        mapping.ModuleName,
        mapping.ManagedPdbGuid,
        mapping.ManagedPdbAge,
        mapping.ManagedPdbName,
        mapping.ILMappings));
      sorted_ = false;
    }
  }

  /// <summary>
  /// Find the managed method containing the given instruction pointer.
  /// </summary>
  public ManagedMethodEntry? FindMethod(long ip) {
    lock (lock_) {
      EnsureSorted();

      // Binary search by native start address.
      int low = 0;
      int high = methods_.Count - 1;

      while (low <= high) {
        int mid = low + (high - low) / 2;
        var method = methods_[mid];

        if (ip < method.NativeStartAddress) {
          high = mid - 1;
        }
        else if (ip >= method.NativeStartAddress + method.NativeSize) {
          low = mid + 1;
        }
        else {
          return method; // IP is within this method's code range.
        }
      }

      return null;
    }
  }

  private void EnsureSorted() {
    if (sorted_) return;
    methods_.Sort((a, b) => a.NativeStartAddress.CompareTo(b.NativeStartAddress));
    sorted_ = true;
  }
}

/// <summary>
/// Internal representation of a managed method mapping.
/// </summary>
internal record ManagedMethodEntry(
  int ProcessId,
  string MethodName,
  long NativeStartAddress,
  int NativeSize,
  int MethodToken,
  string? ModuleName,
  Guid ManagedPdbGuid,
  int ManagedPdbAge,
  string? ManagedPdbName,
  IReadOnlyList<ILToNativeMapping>? ILMappings);
