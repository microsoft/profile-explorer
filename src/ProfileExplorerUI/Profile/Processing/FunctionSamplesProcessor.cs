// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Diagnostics;
using ProfileExplorerCore2.Utilities;

namespace ProfileExplorer.UI.Profile;

public sealed class FunctionSamplesProcessor : ProfileSampleProcessor {
  private const int AllThreadsKey = -1;
  private Dictionary<int, List<SampleIndex>> threadListMap_;
  private List<ChunkData> chunks_;
  private ProfileCallTreeNode node_;

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

  protected override object InitializeChunk(int k, int samplesPerChunk) {
    var chunk = new ChunkData();

    lock (chunks_) {
      chunks_.Add(chunk);
    }

    return chunk;
  }

  protected override void ProcessSample(ref ProfileSample sample, ResolvedProfileStack stack,
                                        int sampleIndex, object chunkData) {
    var data = (ChunkData)chunkData;
    var currentNode = node_;
    bool match = false;

    for (int k = 0; k < stack.StackFrames.Count; k++) {
      if (currentNode == null || currentNode.IsGroup) {
        // Mismatch along the call path leading to the function.
        match = false;
        break;
      }

      var stackFrame = stack.StackFrames[k];

      if (stackFrame.IsUnknown) {
        continue;
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
      var threadList = data.ThreadListMap.GetOrAddValue(stack.Context.ThreadId);
      var index = new SampleIndex(sampleIndex, sample.Time);
      threadList.Add(index);
      data.AllThreadsList.Add(index);
    }
  }

  protected override void Complete() {
    lock (chunks_) {
      // Compute the sample list size for each thread
      // across all chunks to pre-allocate memory.
      var countMap = new Dictionary<int, int>();
      var chunkThreadListMap = new Dictionary<int, List<List<SampleIndex>>>();

      foreach (var chunk in chunks_) {
        foreach (var pair in chunk.ThreadListMap) {
          countMap.AccumulateValue(pair.Key, pair.Value.Count);

          if (pair.Value.Count > 0) {
            chunkThreadListMap.GetOrAddValue(pair.Key).Add(pair.Value);
          }
        }
      }

      // Pre-allocate memory for the merged per-thread sample lists.
      threadListMap_ = new Dictionary<int, List<SampleIndex>>(countMap.Count);

      foreach (var pair in countMap) {
        threadListMap_[pair.Key] = new List<SampleIndex>(pair.Value);
      }

      // The per-thread sample lists are already sorted,
      // now put them in the correct order across all chunks.
      foreach (var chunkList in chunkThreadListMap.Values) {
        chunkList.Sort((a, b) => a[0].Index.CompareTo(b[0].Index));
      }

      var map = new Dictionary<int, int>();

      // Merge the per-thread sample lists.
      foreach (var pair in chunkThreadListMap) {
        var threadList = threadListMap_[pair.Key];
        var chunkLists = pair.Value;

        foreach (var list in chunkLists) {
          threadList.AddRange(list);
        }

        for (int i = 1; i < threadList.Count; i++) {
          int dist = threadList[i].Index - threadList[i - 1].Index;
          map.AccumulateValue(dist, 1);
        }

#if DEBUG
        // Validate sample ordering.
        for (int i = 1; i < chunkLists.Count; i++) {
          Debug.Assert(chunkLists[i][0].Index > chunkLists[i - 1][^1].Index);
        }
#endif
      }
    }
  }

  private class ChunkData {
    public List<SampleIndex> AllThreadsList;
    public Dictionary<int, List<SampleIndex>> ThreadListMap;

    public ChunkData() {
      AllThreadsList = new List<SampleIndex>();
      ThreadListMap = new Dictionary<int, List<SampleIndex>>();
      ThreadListMap[AllThreadsKey] = AllThreadsList;
    }
  }
}