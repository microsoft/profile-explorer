// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using IRExplorerCore;

namespace IRExplorerUI.Profile;

public sealed class FunctionProfileProcessor : ProfileSampleProcessor {
  private class ChunkData {
    public HashSet<int> StackModules = new();
    public HashSet<IRTextFunction> StackFunctions = new();
    public Dictionary<int, TimeSpan> ModuleWeights = new();
    public Dictionary<IRTextFunction, FunctionProfileData> FunctionProfiles = new();
    public TimeSpan TotalWeight = TimeSpan.Zero;
    public TimeSpan ProfileWeight = TimeSpan.Zero;
  }

  private ProfileSampleFilter filter_;
  private List<List<IRTextFunction>> filterStackFuncts_;
  private List<ChunkData> chunks_;

  private FunctionProfileProcessor(ProfileSampleFilter filter) {
    filter_ = filter;
    chunks_ = new();

    if (filter_ != null && filter_.FunctionInstances is {Count: > 0}) {
      // Compute once the list of functions on the path
      // from call tree root to the function instance node.
      filterStackFuncts_ = new List<List<IRTextFunction>>();

      foreach (var instance in filter_.FunctionInstances) {
        if (instance is ProfileCallTreeGroupNode groupNode) {
          foreach (var node in groupNode.Nodes) {
            AddInstanceFilter(node);
          }
        }
        else {
          AddInstanceFilter(instance);
        }
      }
    }
  }

  private void AddInstanceFilter(ProfileCallTreeNode node) {
    var stackFuncts = new List<IRTextFunction>();

    while (node != null) {
      stackFuncts.Add(node.Function);
      node = node.Caller;
    }

    stackFuncts.Reverse();
    filterStackFuncts_.Add(stackFuncts);
  }

  public ProfileData Profile { get; } = new();

  public static ProfileData Compute(ProfileData profile, ProfileSampleFilter filter,
                                    int maxChunks = int.MaxValue) {
    var funcProcessor = new FunctionProfileProcessor(filter);
    funcProcessor.ProcessSampleChunk(profile, filter, maxChunks);
    return funcProcessor.Profile;
  }

  protected override object InitializeChunk(int k, int samplesPerChunk) {
    var chunk = new ChunkData();

    lock (chunks_) {
      chunks_.Add(chunk);
    }

    return chunk;
  }

  protected override void ProcessSample(ref ProfileSample sample, ResolvedProfileStack stack,
                                        int sampleIndex, object chunkData) {
    if (filterStackFuncts_ != null) {
      // Filtering of functions to a single instance is enabled,
      // accept only samples that have the instance path nodes
      // as a prefix of the call stack, this accounts for total weight.
      if (stack.FrameCount < filterStackFuncts_.Count) {
        return;
      }

      bool foundMatch = false;

      foreach (var stackFuncts in filterStackFuncts_) {
        // Check if instance path nodes are a prefix of the call stack.
        if (stack.FrameCount < stackFuncts.Count) {
          continue;
        }

        bool isMatch = true;

        for (int i = 0; i < stackFuncts.Count; i++) {
          if (stackFuncts[i] !=
              stack.StackFrames[stack.FrameCount - i - 1].FrameDetails.Function) {
            isMatch = false;
            break;
          }
        }

        if (isMatch) {
          foundMatch = true;
          break;
        }
      }

      if (!foundMatch) {
        return;
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

      var frameDetails = resolvedFrame.FrameDetails;

      if (isTopFrame && data.StackModules.Add(frameDetails.Image.Id)) {
        data.ModuleWeights.AccumulateValue(frameDetails.Image.Id, sample.Weight);
      }

      long funcRva = frameDetails.DebugInfo.RVA;
      long frameRva = resolvedFrame.FrameRVA;
      var textFunction = frameDetails.Function;
      ref var funcProfile =
        ref CollectionsMarshal.GetValueRefOrAddDefault(data.FunctionProfiles, frameDetails.Function, out var exists);

      if (!exists) {
        funcProfile = new FunctionProfileData(frameDetails.DebugInfo);
      }

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

  protected override void Complete() {
    lock (chunks_) {
      while (chunks_.Count > 1) {
        int step = Math.Min(chunks_.Count, 2);
        // Trace.WriteLine($"=> Merging {chunks_.Count} chunks, step {step}");

        var tasks = new Task[chunks_.Count / step];
        var newChunks = new List<ChunkData>(chunks_.Count / step);

        for (int i = 0; i < chunks_.Count / step; i++) {
          var iCopy = i;
          newChunks.Add(chunks_[iCopy * step]);

          tasks[i] = Task.Run(() => {
            for (int k = 1; k < step; k++) {
              var destChunk = chunks_[iCopy * step];
              var sourceChunk = chunks_[iCopy * step + k];
              MergeChuncks(destChunk, sourceChunk);
            }
          });
        }

        Task.WaitAll(tasks);

        // Handle any chuncks that were not paired during the parallel phase.
        // With a step of 2 this can happen only in the first round.
        if (chunks_.Count % step != 0) {
          int lastHandledIndex = (chunks_.Count / step) * step;

          for (int i = lastHandledIndex; i < chunks_.Count; i++) {
            MergeChuncks(chunks_[0], chunks_[i]);
          }
        }

        chunks_ = newChunks;
      }

      lock (Profile) {
        Profile.FunctionProfiles = chunks_[0].FunctionProfiles;
      }
    }
  }

  private static void MergeChuncks(ChunkData destChunk, ChunkData sourceChunk) {
    foreach (var pair in sourceChunk.FunctionProfiles) {
      ref var existingValue =
        ref CollectionsMarshal.GetValueRefOrAddDefault(destChunk.FunctionProfiles, pair.Key,
                                                       out bool exists);

      if (exists) {
        existingValue.MergeWith(pair.Value);
      }
      else {
        // Copy over func. profile if missing.
        existingValue = pair.Value;
      }
    }
  }
}