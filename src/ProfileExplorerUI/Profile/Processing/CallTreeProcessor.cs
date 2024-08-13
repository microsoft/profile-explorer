// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ProfileExplorer.UI.Profile;

public sealed class CallTreeProcessor : ProfileSampleProcessor {
  public ProfileCallTree CallTree { get; set; } = new();
  private List<ProfileCallTree> chunks_;
  private int maxChunks_;

  public CallTreeProcessor(int maxChunks) {
    chunks_ = new();
    maxChunks_ = maxChunks;
  }

  public static ProfileCallTree Compute(ProfileData profile, ProfileSampleFilter filter,
                                        int maxChunks = int.MaxValue) {
    var funcProcessor = new CallTreeProcessor(maxChunks);
    funcProcessor.ProcessSampleChunk(profile, filter, maxChunks);
    return funcProcessor.CallTree;
  }

  protected override void ProcessSample(ref ProfileSample sample, ResolvedProfileStack stack,
                                        int sampleIndex, object chunkData) {
    var callTree = (ProfileCallTree)chunkData;
    callTree.UpdateCallTree(ref sample, stack);
  }

  protected override object InitializeChunk(int k, int samplesPerChunk) {
    // Partition the node IDs into namespaces based on the chunk
    // of samples they are created from - this ensures that each
    // call tree will usue unique node IDs when compared to other call trees.
    int startNodeId = k * (int.MaxValue / (maxChunks_ + 1));
    var chunk = new ProfileCallTree(startNodeId);

    lock (chunks_) {
      chunks_.Add(chunk);
    }

    return chunk;
  }

  protected override void Complete() {
    lock (chunks_) {
      // Multi-threaded merging of partial call trees.
      while (chunks_.Count > 1) {
        int step = Math.Min(chunks_.Count, 2);
        // Trace.WriteLine($"=> Merging {chunks_.Count} chunks, step {step}");

        var tasks = new Task[chunks_.Count / step];
        var newChunks = new List<ProfileCallTree>(chunks_.Count / step);

        for (int i = 0; i < chunks_.Count / step; i++) {
          var iCopy = i;
          newChunks.Add(chunks_[iCopy * step]);

          tasks[i] = Task.Run(() => {
            for (int k = 1; k < step; k++) {
              chunks_[iCopy * step].MergeWith(chunks_[iCopy * step + k]);
            }
          });
        }

        Task.WaitAll(tasks);

        // Handle any chunks that were not paired during the parallel phase.
        // With a step of 2 this can happen only in the first round.
        if (chunks_.Count % step != 0) {
          int lastHandledIndex = (chunks_.Count / step) * step;

          for (int i = lastHandledIndex; i < chunks_.Count; i++) {
            chunks_[0].MergeWith(chunks_[i]);
          }
        }

        chunks_ = newChunks;
      }

      CallTree = chunks_[0];
#if DEBUG
      CallTree.VerifyCycles();
#endif
    }
  }
}