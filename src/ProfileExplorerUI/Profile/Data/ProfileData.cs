// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.UI.Binary;
using ProfileExplorer.UI.Compilers;

namespace ProfileExplorer.UI.Profile;

public class ProfileData {
  public ProfileData(TimeSpan profileWeight, TimeSpan totalWeight) : this() {
    ProfileWeight = profileWeight;
    TotalWeight = totalWeight;
  }

  public ProfileData() {
    ProfileWeight = TimeSpan.Zero;
    FunctionProfiles = new Dictionary<IRTextFunction, FunctionProfileData>();
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
  public Dictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; set; }
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
      var sampleSpan = CollectionsMarshal.AsSpan(Samples);

      for (int i = 0; i < sampleSpan.Length; i++) {
        threadWeights.AccumulateValue(sampleSpan[i].Stack.Context.ThreadId,
                                      sampleSpan[i].Sample.Weight);
      }

      foreach ((int threadId, var weight) in threadWeights) {
        list.Add((threadId, weight));
      }

      list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
      return list;
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
    return FunctionProfiles.TryGetValue(function, out var profile) ? profile : null;
  }

  public bool HasFunctionProfile(IRTextFunction function) {
    return GetFunctionProfile(function) != null;
  }

  public FunctionProfileData GetOrCreateFunctionProfile(IRTextFunction function,
                                                        FunctionDebugInfo debugInfo) {
    ref var funcProfile =
      ref CollectionsMarshal.GetValueRefOrAddDefault(FunctionProfiles, function, out bool exists);

    if (!exists) {
      funcProfile = new FunctionProfileData(debugInfo);
    }

    return funcProfile;
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

  public List<int> FindModuleIds(Func<string, bool> matchCheck) {
    var ids = new List<int>();

    foreach (var module in Modules) {
      if (matchCheck(module.Value.ModuleName)) {
        ids.Add(module.Key);
      }
    }

    return ids;
  }

  public TimeSpan FindModulesWeight(Func<string, bool> matchCheck) {
    var ids = FindModuleIds(matchCheck);
    var weight = TimeSpan.Zero;

    foreach (int id in ids) {
      weight += ModuleWeights.GetValueOrDefault(id);
    }

    return weight;
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

    CallTree?.ResetTags();
    ModuleWeights = new Dictionary<int, TimeSpan>();
    FunctionProfiles = new Dictionary<IRTextFunction, FunctionProfileData>();
    ProfileWeight = TimeSpan.Zero;
    TotalWeight = TimeSpan.Zero;

    var profile = ComputeProfile(this, filter);
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
                                    bool computeCallTree = true,
                                    int maxChunks = int.MaxValue) {
    // Compute the call tree in parallel with the per-function profiles.
    var tasks = new List<Task>();

    if (maxChunks == int.MaxValue) {
      // Use half the threads for each task.
      maxChunks = Math.Max(1, App.Settings.GeneralSettings.CurrentCpuCoreLimit / 2);
    }

    var callTreeTask = Task.Run(() => {
      if (computeCallTree) {
        return CallTreeProcessor.Compute(baseProfile, filter, maxChunks);
      }

      return null;
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
    var sampleSpan = CollectionsMarshal.AsSpan(Samples);

    for (int i = 0; i < sampleSpan.Length; i++) {
      int threadId = sampleSpan[i].Stack.Context.ThreadId;

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
      new() {
        StartIndex = 0,
        EndIndex = sampleIndex
      }
    };

    ThreadSampleRanges = new ThreadSampleRanges(threadSampleRanges);
    return ThreadSampleRanges;
  }

  public class ProcessingResult {
    public ProfileSampleFilter Filter { get; set; }
    public Dictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; set; }
    public ProfileCallTree CallTree { get; set; }
    public Dictionary<int, TimeSpan> ModuleWeights { get; set; }
    public TimeSpan ProfileWeight { get; set; }
    public TimeSpan TotalWeight { get; set; }

    public override string ToString() {
      return $"ProfileWeight: {ProfileWeight}, TotalWeight: {TotalWeight}, " +
             $"FunctionProfiles: {FunctionProfiles.Count}, CallTree: {CallTree}";
    }
  }
}

// Represents a contiguous range of samples running on the same thread.
public struct ThreadSampleRange {
  public int StartIndex;
  public int EndIndex;
}

// Represents a set of sample ranges for each thread.
public class ThreadSampleRanges {
  public ThreadSampleRanges(Dictionary<int, List<ThreadSampleRange>> ranges) {
    Ranges = ranges;
  }

  public Dictionary<int, List<ThreadSampleRange>> Ranges { get; set; }
}