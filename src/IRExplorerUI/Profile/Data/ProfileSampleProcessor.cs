using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using IRExplorerCore;

namespace IRExplorerUI.Profile;

public partial class ProfileData {
  private sealed class CallTreeProcessor : ProfileSampleProcessor {
    public ProfileCallTree CallTree { get; } = new ProfileCallTree();

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
      CallTree.UpdateCallTree(sample, stack);
    }
  }

  private sealed class FunctionProfileProcessor : ProfileSampleProcessor {
    private class ChunkData {
      public HashSet<int> StackModules = new HashSet<int>();
      public HashSet<IRTextFunction> StackFunctions = new HashSet<IRTextFunction>();
      public Dictionary<int, TimeSpan> ModuleWeights = new Dictionary<int, TimeSpan>();
      public TimeSpan TotalWeight = TimeSpan.Zero;
      public TimeSpan ProfileWeight = TimeSpan.Zero;
    }

    private ProfileSampleFilter filter_;
    private List<IRTextFunction> filterStackFuncts_;

    private FunctionProfileProcessor(ProfileSampleFilter filter) {
      filter_ = filter;

      if (filter_ != null) {
        // Compute once the list of functions on the path
        // from call tree root to the function instance node.
        filterStackFuncts_ = new List<IRTextFunction>();
        var node = filter_.FunctionInstance;

        while (node != null) {
          filterStackFuncts_.Add(node.Function);
          node = node.Caller;
        }

        filterStackFuncts_.Reverse();
      }
    }

    public ProfileData Profile { get; } = new ProfileData();

    public static ProfileData Compute(ProfileData profile, ProfileSampleFilter filter,
                                      int maxChunks = int.MaxValue) {
      var funcProcessor = new FunctionProfileProcessor(filter);
      funcProcessor.ProcessSampleChunk(profile, filter, maxChunks);
      return funcProcessor.Profile;
    }

    protected override object InitializeChunk(int k) {
      return new ChunkData();
    }

    protected override void ProcessSample(ProfileSample sample, ResolvedProfileStack stack,
                                          int sampleIndex, object chunkData) {
      if (filterStackFuncts_ != null) {
        // Filtering of functions to a single instance is enabled,
        // accept only samples that have the instance path nodes
        // as a prefix of the call stack, this accounts for total weight.
        if (stack.FrameCount < filterStackFuncts_.Count) {
          return;
        }

        for (int i = 0; i < filterStackFuncts_.Count; i++) {
          if (filterStackFuncts_[i] !=
              stack.StackFrames[stack.FrameCount - i - 1].FrameDetails.Function) {
            return;
          }
        }
      }

      var data = (ChunkData)chunkData;
      data.TotalWeight += sample.Weight;
      data.ProfileWeight += sample.Weight;

      bool isTopFrame = true;
      data.StackModules.Clear();
      data.StackFunctions.Clear();

      foreach (var resolvedFrame in stack.StackFrames) {
        if (resolvedFrame.IsUnknown) {
          continue;
        }

        if (isTopFrame && data.StackModules.Add(resolvedFrame.FrameDetails.Image.Id)) {
          data.ModuleWeights.AccumulateValue(resolvedFrame.FrameDetails.Image.Id, sample.Weight);
        }

        long funcRva = resolvedFrame.FrameDetails.DebugInfo.RVA;
        long frameRva = resolvedFrame.FrameRVA;
        var textFunction = resolvedFrame.FrameDetails.Function;
        var funcProfile =
          Profile.GetOrCreateFunctionProfile(resolvedFrame.FrameDetails.Function,
                                             resolvedFrame.FrameDetails.DebugInfo);

        lock (funcProfile) {
          long offset = frameRva - funcRva;

          // Don't count the inclusive time for recursive functions multiple times.
          if (data.StackFunctions.Add(textFunction)) {
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

      lock (Profile) {
        Profile.TotalWeight += data.TotalWeight;
        Profile.ProfileWeight += data.ProfileWeight;

        foreach ((int moduleId, var weight) in data.ModuleWeights) {
          Profile.AddModuleSample(moduleId, weight);
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
          object chunkData = InitializeChunk(k);

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

              ProcessSample(sample, stack, i, chunkData);
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

public class ProfileSampleFilter : IEquatable<ProfileSampleFilter> {
  public SampleTimeRangeInfo TimeRange { get; set; }
  public List<int> ThreadIds { get; set; }
  public ProfileCallTreeNode FunctionInstance { get; set; }
  public bool IncludesAll => TimeRange == null &&
                             FunctionInstance == null &&
                             (ThreadIds == null || ThreadIds.Count == 0);

  public static bool operator ==(ProfileSampleFilter left, ProfileSampleFilter right) {
    return Equals(left, right);
  }

  public static bool operator !=(ProfileSampleFilter left, ProfileSampleFilter right) {
    return !Equals(left, right);
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj))
      return false;
    if (ReferenceEquals(this, obj))
      return true;
    if (obj.GetType() != GetType())
      return false;
    return Equals((ProfileSampleFilter)obj);
  }

  public override int GetHashCode() {
    return HashCode.Combine(TimeRange, ThreadIds, FunctionInstance);
  }

  public override string ToString() {
    return $"TimeRange: {TimeRange}, FunctionInstance: {FunctionInstance}, ThreadIds: {ThreadIds}";
  }

  public bool Equals(ProfileSampleFilter other) {
    if (ReferenceEquals(null, other))
      return false;
    if (ReferenceEquals(this, other))
      return true;
    return Equals(TimeRange, other.TimeRange) && Equals(ThreadIds, other.ThreadIds);
  }
}