// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace ProfileExplorer.Core.Profile.Data;

/// <summary>
/// Represents the execution state of a thread
/// https://learn.microsoft.com/en-us/windows/win32/etw/cswitch#properties
/// </summary>
public enum ThreadExecutionState : sbyte {
  Initialized = 0,
  Ready = 1,
  Running = 2,
  Standby = 3,
  Terminated = 4,
  Waiting = 5,
  Transition = 6,
  DeferredReady = 7
}
