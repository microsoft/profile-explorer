// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;
using ProtoBuf;

namespace ProfileExplorer.Core.Profile.Data;

public delegate void ProfileLoadProgressHandler(ProfileLoadProgress info);
public delegate void ProcessListProgressHandler(ProcessListProgress info);

public enum ModuleLoadState {
  Loaded,
  NotFound,
  Failed
}

public enum ProfileLoadStage {
  TraceReading,
  BinaryLoading,
  SymbolLoading,
  TraceProcessing,
  PerfCounterProcessing,
  ComputeCallTree
}

public enum ProfileSessionKind {
  SystemWide,
  StartProcess,
  AttachToProcess
}

public interface IProfileDataProvider {
  Task<ProfileData> LoadTraceAsync(RawProfileData rawProfile, List<int> processIds,
                                   ProfileDataProviderOptions options,
                                   SymbolFileSourceSettings symbolSettings,
                                   ProfileDataReport report,
                                   ProfileLoadProgressHandler progressCallback,
                                   CancelableTask cancelableTask = null);

  Task<ProfileData> LoadTraceAsync(string tracePath, List<int> processIds,
                                   ProfileDataProviderOptions options,
                                   SymbolFileSourceSettings symbolSettings,
                                   ProfileDataReport report,
                                   ProfileLoadProgressHandler progressCallback,
                                   CancelableTask cancelableTask = null);
}

[ProtoContract(SkipConstructor = true)]
public class ProcessSummary {
  public ProcessSummary(ProfileProcess process, TimeSpan weight) {
    Process = process;
    Weight = weight;
  }

  [ProtoMember(1)]
  public ProfileProcess Process { get; set; }
  [ProtoMember(2)]
  public TimeSpan Weight { get; set; }
  [ProtoMember(3)]
  public double WeightPercentage { get; set; }
  [ProtoMember(4)]
  public TimeSpan Duration { get; set; }

  public override string ToString() {
    return $"{Process.Name} ({Weight})";
  }
}

public class ProfileLoadProgress {
  public ProfileLoadProgress(ProfileLoadStage stage) {
    Stage = stage;
  }

  public ProfileLoadStage Stage { get; set; }
  public int Total { get; set; }
  public int Current { get; set; }
  public string Optional { get; set; }

  public override string ToString() {
    return $"{Stage}: {Current}/{Total} {Optional}";
  }
}

public class ProcessListProgress {
  public int Total { get; set; }
  public int Current { get; set; }
  public List<ProcessSummary> Processes { get; set; }

  public override string ToString() {
    return $"{Current}/{Total}";
  }
}