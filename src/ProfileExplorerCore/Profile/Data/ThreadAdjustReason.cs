// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace ProfileExplorer.Core.Profile.Data;

/// <summary>
/// Reason for a priority boost
/// https://learn.microsoft.com/en-us/windows/win32/etw/readythread#properties
/// </summary>
public enum ThreadAdjustReason : sbyte {
  /// <summary>Ignore the increment.</summary>
  None = 0,
  
  /// <summary>Apply the increment, which will decay incrementally at the end of each quantum.</summary>
  Unwait = 1,
  
  /// <summary>Apply the increment as a boost that will decay in its entirety at quantum (typically for priority donation).</summary>
  Boost = 2
}
