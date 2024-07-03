// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace IRExplorerUI.Profile;

public sealed class CallTreeProcessor : ProfileSampleProcessor {
  public ProfileCallTree CallTree { get; set; } = new();
  private List<ProfileCallTree> chunks_;

  public CallTreeProcessor() {
    chunks_ = new();
  }

  public static ProfileCallTree Compute(ProfileData profile, ProfileSampleFilter filter,
                                        int maxChunks = int.MaxValue) {
    var funcProcessor = new CallTreeProcessor();
    funcProcessor.ProcessSampleChunk(profile, filter, 8);
    return funcProcessor.CallTree;
  }

  protected override void ProcessSample(ref ProfileSample sample, ResolvedProfileStack stack,
                                        int sampleIndex, object chunkData) {
    var callTree = (ProfileCallTree)chunkData;
    callTree.UpdateCallTree(ref sample, stack);
  }

  protected override object InitializeChunk(int k) {
    var chunk = new ProfileCallTree();

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

        // Handle any chuncks that were not paired during the parallel phase.
        // With a step of 2 this can happen only in the first round.
        if (chunks_.Count % step != 0) {
          int lastHandledIndex = (chunks_.Count / step) * step;

          for (int i = lastHandledIndex; i < chunks_.Count; i++) {
            Trace.WriteLine($"Merge extra {i}");
            chunks_[0].MergeWith(chunks_[i]);
          }
        }

        chunks_ = newChunks;
      }

      CallTree = chunks_[0];
    }
  }
}