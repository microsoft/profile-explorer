// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
public struct IRTextFunctionId : IEquatable<IRTextFunctionId> {
  [ProtoMember(1)] public Guid SummaryId { get; set; }
  [ProtoMember(2)] public int FunctionNumber { get; set; }

  public IRTextFunctionId(Guid summaryId, int funcNumber) {
    SummaryId = summaryId;
    FunctionNumber = funcNumber;
  }

  public IRTextFunctionId(IRTextFunction func) {
    SummaryId = func.ParentSummary.Id;
    FunctionNumber = func.Number;
  }

  public bool Equals(IRTextFunctionId other) {
    return FunctionNumber == other.FunctionNumber && SummaryId.Equals(other.SummaryId);
  }

  public override bool Equals(object obj) {
    return obj is IRTextFunctionId other && Equals(other);
  }

  public override int GetHashCode() {
    return HashCode.Combine(SummaryId, FunctionNumber);
  }

  public override string ToString() {
    return $"{FunctionNumber}@{SummaryId}";
  }

  public static bool operator ==(IRTextFunctionId left, IRTextFunctionId right) {
    return left.Equals(right);
  }

  public static bool operator !=(IRTextFunctionId left, IRTextFunctionId right) {
    return !left.Equals(right);
  }

  public static implicit operator IRTextFunctionId(IRTextFunction func) {
    return new IRTextFunctionId(func);
  }
}

// Represents a contiguous range of samples running on the same thread.
public struct ThreadSampleRange {
  public int StartIndex;
  public int EndIndex;
}

public partial class ProfileData {
  public ProfileData(TimeSpan profileWeight, TimeSpan totalWeight) : this() {
    ProfileWeight = profileWeight;
    TotalWeight = totalWeight;
  }

  public ProfileData() {
    ProfileWeight = TimeSpan.Zero;
    FunctionProfiles = new ConcurrentDictionary<IRTextFunction, FunctionProfileData>();
    ModuleWeights = new Dictionary<int, TimeSpan>();
    PerformanceCounters = new Dictionary<int, PerformanceCounter>();
    ModuleCounters = new Dictionary<string, PerformanceCounterValueSet>();
    Threads = new Dictionary<int, ProfileThread>();
    Modules = new Dictionary<int, ProfileImage>();
    Samples = new List<(ProfileSample, ResolvedProfileStack)>();
    Events = new List<(PerformanceCounterEvent Sample, ResolvedProfileStack Stack)>();
    ModuleDebugInfo = new Dictionary<string, IDebugInfoProvider>();
    Filter = new ProfileSampleFilter();
  }

  public TimeSpan ProfileWeight { get; set; }
  public TimeSpan TotalWeight { get; set; }
  public ConcurrentDictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; set; }
  public Dictionary<int, TimeSpan> ModuleWeights { get; set; }
  public Dictionary<string, PerformanceCounterValueSet> ModuleCounters { get; set; }
  public Dictionary<int, PerformanceCounter> PerformanceCounters { get; set; }
  public ProfileCallTree CallTree { get; set; }
  public ThreadSampleRanges ThreadSampleRanges { get; set; }
  public ProfileDataReport Report { get; set; }
  public List<(ProfileSample Sample, ResolvedProfileStack Stack)> Samples { get; set; }
  public List<(PerformanceCounterEvent Sample, ResolvedProfileStack Stack)> Events { get; set; }
  public ProfileProcess Process { get; set; }
  public Dictionary<int, ProfileThread> Threads { get; set; }
  public Dictionary<int, ProfileImage> Modules { get; set; }
  public Dictionary<string, IDebugInfoProvider> ModuleDebugInfo { get; set; }
  public ProfileSampleFilter Filter { get; set; }

  public List<PerformanceCounter> SortedPerformanceCounters {
    get {
      var list = PerformanceCounters.ToValueList();
      list.Sort((a, b) => b.Id.CompareTo(a.Id));
      return list;
    }
  }

  public List<(int ThreadId, TimeSpan Weight)> SortedThreadWeights {
    get {
      var list = new List<(int ThreadId, TimeSpan Weight)>();
      var threadWeights = new Dictionary<int, TimeSpan>();

      foreach (var (sample, stack) in Samples) {
        threadWeights.AccumulateValue(stack.Context.ThreadId, sample.Weight);
      }

      foreach ((int threadId, var weight) in threadWeights) {
        list.Add((threadId, weight));
      }

      list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
      return list;
    }
  }

  public static ProfileData MakeDummySamples(int countM, TimeSpan duration) {
    countM *= 1000 * 1000;
    var samples = new List<(ProfileSample Sample, ResolvedProfileStack Stack)>(countM);
    double timePerSample = (double)duration.Ticks / countM;

    var stack = new ResolvedProfileStack(0, new ProfileContext());

    for (int i = 0; i < countM; i++) {
      var sample = new ProfileSample {
        Time = TimeSpan.FromTicks((long)(timePerSample * i)),
        Weight = TimeSpan.FromTicks(10000)
      };

      samples.Add((sample, stack));
    }

    return new ProfileData {Samples = samples};
  }

  public static ProfileData Deserialize(byte[] data, List<IRTextSummary> summaries) {
    var state = StateSerializer.Deserialize<ProfileDataState>(data);
    var profileData = new ProfileData(state.ProfileWeight, state.TotalWeight);
    profileData.PerformanceCounters = state.PerformanceCounters;
    profileData.ModuleWeights = state.ModuleWeights;

    var summaryMap = new Dictionary<Guid, IRTextSummary>();

    foreach (var summary in summaries) {
      summaryMap[summary.Id] = summary;
    }

    foreach (var pair in state.FunctionProfiles) {
      var summary = summaryMap[pair.Key.SummaryId];
      var function = summary.GetFunctionWithId(pair.Key.FunctionNumber);

      if (function == null) {
        Trace.TraceWarning($"No func for {pair.Key}");
        continue;
      }

      profileData.FunctionProfiles[function] = pair.Value;
    }

    profileData.Process = state.Process;
    profileData.Threads = state.Threads;
    profileData.Modules = state.Modules;
    profileData.Report = state.Report;
    profileData.CallTree = ProfileCallTree.Deserialize(state.CallTreeState, summaryMap);
    DeserializeSamples(profileData, state, summaryMap);
    return profileData;
  }

  private static void DeserializeSamples(ProfileData profileData, ProfileDataState state,
                                         Dictionary<Guid, IRTextSummary> summaryMap) {
    if (state.Samples == null) {
      return;
    }

    profileData.Samples = state.Samples;

    foreach (var pair in profileData.Samples) {
      foreach (var frame in pair.Stack.StackFrames) {
        if (frame.FrameDetails.Function == null) {
          continue; // Unknown frame.
        }

        // if (!summaryMap.ContainsKey(frame.FrameDetails.Function.Id.SummaryId)) {
        //   continue;
        // }
        //
        // var summary = summaryMap[frame.FrameDetails.Function.Id.SummaryId];
        // var function = summary.GetFunctionWithId(frame.FrameDetails.Function.Id.FunctionNumber);

        // if (function == null) {
        //   Debug.Assert(false, "Could not find node for func");
        //   continue;
        // }
        //
        // frame.FrameDetails.Function = function;
      }
    }
  }

  public void RegisterModuleDebugInfo(string moduleName, IDebugInfoProvider provider) {
    ModuleDebugInfo[moduleName] = provider;
  }

  public void AddModuleSample(int moduleId, TimeSpan weight) {
    ModuleWeights.AccumulateValue(moduleId, weight);
  }

  public void AddModuleCounter(string moduleName, int perfCounterId, long value) {
    if (!ModuleCounters.TryGetValue(moduleName, out var counterSet)) {
      counterSet = new PerformanceCounterValueSet();
      ModuleCounters[moduleName] = counterSet;
    }

    counterSet.AddCounterSample(perfCounterId, value);
  }

  public void RegisterPerformanceCounter(PerformanceCounter perfCounter) {
    perfCounter.Index = PerformanceCounters.Count;
    PerformanceCounters[perfCounter.Id] = perfCounter;
  }

  public PerformanceCounter GetPerformanceCounter(int id) {
    if (PerformanceCounters.TryGetValue(id, out var counter)) {
      return counter;
    }

    return null;
  }

  public PerformanceCounter FindPerformanceCounter(string name) {
    foreach (var pair in PerformanceCounters) {
      if (pair.Value.Name == name) {
        return pair.Value;
      }
    }

    return null;
  }

  public PerformanceMetric RegisterPerformanceMetric(int id, PerformanceMetricConfig config) {
    var baseCounter = FindPerformanceCounter(config.BaseCounterName);
    var relativeCounter = FindPerformanceCounter(config.RelativeCounterName);

    if (baseCounter != null && relativeCounter != null) {
      var metric = new PerformanceMetric(id, config, baseCounter, relativeCounter);
      PerformanceCounters[id] = metric;
      return metric;
    }

    return null;
  }

  public double ScaleFunctionWeight(TimeSpan weight) {
    return ProfileWeight.Ticks == 0 ? 0 : weight.Ticks / (double)ProfileWeight.Ticks;
  }

  public double ScaleModuleWeight(TimeSpan weight) {
    return TotalWeight.Ticks == 0 ? 0 : weight.Ticks / (double)TotalWeight.Ticks;
  }

  public FunctionProfileData GetFunctionProfile(IRTextFunction function) {
    if (FunctionProfiles.TryGetValue(function, out var profile)) {
      return profile;
    }

    return null;
  }

  public bool HasFunctionProfile(IRTextFunction function) {
    return GetFunctionProfile(function) != null;
  }

  public FunctionProfileData GetOrCreateFunctionProfile(IRTextFunction function,
                                                        FunctionDebugInfo debugInfo) {
    return FunctionProfiles.GetOrAdd(function, key => {
      return new FunctionProfileData {FunctionDebugInfo = debugInfo};
    });
  }

  public byte[] Serialize() {
    var state = new ProfileDataState(ProfileWeight, TotalWeight);
    state.PerformanceCounters = PerformanceCounters;
    state.ModuleWeights = ModuleWeights;
    state.Report = Report;
    state.CallTreeState = CallTree.Serialize();
    state.Samples = Samples;
    state.Process = Process;
    state.Threads = Threads;
    state.Modules = Modules;

    foreach (var pair in FunctionProfiles) {
      var funcId = new IRTextFunctionId(pair.Key);
      state.FunctionProfiles[funcId] = pair.Value;
    }

    return StateSerializer.Serialize(state);
  }

  public List<(IRTextFunction, FunctionProfileData)> GetSortedFunctions() {
    var list = FunctionProfiles.ToList();
    list.Sort((a, b) => -a.Item2.ExclusiveWeight.CompareTo(b.Item2.ExclusiveWeight));
    return list;
  }

  public void AddThreads(IEnumerable<ProfileThread> threads) {
    foreach (var thread in threads) {
      Threads[thread.ThreadId] = thread;
    }
  }

  public void AddModules(IEnumerable<ProfileImage> modules) {
    foreach (var module in modules) {
      Modules[module.Id] = module;
    }
  }

  public ProfileThread FindThread(int threadId) {
    if (Threads != null) {
      return Threads.GetValueOrNull(threadId);
    }

    return null;
  }

  public ProcessingResult FilterFunctionProfile(ProfileSampleFilter filter) {
    //? TODO: Split ProfileData into a part that has the samples and other info that doesn't change,
    //? while the rest is more like a processing result similar to FuncProfileData
    var currentProfile = new ProcessingResult {
      FunctionProfiles = FunctionProfiles,
      CallTree = CallTree,
      ModuleWeights = ModuleWeights,
      ProfileWeight = ProfileWeight,
      TotalWeight = TotalWeight,
      Filter = Filter
    };

    ModuleWeights = new Dictionary<int, TimeSpan>();
    FunctionProfiles = new ConcurrentDictionary<IRTextFunction, FunctionProfileData>();
    ProfileWeight = TimeSpan.Zero;
    TotalWeight = TimeSpan.Zero;

    var profile = ComputeProfile(this, filter, int.MaxValue);
    ModuleWeights = profile.ModuleWeights;
    ProfileWeight = profile.ProfileWeight;
    TotalWeight = profile.TotalWeight;
    FunctionProfiles = profile.FunctionProfiles;
    CallTree = profile.CallTree;
    Filter = filter;
    return currentProfile;
  }

  public ProcessingResult RestorePreviousProfile(ProcessingResult previousProfile) {
    var currentProfile = new ProcessingResult {
      FunctionProfiles = FunctionProfiles,
      CallTree = CallTree,
      ModuleWeights = ModuleWeights,
      ProfileWeight = ProfileWeight,
      TotalWeight = TotalWeight,
      Filter = Filter
    };

    ModuleWeights = previousProfile.ModuleWeights;
    ProfileWeight = previousProfile.ProfileWeight;
    TotalWeight = previousProfile.TotalWeight;
    FunctionProfiles = previousProfile.FunctionProfiles;
    CallTree = previousProfile.CallTree;
    Filter = previousProfile.Filter;
    return currentProfile;
  }

  public ProfileData ComputeProfile(ProfileData baseProfile, ProfileSampleFilter filter,
                                    int maxChunks = int.MaxValue) {
    // Compute the call tree in parallel with the per-function profiles.
    var tasks = new List<Task>();

    var callTreeTask = Task.Run(() => {
      return CallTreeProcessor.Compute(baseProfile, filter, maxChunks);
    });

    var funcProfileTask = Task.Run(() => {
      return FunctionProfileProcessor.Compute(baseProfile, filter, maxChunks);
    });

    tasks.Add(callTreeTask);
    Task.WhenAll(tasks.ToArray()).Wait();

    var profile = funcProfileTask.Result;
    profile.CallTree = callTreeTask.Result;
    return profile;
  }

  //? TODO: Port to ProfileSampleProcessor
  public ThreadSampleRanges ComputeThreadSampleRanges() {
    // Compute lists of contiguous range of samples running on the same thread,
    // used later to speed up the timeline slice computation and per-thread filtering.
    var threadSampleRanges = new Dictionary<int, List<ThreadSampleRange>>();

    int sampleIndex = 0;
    int prevThreadId = -1;
    int prevSampleIndex = -1;

    foreach (var (sample, stack) in Samples) {
      int threadId = stack.Context.ThreadId;

      if (threadId != prevThreadId) {
        if (prevThreadId != -1) {
          threadSampleRanges.GetOrAddValue(prevThreadId).Add(new ThreadSampleRange {
            StartIndex = prevSampleIndex,
            EndIndex = sampleIndex
          });
        }

        prevThreadId = threadId;
        prevSampleIndex = sampleIndex;
      }

      sampleIndex++;
    }

    if (prevThreadId != -1) {
      threadSampleRanges.GetOrAddValue(prevThreadId).Add(new ThreadSampleRange {
        StartIndex = prevSampleIndex,
        EndIndex = sampleIndex
      });
    }

    // Add an entry representing all threads, covering all samples.
    threadSampleRanges[-1] = new List<ThreadSampleRange> {
      new ThreadSampleRange {
        StartIndex = 0,
        EndIndex = sampleIndex
      }
    };

    ThreadSampleRanges = new ThreadSampleRanges(threadSampleRanges);
    return ThreadSampleRanges;
  }

  public class ProcessingResult {
    public ProfileSampleFilter Filter { get; set; }
    public ConcurrentDictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; set; }
    public ProfileCallTree CallTree { get; set; }
    public Dictionary<int, TimeSpan> ModuleWeights { get; set; }
    public TimeSpan ProfileWeight { get; set; }
    public TimeSpan TotalWeight { get; set; }

    public override string ToString() {
      return $"ProfileWeight: {ProfileWeight}, TotalWeight: {TotalWeight}, " +
             $"FunctionProfiles: {FunctionProfiles.Count}, CallTree: {CallTree}";
    }
  }

  [ProtoContract(SkipConstructor = true)]
  public class ProfileDataState {
    [ProtoMember(7)] public byte[] CallTreeState; //? TODO: Reimplement, super slow!

    public ProfileDataState(TimeSpan profileWeight, TimeSpan totalWeight) {
      ProfileWeight = profileWeight;
      TotalWeight = totalWeight;
      FunctionProfiles = new Dictionary<IRTextFunctionId, FunctionProfileData>();
    }

    [ProtoMember(1)] public TimeSpan ProfileWeight { get; set; }
    [ProtoMember(2)] public TimeSpan TotalWeight { get; set; }
    [ProtoMember(3)] public Dictionary<IRTextFunctionId, FunctionProfileData> FunctionProfiles { get; set; }
    [ProtoMember(4)] public Dictionary<int, PerformanceCounter> PerformanceCounters { get; set; }
    [ProtoMember(5)] public Dictionary<int, TimeSpan> ModuleWeights { get; set; }
    [ProtoMember(6)] public ProfileDataReport Report { get; set; }
    [ProtoMember(8)] public List<(ProfileSample Sample, ResolvedProfileStack Stack)> Samples { get; set; }
    [ProtoMember(9)] public ProfileProcess Process { get; set; }
    [ProtoMember(10)]
    public Dictionary<int, ProfileThread> Threads { get; set; }
    [ProtoMember(10)]
    public Dictionary<int, ProfileImage> Modules { get; set; }
  }

  [ProtoContract(SkipConstructor = true)]
  private class SampleStore {
    [ProtoMember(1)] public List<(ProfileSample Sample, ResolvedProfileStack Stack)> Samples { get; set; }
  }
}

[ProtoContract(SkipConstructor = true)]
public class IRTextFunctionReference {
  [ProtoMember(1)] public IRTextFunctionId Id;
  public IRTextFunction Value;

  public IRTextFunctionReference() {
  }

  public IRTextFunctionReference(IRTextFunction func) {
    Id = new IRTextFunctionId(func);
    Value = func;
  }

  public IRTextFunctionReference(IRTextFunctionId id, IRTextFunction func = null) {
    Id = id;
    Value = func;
  }

  public static implicit operator IRTextFunctionReference(IRTextFunction func) {
    return new IRTextFunctionReference(func);
  }

  public static implicit operator IRTextFunction(IRTextFunctionReference funcRef) {
    return funcRef.Value;
  }
}

// Represents a set of sample ranges for each thread.
public class ThreadSampleRanges {
  public ThreadSampleRanges(Dictionary<int, List<ThreadSampleRange>> ranges) {
    Ranges = ranges;
  }

  public Dictionary<int, List<ThreadSampleRange>> Ranges { get; set; }
}