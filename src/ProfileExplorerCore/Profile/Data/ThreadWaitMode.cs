// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace ProfileExplorer.Core.Profile.Data;

/// <summary>
/// Wait mode when a thread enters a wait state.
/// https://learn.microsoft.com/en-us/windows/win32/etw/cswitch#properties
/// </summary>
public enum ThreadWaitMode : sbyte {
  KernelMode = 0,
  UserMode = 1
}
