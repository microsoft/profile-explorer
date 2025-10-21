// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ProtoBuf;

namespace ProfileExplorer.Core.Profile.Data;

/// <summary>
/// Represents a period when a thread was waiting (blocked).
/// </summary>
[ProtoContract]
public class WaitInterval {
  [ProtoMember(1)]
  public int ContextId { get; set; }
  
  [ProtoMember(2)]
  public TimeSpan StartTime { get; set; }
  [ProtoMember(3)]
  public TimeSpan EndTime { get; set; }
  public TimeSpan Duration => EndTime - StartTime;
  
  [ProtoMember(4)]
  public ThreadWaitReason WaitReason { get; set; }
  
  [ProtoMember(5)]
  public int? ReadyingContextId { get; set; }
  
  [ProtoMember(6)]
  public long[]? WaitingThreadStack { get; set; }
  [ProtoMember(7)]
  public long[]? ReadyingThreadStack { get; set; }
  
  public ProfileContext GetContext(RawProfileData profileData) {
    return profileData.FindContext(ContextId);
  }
  
  public ProfileContext? GetReadyingContext(RawProfileData profileData) {
    return ReadyingContextId.HasValue ? profileData.FindContext(ReadyingContextId.Value) : null;
  }
}

/// <summary>
/// Aggregated wait statistics for a process' thread.
/// </summary>
[ProtoContract]
public class ThreadWaitSummary {
  [ProtoMember(1)]
  public int ContextId { get; set; }
  
  [ProtoMember(2)]
  public TimeSpan TotalWaitTime { get; set; }
  [ProtoMember(3)]
  public int WaitCount { get; set; }
  
  [ProtoMember(4)]
  public Dictionary<ThreadWaitReason, TimeSpan> WaitTimeByReason { get; set; } = [];
  
  // Context IDs of threads that most frequently readied this thread (for blocking relationship analysis)
  [ProtoMember(5)]
  public Dictionary<int, int> ReadyingContextCounts { get; set; } = [];
  
  public ProfileContext GetContext(RawProfileData profileData) {
    return profileData.FindContext(ContextId);
  }
}

/// <summary>
/// Container class for wait intervals and per-thread wait info
/// </summary>
[ProtoContract]
public class WaitAnalysisData {
  [ProtoMember(1)]
  public List<WaitInterval> WaitIntervals { get; set; } = [];
  [ProtoMember(2)]
  public Dictionary<int, ThreadWaitSummary> ThreadWaitSummaries { get; set; } = [];
}
