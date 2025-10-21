// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace ProfileExplorer.Core.Profile.Data;

/// <summary>
/// Represents a context switch event
/// https://learn.microsoft.com/en-us/windows/win32/etw/cswitch
/// </summary>
public struct ContextSwitchEvent {
  // Fields from EVENT_HEADER and ETW_BUFFER_CONTEXT
  public TimeSpan Timestamp { get; set; }
  
  /// <summary>The CPU number that the event is occurring on (from ETW_BUFFER_CONTEXT).</summary>
  public int Processor { get; set; }
  
  /// <summary>The process ID (from EVENT_HEADER).</summary>
  public int ProcessId { get; set; }

  // Fields from CSwitch event payload (in documentation order)
  
  /// <summary>New thread ID after the switch.</summary>
  public int NewThreadId { get; set; }
  
  /// <summary>Previous thread ID.</summary>
  public int OldThreadId { get; set; }
  
  /// <summary>Thread priority of the new thread.</summary>
  public sbyte NewThreadPriority { get; set; }
  
  /// <summary>Thread priority of the previous thread.</summary>
  public sbyte OldThreadPriority { get; set; }
  
  /// <summary>Wait reason for the previous thread.</summary>
  public ThreadWaitReason OldThreadWaitReason { get; set; }
  
  /// <summary>Wait mode for the previous thread.</summary>
  public ThreadWaitMode OldThreadWaitMode { get; set; }
  
  /// <summary>State of the previous thread.</summary>
  public ThreadExecutionState OldThreadState { get; set; }
  
  /// <summary>Wait time for the new thread.</summary>
  public TimeSpan NewThreadWaitTime { get; set; }
}

/// <summary>
/// Represents a ready thread event
/// https://learn.microsoft.com/en-us/windows/win32/etw/readythread
/// </summary>
public struct ReadyThreadEvent {
  // Fields from EVENT_HEADER and ETW_BUFFER_CONTEXT
  public TimeSpan Timestamp { get; set; }
  
  /// <summary>The CPU number that the event is occurring on (from ETW_BUFFER_CONTEXT).</summary>
  public int Processor { get; set; }
  
  /// <summary>The process ID (from EVENT_HEADER).</summary>
  public int ProcessId { get; set; }

  // Fields from ReadyThread event payload (in documentation order)
  
  /// <summary>The thread identifier of the thread being readied for execution.</summary>
  public int ReadiedThreadId { get; set; }
  
  /// <summary>The reason for the priority boost.</summary>
  public ThreadAdjustReason AdjustReason { get; set; }

  /// <summary>The value by which the priority is being adjusted.</summary>
  public sbyte AdjustIncrement { get; set; }

  /// <summary>Possible state flags for the readied thread.</summary>
  public ReadyThreadFlags Flags { get; set; }
}
