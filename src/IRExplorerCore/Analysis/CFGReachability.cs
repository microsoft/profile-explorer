// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.Analysis;

public class CFGReachability {
  private FunctionIR function_;
  private int maxBlockNumber_;
  private BitArray[] reachableBlocks_;

  public CFGReachability(FunctionIR function) {
    function_ = function;
    maxBlockNumber_ = -1;

    function_.Blocks.ForEach(item => maxBlockNumber_ =
                               Math.Max(item.Number, maxBlockNumber_));

    maxBlockNumber_++;
    InitializeBitVectors();
    Compute();
  }

  //? TODO: Algo cares only a bit being set at all
  public static int GetCardinality(BitArray bitArray) {
    int[] ints = new int[(bitArray.Count >> 5) + 1];

    bitArray.CopyTo(ints, 0);

    int count = 0;

    // fix for not truncated bits in last integer that may have been set to true with SetAll()
    ints[ints.Length - 1] &= ~(-1 << bitArray.Count % 32);

    for (int i = 0; i < ints.Length; i++) {
      int c = ints[i];

      // magic (http://graphics.stanford.edu/~seander/bithacks.html#CountBitsSetParallel)
      unchecked {
        c = c - (c >> 1 & 0x55555555);
        c = (c & 0x33333333) + (c >> 2 & 0x33333333);
        c = (c + (c >> 4) & 0xF0F0F0F) * 0x1010101 >> 24;
      }

      count += c;
    }

    return count;
  }

  public bool Reaches(BlockIR block, BlockIR targetBlock) {
    return reachableBlocks_[targetBlock.Number].Get(block.Number);
  }

  public List<BlockIR> FindPath(BlockIR block, BlockIR targetBlock) {
    if (!Reaches(block, targetBlock)) {
      return new List<BlockIR>();
    }

    var visited = new HashSet<BlockIR>(maxBlockNumber_);
    var worklist = new List<PathBlock>(maxBlockNumber_);
    worklist.Add(new PathBlock(block));
    visited.Add(block);

    while (worklist.Count > 0) {
      var current = worklist[worklist.Count - 1];
      worklist.RemoveAt(worklist.Count - 1);

      if (current.Block == targetBlock) {
        var pathBlocks = new List<BlockIR>(current.Distance + 1);

        while (current != null) {
          pathBlocks.Add(current.Block);
          current = current.Previous;
        }

        return pathBlocks;
      }

      foreach (var succBlock in current.Block.Successors) {
        if (!visited.Contains(succBlock)) {
          worklist.Add(new PathBlock(succBlock, current.Distance + 1, current));
          visited.Add(succBlock);
        }
      }
    }

    return new List<BlockIR>();
  }

  private void InitializeBitVectors() {
    reachableBlocks_ = new BitArray[maxBlockNumber_ + 1];

    for (int i = 0; i <= maxBlockNumber_; i++) {
      reachableBlocks_[i] = new BitArray(maxBlockNumber_ + 1);
    }

    if (function_.EntryBlock != null) {
      reachableBlocks_[function_.EntryBlock.Number].Set(function_.EntryBlock.Number, true);
    }
  }

  private void Compute() {
    //? TODO: This entire code is very inefficient
    //? A proper sparse bit-vector is needed.
    var currentValues = new BitArray(maxBlockNumber_ + 1);
    bool changed = true;
    //var sw = Stopwatch.StartNew();

    var blockOrdering = new CFGBlockOrdering(function_);

    while (changed) {
      changed = false;

      //foreach (var block in function_.Blocks) {
      blockOrdering.ReversePostorderWalk((block, _) => {
        if (block.Predecessors.Count == 0) {
          return true;
        }

        currentValues.SetAll(false);

        foreach (var predBlock in block.Predecessors) {
          var inValues = reachableBlocks_[predBlock.Number];
          currentValues.Or(inValues);
        }

        int popcnt = GetCardinality(currentValues);

        if (popcnt > 0) {
          currentValues.Set(block.Number, true);
        }

        var outValues = reachableBlocks_[block.Number];

        for (int i = 0; i < maxBlockNumber_; i++) {
          if (currentValues[i] != outValues[i]) {
            reachableBlocks_[block.Number] = new BitArray(currentValues);
            changed = true;
            break;
          }
        }

        return true;
      });
    }

    //sw.Stop();
    //Trace.WriteLine($"Time {sw.ElapsedMilliseconds}");
    //Trace.Flush();
  }

  private class PathBlock {
    public BlockIR Block;
    public int Distance;
    public PathBlock Previous;

    public PathBlock(BlockIR block, int distance = 0, PathBlock previous = null) {
      Block = block;
      Distance = distance;
      Previous = previous;
    }
  }
}
