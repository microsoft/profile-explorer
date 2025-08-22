// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using ProfileExplorerCore2;

namespace ProfileExplorer.UI.Profile;

public sealed class FunctionsForSamplesProcessor : ProfileSampleProcessor {
  private HashSet<IRTextFunction> functionSet_;
  private List<ChunkData> chunks_;

  public FunctionsForSamplesProcessor() {
    chunks_ = new List<ChunkData>();
  }

  public static HashSet<IRTextFunction>
    Compute(ProfileSampleFilter filter,
            ProfileData profile,
            int maxChunks = int.MaxValue) {
    // Compute the list of functions covered by the samples
    // on the specified thread or all threads.
    var funcProcessor = new FunctionsForSamplesProcessor();
    funcProcessor.ProcessSampleChunk(profile, filter, maxChunks);
    return funcProcessor.functionSet_;
  }

  protected override object InitializeChunk(int k, int samplesPerChunk) {
    var chunk = new ChunkData();

    lock (chunks_) {
      chunks_.Add(chunk);
    }

    return chunk;
  }

  protected override void ProcessSample(ref ProfileSample sample, ResolvedProfileStack stack, int sampleIndex,
                                        object chunkData) {
    var data = (ChunkData)chunkData;

    foreach (var stackFrame in stack.StackFrames) {
      if (!stackFrame.IsUnknown) {
        data.functionSet_.Add(stackFrame.FrameDetails.Function);
      }
    }
  }

  protected override void Complete() {
    // Compute the sample list size for each thread
    // across all chunks to pre-allocate memory.
    int count = 0;

    foreach (var chunk in chunks_) {
      count += chunk.functionSet_.Count;
    }

    functionSet_ = new HashSet<IRTextFunction>(count);

    // Merge the per-thread sample lists.
    foreach (var chunk in chunks_) {
      foreach (var func in chunk.functionSet_) {
        functionSet_.Add(func);
      }
    }
  }

  private class ChunkData {
    public HashSet<IRTextFunction> functionSet_ = new(1024);
  }
}