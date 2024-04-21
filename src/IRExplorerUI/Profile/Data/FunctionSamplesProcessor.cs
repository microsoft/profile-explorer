using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using IRExplorerCore.Utilities;

namespace IRExplorerUI.Profile;

public sealed class FunctionSamplesProcessor : ProfileSampleProcessor {
  private Dictionary<int, List<SampleIndex>> threadListMap_;
  private List<ChunkData> chunks_;
  private ProfileCallTreeNode node_;

  private static int EstimateSampleCount(ProfileCallTreeNode node) {
    return (int)node.Weight.TotalSeconds * 8000;
  }
  
  private class ChunkData {
    public ChunkData(ProfileCallTreeNode node) {
      AllThreadsList = new List<SampleIndex>(EstimateSampleCount(node));
      ThreadListMap = new Dictionary<int, List<SampleIndex>>();
      ThreadListMap[-1] = AllThreadsList;
    }

    public List<SampleIndex> AllThreadsList;
    public Dictionary<int, List<SampleIndex>> ThreadListMap;
  }

  public FunctionSamplesProcessor(ProfileCallTreeNode node) {
    node_ = node;
    chunks_ = new List<ChunkData>();
  }

  public static Dictionary<int, List<SampleIndex>>
    Compute(ProfileCallTreeNode node,
            ProfileData profile,
            ProfileSampleFilter filter,
            int maxChunks = int.MaxValue) {
    // Compute the list of samples associated with the function,
    // for each thread it was executed on.
    var funcProcessor = new FunctionSamplesProcessor(node);
    funcProcessor.ProcessSampleChunk(profile, filter, maxChunks);
    return funcProcessor.threadListMap_;
  }

  protected override object InitializeChunk(int k) {
    var chunk = new ChunkData(node_);

    lock (chunks_) {
      chunks_.Add(chunk);
    }

    return chunk;
  }

  protected override void ProcessSample(ProfileSample sample, ResolvedProfileStack stack, int sampleIndex, object chunkData) {
    var data = (ChunkData)chunkData;
    var currentNode = node_;
    bool match = false;

    for (int k = 0; k < stack.StackFrames.Count; k++) {
      var stackFrame = stack.StackFrames[k];

      if (stackFrame.IsUnknown) {
        continue;
      }

      if (currentNode == null || currentNode.IsGroup) {
        // Mismatch along the call path leading to the function.
        match = false;
        break;
      }

      if (stackFrame.FrameDetails.Function.Equals(currentNode.Function)) {
        // Continue checking if the callers show up on the stack trace
        // to make the search context-sensitive.
        match = true;
        currentNode = currentNode.Caller;
      }
      else if (match) {
        // Mismatch along the call path leading to the function.
        match = false;
        break;
      }
    }

    if (match) {
      var threadList = data.ThreadListMap.GetOrAddValue(stack.Context.ThreadId,
        () => new List<SampleIndex>(EstimateSampleCount(node_)));
      var index = new SampleIndex(sampleIndex, sample.Time);
      threadList.Add(index);
      data.AllThreadsList.Add(index);
    }
  }

  protected override void Complete() {
    // Compute the sample list size for each thread
    // across all chunks to pre-allocate memory.
    var countMap = new Dictionary<int, int>();

    foreach (var chunk in chunks_) {
      foreach (var pair in chunk.ThreadListMap) {
        countMap.AccumulateValue(pair.Key, pair.Value.Count);
      }
    }

    threadListMap_ = new Dictionary<int, List<SampleIndex>>(countMap.Count);

    foreach (var pair in countMap) {
      threadListMap_[pair.Key] = new List<SampleIndex>(pair.Value);
    }

    // Merge the per-thread sample lists.
    foreach (var chunk in chunks_) {
      foreach (var pair in chunk.ThreadListMap) {
        threadListMap_[pair.Key].AddRange(pair.Value);
      }
    }

    // Sort each list in parallel, since samples may not be in order.
    var sortTasks = new List<Task>();
    
    foreach (var pair in threadListMap_) {
      var list = pair.Value;
      sortTasks.Add(Task.Run(() => list.Sort((a, b) => a.Index.CompareTo(b.Index))));
    }
    
    Task.WaitAll(sortTasks.ToArray());
  }
}