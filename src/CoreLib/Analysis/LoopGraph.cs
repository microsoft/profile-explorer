// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.Analysis {
    public enum LoopKind {
        Natural,
        NonReducible
    }

    public sealed class Loop {
        public Loop() {
            Blocks = new HashSet<BlockIR>();
            NestedLoops = new List<Loop>();
        }

        public LoopKind Kind { get; set; }
        public int NestingLevel { get; set; }
        public Loop ParentLoop { get; set; }

        public HashSet<BlockIR> Blocks { get; set; }

        // public List<BlockIR> ExitBlocks { get; set; }
        // public BlockIR HeaderBlock {get;set;}
        public List<Loop> NestedLoops { get; set; }

        public Loop LoopNestRoot {
            get {
                var leader = this;
                var current = ParentLoop;
                var visitedBlocks = new List<Loop>(4);

                while (current != null) {
                    leader = current;
                    current = current.ParentLoop;

                    // With invalid CFGs this can happen, break the loop.
                    if (visitedBlocks.Contains(current)) {
                        break;
                    }

                    visitedBlocks.Add(current);
                }

                return leader;
            }
        }

        public void AddNestedLoop(Loop loop) {
            if (loop == this) {
                return;
            }

            if (!NestedLoops.Contains(loop)) {
                NestedLoops.Add(loop);
            }
        }
    }

    public sealed class LoopGraph {
        private Dictionary<BlockIR, Loop> blockLoopMap_;
        private FunctionIR function_;
        private List<Loop> functionLoops_;

        public LoopGraph(FunctionIR function) {
            function_ = function;
            functionLoops_ = new List<Loop>();
            blockLoopMap_ = new Dictionary<BlockIR, Loop>();

            foreach (var block in function_.Blocks) {
                block.RemoveTag<LoopBlockTag>();
            }
        }

        public bool HasLoops => functionLoops_.Count > 0;
        public List<Loop> Loops => functionLoops_;

        private void MarkLoopBlocks(int startId, int endId) {
            var loop = new Loop();
            functionLoops_.Add(loop);

            foreach (var block in function_.Blocks) {
                if (block.Number >= startId && block.Number <= endId) {
                    loop.Blocks.Add(block);
                    var loopTag = block.GetTag<LoopBlockTag>();

                    if (loopTag == null) {
                        loopTag = new LoopBlockTag(loop);
                        block.AddTag(loopTag);
                    }
                    else {
                        var nestedLoop = loopTag.Loop.LoopNestRoot;
                        loop.AddNestedLoop(nestedLoop);
                        loopTag.Loop.ParentLoop = loop;
                        loopTag.NestingLevel++;
                    }
                }
                else if (block.Number > endId) {
                    break;
                }
            }
        }

        public void FindLoops() {
            foreach (var block in function_.Blocks) {
                foreach (var succBlock in block.Successors) {
                    if (succBlock.Number <= block.Number) {
                        MarkLoopBlocks(succBlock.Number, block.Number);
                    }
                }
            }
        }
    }
}
