// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Profile.Data;

public class ProcessSummaryBuilder {
  private RawProfileData profile_;
  private Dictionary<int, TimeSpan> processSamples_ = new();
  private Dictionary<int, (TimeSpan First, TimeSpan Last)> procDuration_ = new();
  private TimeSpan totalWeight_;

  public ProcessSummaryBuilder(RawProfileData profile) {
    profile_ = profile;
  }

  public void AddSample(ProfileSample sample) {
    var context = sample.GetContext(profile_);
    int processId = context.ProcessId;
    profile_.GetOrCreateProcess(processId); // Ensure process object exists.
    processSamples_.AccumulateValue(processId, sample.Weight);
    totalWeight_ += sample.Weight;

    // Modify in-place.
    ref var durationRef = ref CollectionsMarshal.GetValueRefOrAddDefault(procDuration_, processId, out bool found);

    if (!found) {
      durationRef.First = sample.Time;
    }

    durationRef.Last = sample.Time;
  }

  public void AddSample(TimeSpan sampleWeight, TimeSpan sampleTime, int processId) {
    profile_.GetOrCreateProcess(processId); // Ensure process object exists.
    processSamples_.AccumulateValue(processId, sampleWeight);
    totalWeight_ += sampleWeight;

    // Modify in-place.
    ref var durationRef = ref CollectionsMarshal.GetValueRefOrAddDefault(procDuration_, processId, out bool found);

    if (!found) {
      durationRef.First = sampleTime;
    }

    durationRef.Last = sampleTime;
  }

  public List<ProcessSummary> MakeSummaries() {
    var list = new List<ProcessSummary>(procDuration_.Count);

    // Calculate non-idle total weight for the excluding-idle percentage.
    long nonIdleWeightTicks = totalWeight_.Ticks;

    if (processSamples_.TryGetValue(ETW.ETWEventProcessor.KernelProcessId, out var idleWeight)) {
      nonIdleWeightTicks -= idleWeight.Ticks;
    }

    foreach (var pair in processSamples_) {
      var process = profile_.GetOrCreateProcess(pair.Key);

      double weightPercentage = totalWeight_.Ticks > 0
        ? 100 * (double)pair.Value.Ticks / totalWeight_.Ticks
        : 0;

      double weightPercentageExcludingIdle;
      if (nonIdleWeightTicks > 0) {
        if (pair.Key == ETW.ETWEventProcessor.KernelProcessId) {
          // For the idle/kernel process, the excluding-idle percentage is not meaningful.
          // Set it equal to the overall weight percentage to avoid confusing values.
          weightPercentageExcludingIdle = weightPercentage;
        } else {
          weightPercentageExcludingIdle = 100 * (double)pair.Value.Ticks / nonIdleWeightTicks;
        }
      } else {
        weightPercentageExcludingIdle = 0;
      }

      var item = new ProcessSummary(process, pair.Value) {
        WeightPercentage = weightPercentage,
        WeightPercentageExcludingIdle = weightPercentageExcludingIdle
      };

      list.Add(item);

      if (procDuration_.TryGetValue(pair.Key, out var duration)) {
        item.Duration = duration.Last - duration.First;
      }
    }

    return list;
  }
}
