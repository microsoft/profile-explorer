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
  public Stopwatch sw;
  

  public CallTreeProcessor() {
    chunks_ = new();
    
    //? TODO: Remove Stopwatch, pick thread count as half of logical processors from caller.
    sw = Stopwatch.StartNew();
  }

  public static ProfileCallTree Compute(ProfileData profile, ProfileSampleFilter filter,
                                        int maxChunks = int.MaxValue) {
    var funcProcessor = new CallTreeProcessor();
    var sw = Stopwatch.StartNew();
    funcProcessor.ProcessSampleChunk(profile, filter, 8);

    Trace.WriteLine($"=> Tree Done: {sw.ElapsedMilliseconds} ms");
    Trace.Flush();
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
      Trace.WriteLine($"=> All chunks in {sw.ElapsedMilliseconds} ms");
      sw.Restart();
      
      Trace.WriteLine($"=> Used {chunks_.Count} chunks");

      // Multi-threaded merging of partial call trees.
      while (chunks_.Count > 1) {
        int step = Math.Min(chunks_.Count, 2);
        Trace.WriteLine($"=> Merging {chunks_.Count} chunks, step {step}");

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
        chunks_ = newChunks;
      }

      sw.Stop();
      Trace.WriteLine($"=> Merge trees in {sw.ElapsedMilliseconds} ms");
      CallTree = chunks_[0];
    }
  }
}