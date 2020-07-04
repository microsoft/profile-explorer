// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.Analysis {
    public enum CFGEdgeKind {
        Tree,
        Forward,
        Backward,
        Crossing
    }

    public struct CFGEdge {
        private BlockIR FromBlock;
        private BlockIR ToBlock;
        private CFGEdgeKind Kind;
    }

    public class CFGBlockOrdering {
        private FunctionIR function_;
        private List<BlockIR> postorderList_;
        private Dictionary<BlockIR, int> postorderMap_;
        private List<BlockIR> preorderList_;
        private Dictionary<BlockIR, int> preorderMap_;
        private HashSet<BlockIR> visitedBlocks_;

        public CFGBlockOrdering(FunctionIR function) {
            function_ = function;
            preorderList_ = new List<BlockIR>(function_.Blocks.Count);
            postorderList_ = new List<BlockIR>(function_.Blocks.Count);
            preorderMap_ = new Dictionary<BlockIR, int>(function_.Blocks.Count);
            postorderMap_ = new Dictionary<BlockIR, int>(function_.Blocks.Count);
            visitedBlocks_ = new HashSet<BlockIR>(function_.Blocks.Count);

            if (function.EntryBlock != null) {
                visitedBlocks_.Add(function_.EntryBlock);
                ComputeInfo(function.EntryBlock);
            }
        }

        public List<BlockIR> PreorderList => preorderList_;
        public List<BlockIR> PostorderList => postorderList_;

        public int GetBlockPreorderNumber(BlockIR block) {
            if (preorderMap_.TryGetValue(block, out int number)) {
                return number;
            }

            return -1;
        }

        public int GetBlockPostorderNumber(BlockIR block) {
            if (postorderMap_.TryGetValue(block, out int number)) {
                return number;
            }

            return -1;
        }

        public void PostorderWalk(Func<BlockIR, int, bool> action) {
            for (int i = 0; i < postorderList_.Count; i++) {
                if (!action(postorderList_[i], i)) {
                    break;
                }
            }
        }

        public void PreorderWalk(Func<BlockIR, int, bool> action) {
            for (int i = 0; i < preorderList_.Count; i++) {
                if (!action(preorderList_[i], i)) {
                    break;
                }
            }
        }

        public void ReversePostorderWalk(Func<BlockIR, int, bool> action) {
            for (int i = postorderList_.Count - 1; i >= 0; i--) {
                if (!action(postorderList_[i], i)) {
                    break;
                }
            }
        }

        private void ComputeInfo(BlockIR block) {
            preorderMap_.Add(block, preorderList_.Count);
            preorderList_.Add(block);

            foreach (var successorBlock in block.Successors) {
                if (visitedBlocks_.Add(successorBlock)) {
                    // First time visiting the block.
                    //? TODO: Compute edge kind
                    ComputeInfo(successorBlock);
                }
                else {
                    //? TODO: Compute edge kind
                }
            }

            postorderMap_.Add(block, postorderList_.Count);
            postorderList_.Add(block);
        }
    }
}
