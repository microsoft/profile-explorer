// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace ProfileExplorer.Core.Profile.Data;

/// <summary>
/// Flags to indicate a thread's state when readied
/// https://learn.microsoft.com/en-us/windows/win32/etw/readythread#properties
/// </summary>
[Flags]
public enum ReadyThreadFlags : byte {
  None = 0,
  
  /// <summary>The thread has been readied from DPC (deferred procedure call).</summary>
  ReadiedFromDpc = 0x1,
  
  /// <summary>The kernel stack is currently swapped out.</summary>
  KernelStackSwappedOut = 0x2,
  
  /// <summary>The process address space is swapped out.</summary>
  ProcessAddressSpaceSwappedOut = 0x4
}
