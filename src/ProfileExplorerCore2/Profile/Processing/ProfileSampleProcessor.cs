// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ProfileExplorerCore2.Profile.Data;
using ProfileExplorerCore2.Utilities;

namespace ProfileExplorerCore2.Profile.Processing;

// Provides support for running an analysis over samples, with filtering,
// in parallel, by splitting the samples into multiple chunks, each
// processed on a different thread.
public abstract class ProfileSampleProcessor {
  protected virtual int DefaultThreadCount => CoreSettingsProvider.GeneralSettings.CurrentCpuCoreLimit;

  protected virtual object InitializeChunk(int k, int samplesPerChunk) {
    return null;
  }

  protected abstract void ProcessSample(ref ProfileSample sample, ResolvedProfileStack stack,
                                        int sampleIndex, object chunkData);

  protected virtual void CompleteChunk(int k, object chunkData) {
  }

  protected virtual void Complete() {
  }

  protected void ProcessSampleChunk(ProfileData profile, ProfileSampleFilter filter,
                                    int maxChunks = int.MaxValue) {
    int sampleStartIndex = filter.TimeRange?.StartSampleIndex ?? 0;
    int sampleEndIndex = filter.TimeRange?.EndSampleIndex ?? profile.Samples.Count;
    //Trace.WriteLine($"ProfileSampleProcessor: Sample range: {sampleStartIndex} - {sampleEndIndex}");

    int sampleCount = sampleEndIndex - sampleStartIndex;
    int chunks = Math.Min(maxChunks, DefaultThreadCount);
    //Trace.WriteLine($"ProfileSampleProcessor: Using {chunks} chunks");
    //var sw = Stopwatch.StartNew();

    int chunkSize = sampleCount / chunks;
    var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, chunks);
    var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);
    var tasks = new List<Task>();

    for (int k = 0; k < chunks; k++) {
      int start = Math.Min(sampleStartIndex + k * chunkSize, sampleEndIndex);
      int end = k == chunks - 1 ? sampleEndIndex : Math.Min(sampleStartIndex + (k + 1) * chunkSize, sampleEndIndex);

      // If a single thread is selected, only process the samples for that thread
      // by going through the thread sample ranges.
      var ranges = profile.ThreadSampleRanges.Ranges[-1];

      if (filter.HasThreadFilter && filter.ThreadIds.Count == 1) {
        ranges = profile.ThreadSampleRanges.Ranges[filter.ThreadIds[0]];
        // Trace.WriteLine($"Filter single thread with {ranges.Count} ranges");
      }

      int kCopy = k; // Pass value copy.
      tasks.Add(taskFactory.StartNew(() => {
        int samplesPerChunk = (int)Math.Ceiling((double)sampleCount / chunks);
        object chunkData = InitializeChunk(kCopy, samplesPerChunk);

        // Find the ranges of samples that overlap with the filter time range.
        int startRangeIndex = 0;
        int endRangeIndex = ranges.Count - 1;

        while (startRangeIndex < ranges.Count && ranges[startRangeIndex].EndIndex < start) {
          startRangeIndex++;
        }

        while (endRangeIndex > 0 && ranges[endRangeIndex].StartIndex > end) {
          endRangeIndex--;
        }

        // Walk each sample in the range and update the function profile.
        bool hasThreadFilter = filter.HasThreadFilter;
        var sampleSpan = CollectionsMarshal.AsSpan(profile.Samples);

        for (int k = startRangeIndex; k <= endRangeIndex; k++) {
          var range = ranges[k];
          int startIndex = Math.Max(start, range.StartIndex);
          int endIndex = Math.Min(end, range.EndIndex);

          for (int i = startIndex; i < endIndex; i++) {
            ref var stack = ref sampleSpan[i].Stack;

            if (hasThreadFilter &&
                !filter.ThreadIds.Contains(stack.Context.ThreadId)) {
              continue;
            }

            ref var sample = ref sampleSpan[i].Sample;
            ProcessSample(ref sample, stack, i, chunkData);
          }
        }

        CompleteChunk(kCopy, chunkData);
      }));
    }

    Task.WhenAll(tasks.ToArray()).Wait();
    Complete();

    //sw.Stop();
    //Trace.WriteLine($"ProfileSampleProcessor: Time {sw.ElapsedMilliseconds} ms");
    //Trace.Flush();
  }
}