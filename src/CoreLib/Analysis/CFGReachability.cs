// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using CoreLib.IR;

namespace CoreLib.Analysis {
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
            reachableBlocks_ = new BitArray[maxBlockNumber_];

            foreach (var block in function_.Blocks) {
                reachableBlocks_[block.Number] = new BitArray(maxBlockNumber_);
                reachableBlocks_[block.Number].Set(block.Number, true);
            }
        }

        private void Compute() {
            //? TODO: This entire code is very inefficient
            //? A proper sparse bit-vector is needed.
            var currentValues = new BitArray(maxBlockNumber_);
            bool changed = true;

            while (changed) {
                changed = false;

                foreach (var block in function_.Blocks) {
                    currentValues.SetAll(false);
                    currentValues.Set(block.Number, true);

                    foreach (var predBlock in block.Predecessors) {
                        var inValues = reachableBlocks_[predBlock.Number];
                        currentValues.Or(inValues);
                    }

                    var outValues = reachableBlocks_[block.Number];

                    for (int i = 0; i < maxBlockNumber_; i++) {
                        if (currentValues[i] != outValues[i]) {
                            reachableBlocks_[block.Number] = new BitArray(currentValues);
                            changed = true;
                            break;
                        }
                    }
                }
            }
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
}
