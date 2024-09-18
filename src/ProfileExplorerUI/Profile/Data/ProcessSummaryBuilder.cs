// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ProfileExplorer.UI.Profile;

public class ProcessSummaryBuilder {
  private RawProfileData profile_;
  private Dictionary<ProfileProcess, TimeSpan> processSamples_ = new();
  private Dictionary<ProfileProcess, (TimeSpan First, TimeSpan Last)> procDuration_ = new();
  private TimeSpan totalWeight_;

  public ProcessSummaryBuilder(RawProfileData profile) {
    profile_ = profile;
  }

  public void AddSample(ProfileSample sample) {
    var context = sample.GetContext(profile_);
    var process = profile_.GetOrCreateProcess(context.ProcessId);
    processSamples_.AccumulateValue(process, sample.Weight);
    totalWeight_ += sample.Weight;

    // Modify in-place.
    ref var durationRef = ref CollectionsMarshal.GetValueRefOrAddDefault(procDuration_, process, out bool found);

    if (!found) {
      durationRef.First = sample.Time;
    }

    durationRef.Last = sample.Time;
  }

  public void AddSample(TimeSpan sampleWeight, TimeSpan sampleTime, int processId) {
    var process = profile_.GetOrCreateProcess(processId);
    processSamples_.AccumulateValue(process, sampleWeight);
    totalWeight_ += sampleWeight;

    // Modify in-place.
    ref var durationRef = ref CollectionsMarshal.GetValueRefOrAddDefault(procDuration_, process, out bool found);

    if (!found) {
      durationRef.First = sampleTime;
    }

    durationRef.Last = sampleTime;
  }

  public List<ProcessSummary> MakeSummaries() {
    var list = new List<ProcessSummary>(procDuration_.Count);

    foreach (var pair in processSamples_) {
      var item = new ProcessSummary(pair.Key, pair.Value) {
        WeightPercentage = 100 * (double)pair.Value.Ticks / totalWeight_.Ticks
      };

      list.Add(item);

      if (procDuration_.TryGetValue(pair.Key, out var duration)) {
        item.Duration = duration.Last - duration.First;
      }
    }

    return list;
  }
}