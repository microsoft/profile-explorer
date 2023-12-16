using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IRExplorerCore;

namespace IRExplorerUI.Profile;

public partial class ProfileData {
  private sealed class CallTreeProcessor : ProfileSampleProcessor {
    public ProfileCallTree callTree_ = new ProfileCallTree();
    public ProfileCallTree CallTree => callTree_;

    //? TODO: Multi-threading disabled until merging of trees is impl.
    protected override int DefaultThreadCount => 1;

    public static ProfileCallTree Compute(ProfileData profile, ProfileSampleFilter filter,
                                          int maxChunks = int.MaxValue) {
      var funcProcessor = new CallTreeProcessor();
      funcProcessor.ProcessSampleChunk(profile, filter, 1);
      return funcProcessor.CallTree;
    }

    protected override void ProcessSample(ProfileSample sample, ResolvedProfileStack stack,
                                          int sampleIndex, object chunkData) {
      callTree_.UpdateCallTree(sample, stack);
    }
  }

  private sealed class FunctionProfileProcessor : ProfileSampleProcessor {
    private class ChunkData {
      public HashSet<int> stackModules = new ();
      public HashSet<IRTextFunction> stackFuncts = new();
      public Dictionary<int, TimeSpan> moduleWeights = new();
      public TimeSpan totalWeight = TimeSpan.Zero;
      public TimeSpan profileWeight = TimeSpan.Zero;
    }

    private ProfileData profile = new();
    public ProfileData Profile => profile;

    public static ProfileData Compute(ProfileData profile, ProfileSampleFilter filter,
                                      int maxChunks = int.MaxValue) {
      var funcProcessor = new FunctionProfileProcessor();
      funcProcessor.ProcessSampleChunk(profile, filter, maxChunks);
      return funcProcessor.Profile;
    }

    protected override object InitializeChunk(int k) {
      return new ChunkData();
    }

    protected override void ProcessSample(ProfileSample sample, ResolvedProfileStack stack,
                                          int sampleIndex, object chunkData) {
      var data = (ChunkData)chunkData;
      data.totalWeight += sample.Weight;
      data.profileWeight += sample.Weight;

      bool isTopFrame = true;
      data.stackModules.Clear();
      data.stackFuncts.Clear();

      foreach (var resolvedFrame in stack.StackFrames) {
        if (resolvedFrame.IsUnknown) {
          continue;
        }

        if (isTopFrame && data.stackModules.Add(resolvedFrame.FrameDetails.Image.Id)) {
          data.moduleWeights.AccumulateValue(resolvedFrame.FrameDetails.Image.Id, sample.Weight);
        }

        long funcRva = resolvedFrame.FrameDetails.DebugInfo.RVA;
        long frameRva = resolvedFrame.FrameRVA;
        var textFunction = resolvedFrame.FrameDetails.Function;
        var funcProfile =
          profile.GetOrCreateFunctionProfile(resolvedFrame.FrameDetails.Function,
                                             resolvedFrame.FrameDetails.DebugInfo);

        lock (funcProfile) {
          long offset = frameRva - funcRva;

          // Don't count the inclusive time for recursive functions multiple times.
          if (data.stackFuncts.Add(textFunction)) {
            funcProfile.AddInstructionSample(offset, sample.Weight);
            funcProfile.Weight += sample.Weight;

            // Set sample range covered by function.
            funcProfile.SampleStartIndex = Math.Min(funcProfile.SampleStartIndex, sampleIndex);
            funcProfile.SampleEndIndex = Math.Max(funcProfile.SampleEndIndex, sampleIndex);
          }

          // Count the exclusive time for the top frame function.
          if (isTopFrame) {
            funcProfile.ExclusiveWeight += sample.Weight;
          }
        }

        isTopFrame = false;
      }
    }

    protected override void CompleteChunk(int k, object chunkData) {
      var data = (ChunkData)chunkData;

      lock (profile) {
        profile.TotalWeight += data.totalWeight;
        profile.ProfileWeight += data.profileWeight;

        foreach ((int moduleId, var weight) in data.moduleWeights) {
          profile.AddModuleSample(moduleId, weight);
        }
      }
    }
  }

  // Provides support for running an analysis over samples, with filtering,
  // in parallel, by splitting the samples into multiple chunks, each
  // processed on a different thread.
  public abstract class ProfileSampleProcessor {
    protected virtual object InitializeChunk(int k) {
      return null;
    }

    protected abstract void ProcessSample(ProfileSample sample, ResolvedProfileStack stack,
                                          int sampleIndex, object chunkData);

    protected virtual void CompleteChunk(int k, object chunkData) {

    }

    protected virtual void Complete() {

    }

    protected virtual int DefaultThreadCount => Environment.ProcessorCount * 3 / 4;

    protected void ProcessSampleChunk(ProfileData profile, ProfileSampleFilter filter,
                                      int maxChunks = int.MaxValue) {
      int sampleStartIndex = filter.TimeRange?.StartSampleIndex ?? 0;
      int sampleEndIndex = filter.TimeRange?.EndSampleIndex ?? profile.Samples.Count;
      // Trace.WriteLine($"Sample range: {sampleStartIndex} - {sampleEndIndex}");

      int sampleCount = sampleEndIndex - sampleStartIndex;
      int chunks = Math.Min(maxChunks, DefaultThreadCount);
      // Trace.WriteLine($"Using {chunks} chunks");

      int chunkSize = sampleCount / chunks;
      var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, chunks);
      var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);
      var tasks = new List<Task>();

      for (int k = 0; k < chunks; k++) {
        int start = Math.Min(sampleStartIndex + k * chunkSize, sampleEndIndex);
        int end = Math.Min(sampleStartIndex + (k + 1) * chunkSize, sampleEndIndex);

        // If a single thread is selected, only process the samples for that thread
        // by going through the thread sample ranges.
        var ranges = profile.ThreadSampleRanges.Ranges[-1];

        if (filter.ThreadIds != null && filter.ThreadIds.Count == 1) {
          ranges = profile.ThreadSampleRanges.Ranges[filter.ThreadIds[0]];
          // Trace.WriteLine($"Filter single thread with {ranges.Count} ranges");
        }

        tasks.Add(taskFactory.StartNew(() => {
          var chunkData = InitializeChunk(k);

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
          for (int k = startRangeIndex; k <= endRangeIndex; k++) {
            var range = ranges[k];
            int startIndex = Math.Max(start, range.StartIndex);
            int endIndex = Math.Min(end, range.EndIndex);

            for (int i = startIndex; i < endIndex; i++) {
              var (sample, stack) = profile.Samples[i];

              if (filter.ThreadIds != null &&
                  !filter.ThreadIds.Contains(stack.Context.ThreadId)) {
                continue;
              }

              ProcessSample(sample, stack, k, chunkData);
            }
          }

          CompleteChunk(k, chunkData);
        }));
      }

      Task.WhenAll(tasks.ToArray()).Wait();
      Complete();
    }
  }
}