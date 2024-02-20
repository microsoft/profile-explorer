// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using ProtoBuf;

namespace IRExplorerUI.Profile;

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
  PerfCounterProcessing
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
  //[ProtoMember(5)]
  //public List<(ProfileImage Image, TimeSpan Weight)> ImageWeights;

  public ProcessSummary(ProfileProcess process, TimeSpan weight) {
    Process = process;
    Weight = weight;
    //ImageWeights = new List<(ProfileImage Image, TimeSpan Weight)>();
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
