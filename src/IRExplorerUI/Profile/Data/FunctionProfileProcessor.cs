using System;
using System.Collections.Generic;
using IRExplorerCore;

namespace IRExplorerUI.Profile;

public sealed class FunctionProfileProcessor : ProfileSampleProcessor {
  private class ChunkData {
    public HashSet<int> StackModules = new HashSet<int>();
    public HashSet<IRTextFunction> StackFunctions = new HashSet<IRTextFunction>();
    public Dictionary<int, TimeSpan> ModuleWeights = new Dictionary<int, TimeSpan>();
    public TimeSpan TotalWeight = TimeSpan.Zero;
    public TimeSpan ProfileWeight = TimeSpan.Zero;
  }

  private ProfileSampleFilter filter_;
  private List<List<IRTextFunction>> filterStackFuncts_;

  private FunctionProfileProcessor(ProfileSampleFilter filter) {
    filter_ = filter;

    if (filter_ != null && filter_.FunctionInstances is {Count:> 0}) {
      // Compute once the list of functions on the path
      // from call tree root to the function instance node.
      filterStackFuncts_ = new List<List<IRTextFunction>>();
      
      foreach(var instance in  filter_.FunctionInstances) {
        if (instance is ProfileCallTreeGroupNode groupNode) {
          foreach(var node in groupNode.Nodes) {
            AddInstanceFilter(node);
          }
        } else {
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

      bool foundMatch = false;
      
      foreach(var stackFuncts in filterStackFuncts_) {
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
      
      if(!foundMatch) {
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
