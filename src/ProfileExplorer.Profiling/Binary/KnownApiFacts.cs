// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;

namespace ProfileExplorer.Core.Binary;

/// <summary>
/// Coarse semantic category for a known API's primary side effect -- enough for an AI to reason
/// about ownership/lifetime/synchronization without needing the API's actual implementation.
/// </summary>
public enum ApiSideEffectCategory {
  Unknown,
  MemoryAllocation,
  MemoryFree,
  LockAcquire,
  LockRelease,
  ReferenceCountIncrement,
  ReferenceCountDecrement,
  MemoryCopyOrFill,
}

/// <summary>
/// A documented fact about a well-known API, attached to a resolved import so the AI gets the
/// API's semantics for free instead of having to infer them (or hallucinate them) from the call
/// site alone. This is intentionally a small, curated, plain-name-keyed table of common Windows/
/// CRT APIs spanning several categories -- not scoped to any single investigation (e.g. pool
/// memory); see the reverse-engineering enhancement plan for the "KnownApiFacts" design.
/// </summary>
public sealed record KnownApiFact(string FunctionName, ApiSideEffectCategory Category, string Summary);

public static class KnownApiFacts {
  private static readonly Dictionary<string, KnownApiFact> Facts = BuildFacts();

  /// <summary>
  /// Looks up a documented fact by exact, case-insensitive function name (as it appears in the PE
  /// import table -- i.e. plain C-style names; C++-mangled names are not matched here since that
  /// requires demangling integration, tracked as a follow-up). Returns false (never guesses) when
  /// the name is null/empty or unknown.
  /// </summary>
  public static bool TryGetFact(string? functionName, out KnownApiFact? fact) {
    if (string.IsNullOrEmpty(functionName)) {
      fact = null;
      return false;
    }

    return Facts.TryGetValue(functionName, out fact);
  }

  private static Dictionary<string, KnownApiFact> BuildFacts() {
    var facts = new Dictionary<string, KnownApiFact>(System.StringComparer.OrdinalIgnoreCase);

    void Add(string name, ApiSideEffectCategory category, string summary) {
      facts[name] = new KnownApiFact(name, category, summary);
    }

    // Kernel pool allocation/free.
    Add("ExAllocatePool2", ApiSideEffectCategory.MemoryAllocation,
      "Allocates pool memory (Flags, NumberOfBytes, Tag); returns a pointer or NULL on failure.");
    Add("ExAllocatePool3", ApiSideEffectCategory.MemoryAllocation,
      "Allocates pool memory with extended parameters; returns a pointer or NULL on failure.");
    Add("ExAllocatePoolWithTag", ApiSideEffectCategory.MemoryAllocation,
      "Legacy tagged pool allocation API (deprecated in favor of ExAllocatePool2/3); returns a pointer or NULL on failure.");
    Add("ExFreePool", ApiSideEffectCategory.MemoryFree,
      "Frees pool memory previously allocated by an Ex*Pool* API.");
    Add("ExFreePoolWithTag", ApiSideEffectCategory.MemoryFree,
      "Frees tagged pool memory previously allocated by an Ex*Pool* API.");

    // User-mode heap/CRT allocation.
    Add("HeapAlloc", ApiSideEffectCategory.MemoryAllocation,
      "Allocates memory from a heap; returns a pointer or NULL on failure.");
    Add("HeapFree", ApiSideEffectCategory.MemoryFree,
      "Frees memory previously allocated from a heap.");
    Add("HeapReAlloc", ApiSideEffectCategory.MemoryAllocation,
      "Resizes a previous heap allocation; may return a different pointer.");
    Add("malloc", ApiSideEffectCategory.MemoryAllocation,
      "Allocates memory from the CRT heap; returns a pointer or NULL on failure.");
    Add("free", ApiSideEffectCategory.MemoryFree,
      "Frees memory previously allocated by malloc/calloc/realloc.");
    Add("calloc", ApiSideEffectCategory.MemoryAllocation,
      "Allocates zero-initialized memory from the CRT heap.");
    Add("realloc", ApiSideEffectCategory.MemoryAllocation,
      "Resizes a previous CRT allocation; may return a different pointer.");

    // Synchronization: acquire/release pairs.
    Add("KeAcquireSpinLock", ApiSideEffectCategory.LockAcquire,
      "Acquires a kernel spin lock, raising IRQL to DISPATCH_LEVEL.");
    Add("KeReleaseSpinLock", ApiSideEffectCategory.LockRelease,
      "Releases a kernel spin lock, restoring the previous IRQL.");
    Add("KeAcquireSpinLockRaiseToDpc", ApiSideEffectCategory.LockAcquire,
      "Acquires a kernel spin lock, raising IRQL to DISPATCH_LEVEL (return value is the prior IRQL).");
    Add("ExAcquireResourceExclusiveLite", ApiSideEffectCategory.LockAcquire,
      "Acquires an executive resource (ERESOURCE) for exclusive access.");
    Add("ExAcquireResourceSharedLite", ApiSideEffectCategory.LockAcquire,
      "Acquires an executive resource (ERESOURCE) for shared access.");
    Add("ExReleaseResourceLite", ApiSideEffectCategory.LockRelease,
      "Releases an executive resource (ERESOURCE).");
    Add("AcquireSRWLockExclusive", ApiSideEffectCategory.LockAcquire,
      "Acquires a slim reader/writer lock for exclusive access.");
    Add("ReleaseSRWLockExclusive", ApiSideEffectCategory.LockRelease,
      "Releases a slim reader/writer lock held for exclusive access.");
    Add("AcquireSRWLockShared", ApiSideEffectCategory.LockAcquire,
      "Acquires a slim reader/writer lock for shared access.");
    Add("ReleaseSRWLockShared", ApiSideEffectCategory.LockRelease,
      "Releases a slim reader/writer lock held for shared access.");
    Add("EnterCriticalSection", ApiSideEffectCategory.LockAcquire,
      "Enters a critical section, blocking until owned.");
    Add("LeaveCriticalSection", ApiSideEffectCategory.LockRelease,
      "Leaves a critical section.");

    // Kernel object reference counting.
    Add("ObfReferenceObject", ApiSideEffectCategory.ReferenceCountIncrement,
      "Increments a kernel object's reference count.");
    Add("ObfDereferenceObject", ApiSideEffectCategory.ReferenceCountDecrement,
      "Decrements a kernel object's reference count; may free the object when it reaches zero.");
    Add("ObReferenceObject", ApiSideEffectCategory.ReferenceCountIncrement,
      "Increments a kernel object's reference count.");
    Add("ObDereferenceObject", ApiSideEffectCategory.ReferenceCountDecrement,
      "Decrements a kernel object's reference count; may free the object when it reaches zero.");

    // Bulk memory operations.
    Add("memcpy", ApiSideEffectCategory.MemoryCopyOrFill, "Copies a fixed number of bytes between non-overlapping buffers.");
    Add("memmove", ApiSideEffectCategory.MemoryCopyOrFill, "Copies a fixed number of bytes; safe for overlapping buffers.");
    Add("memset", ApiSideEffectCategory.MemoryCopyOrFill, "Fills a buffer with a repeated byte value.");
    Add("RtlCopyMemory", ApiSideEffectCategory.MemoryCopyOrFill, "Windows-native equivalent of memcpy.");
    Add("RtlMoveMemory", ApiSideEffectCategory.MemoryCopyOrFill, "Windows-native equivalent of memmove.");
    Add("RtlZeroMemory", ApiSideEffectCategory.MemoryCopyOrFill, "Zero-fills a buffer.");
    Add("RtlFillMemory", ApiSideEffectCategory.MemoryCopyOrFill, "Fills a buffer with a repeated byte value.");

    return facts;
  }
}
