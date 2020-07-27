// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore.Analysis {
    public class DominatorTreeNode {
        public DominatorTreeNode(BlockIR block, int childCount = 0) {
            Block = block;
            Children = new List<DominatorTreeNode>(childCount);
        }

        public BlockIR Block { get; set; }
        public DominatorTreeNode ImmediateDominator { get; set; }
        public List<DominatorTreeNode> Children { get; set; }
    }

    [Flags]
    public enum DominatorAlgorithmOptions {
        Dominators = 1 << 0,
        PostDominators = 1 << 1,
        BuildDominatorTree = 1 << 2,
        BuildQueryCache = 1 << 3
    }

    public class DominatorAlgorithm {
        private Dictionary<BlockIR, DominatorTreeNode> blockDomTreeNodeMap_;
        private Dictionary<BlockIR, int> blockIdMap_;
        private CFGBlockOrdering blockOrdering_;
        private HashSet<Tuple<BlockIR, BlockIR>> dominanceCache_;
        private FunctionIR function_;
        private List<int> immDoms_;
        private DominatorAlgorithmOptions options_;
        private List<BlockIR> postorderList_;
        private Dictionary<int, BlockIR> postorderNumberBlockMap_;
        private BlockIR treeStartBlock_;
        private DominatorTreeNode treeRootNode_;

        public DominatorAlgorithm(FunctionIR function, DominatorAlgorithmOptions options) {
            function_ = function;
            options_ = options;
            bool usePostDominators = options.HasFlag(DominatorAlgorithmOptions.PostDominators);
            treeStartBlock_ = usePostDominators ? function.ExitBlock : function.EntryBlock;
            int blockCount = function.Blocks.Count;

            // Build a list of the blocks  in postorder.
            blockOrdering_ = new CFGBlockOrdering(function);
            postorderList_ = blockOrdering_.PostorderList;

            // For post-dominators, the blicks are walked in reverse-postorder.
            if (usePostDominators) {
                postorderList_.Reverse();
            }

            // Build map of block to its ID in the immDom array.
            blockIdMap_ = new Dictionary<BlockIR, int>(blockCount);

            for (int i = 0; i < postorderList_.Count; i++) {
                blockIdMap_[postorderList_[i]] = i;
            }

            // Build a reverse map from block -> postorder number
            // to allow querying the immediate dominator list quickly.
            postorderNumberBlockMap_ = new Dictionary<int, BlockIR>(blockCount);

            foreach (var block in postorderList_) {
                postorderNumberBlockMap_[GetBlockId(block)] = block;
            }

            if (!InitializeImmediateDoms(treeStartBlock_)) {
                return; // CFG is invalid.
            }

            if (usePostDominators) {
                ComputePostDominators();
            }
            else {
                ComputeDominators();
            }

            if (options.HasFlag(DominatorAlgorithmOptions.BuildDominatorTree)) {
                BuildTree(treeStartBlock_);
            }
        }

        public DominatorTreeNode DomTreeRootNode => treeRootNode_;

        public BlockIR GetImmediateDominator(BlockIR block) {
            int blockId = GetBlockId(block);

            if (blockId == -1) {
                return null; // CFG is invalid;
            }

            int immDom = immDoms_[blockId];
            return immDom != -1 ? postorderNumberBlockMap_[immDom] : null;
        }

        public bool Dominates(BlockIR block, BlockIR dominatedBlock) {
            if (block == dominatedBlock) {
                return true;
            }

            if (BuildQueryCache(treeStartBlock_)) {
                var pair = new Tuple<BlockIR, BlockIR>(block, dominatedBlock);
                return dominanceCache_.Contains(pair);
            }

            // Fall back to a search through the immdom array.
            int blockId = GetBlockId(dominatedBlock);

            if (blockId == -1) {
                return false; // Unreachable block.
            }

            int immDom = immDoms_[blockId];

            while (immDom != -1) {
                var immDomBlock = postorderNumberBlockMap_[immDom];

                if (immDomBlock == block) {
                    return true;
                }
                else if (immDomBlock == treeStartBlock_) {
                    return false;
                }

                immDom = immDoms_[immDom];
            }

            return false;
        }

        private void ComputeDominators() {
            bool changed = true;

            while (changed) {
                changed = false;

                // Iterate over the block list. Note that we don't start with the last node
                // because we want to skip over the start (entry) block.
                for (int i = postorderList_.Count - 2; i >= 0; i--) {
                    // We need to choose the first predecessor that was processed.
                    // Then we intersect its dominator set with the sets of all
                    // the other predecessors that have been processed.
                    int newIdomId = -1;
                    var block = postorderList_[i];

                    foreach (var predBlock in block.Predecessors) {
                        UpdateImmediateDominator(predBlock, ref newIdomId);
                    }

                    // If the new immediate dominator is not the same as the last one
                    // save it and mark that a change has been made.
                    if (immDoms_[i] != newIdomId) {
                        immDoms_[i] = newIdomId;
                        changed = true;
                    }
                }
            }
        }

        private void ComputePostDominators() {
            // Similar to the dominators algorithm, but with blocks
            // being iterated bottom-up and intersecting successor blocks instead.
            bool changed = true;

            while (changed) {
                changed = false;

                for (int i = postorderList_.Count - 2; i >= 0; i--) {
                    int newIdomId = -1;
                    var block = postorderList_[i];

                    foreach (var successorBlock in block.Successors) {
                        UpdateImmediateDominator(successorBlock, ref newIdomId);
                    }

                    if (immDoms_[i] != newIdomId) {
                        immDoms_[i] = newIdomId;
                        changed = true;
                    }
                }
            }
        }

        private void UpdateImmediateDominator(BlockIR block, ref int newIdomId) {
            int blockId = GetBlockId(block);

            if (blockId == -1) {
                // This happens when the predecessor is unreachable,
                // but the current block is reachable.
                return;
            }

            if (immDoms_[blockId] == -1) {
                // Skip the predecessor if it wasn't processed yet.
            }
            else if (newIdomId == -1) {
                // This is the first predecessor that was processed.
                newIdomId = blockId;
            }
            else {
                // This is a predecessor that was processed. Intersect its
                // dominator set with the current new immediate dominator.
                newIdomId = Intersect(blockId, newIdomId);
            }
        }

        private bool InitializeImmediateDoms(BlockIR startBlock) {
            immDoms_ = new List<int>(function_.Blocks.Count);

            for (int i = 0; i < postorderList_.Count; i++) {
                immDoms_.Add(-1);
            }

            // The start node is dominated only by itself.
            int startBlockId = GetBlockId(startBlock);

            if (startBlockId == -1) {
                return false;
            }

            immDoms_[startBlockId] = startBlockId;
            return true;
        }

        private int Intersect(int a, int b) {
            // Walk up the immediate dominator array until the "fingers" point to the
            // same postorder number. Note that a higher postorder number means that
            // we're closer to the entry block of the CFG (exit block if we're
            // talking about a post-dominator tree).
            while (a != b) {
                while (a < b) {
                    a = immDoms_[a]; // PostNumb(immDoms[a]) > PostNumb(a)
                }

                while (b < a) {
                    b = immDoms_[b]; // Same as above.
                }
            }

            return a;
        }

        private int GetBlockId(BlockIR block) {
            if (block == null) {
                return -1; // Invalid CFG.
            }

            if (blockIdMap_.TryGetValue(block, out int id)) {
                return id;
            }

            return -1;
        }

        private void BuildTree(BlockIR startBlock) {
            blockDomTreeNodeMap_ =
                new Dictionary<BlockIR, DominatorTreeNode>(function_.Blocks.Count);

            treeRootNode_ = GetOrCreateDomTreeNode(startBlock);

            // Build the tree top-down.
            for (int i = postorderList_.Count - 2; i >= 0; i--) {
                var block = postorderList_[i];
                var blockNode = GetOrCreateDomTreeNode(block);
                int blockId = GetBlockId(block);
                int immDom = immDoms_[blockId];

                if (immDom == -1) {
                    continue; // This is an unreachable block.
                }

                var immDomBlock = postorderNumberBlockMap_[immDom];
                var immDomBlockNode = GetOrCreateDomTreeNode(immDomBlock);
                blockNode.ImmediateDominator = immDomBlockNode;
                immDomBlockNode.Children.Add(blockNode);
            }
        }

        private DominatorTreeNode GetOrCreateDomTreeNode(BlockIR block) {
            if (blockDomTreeNodeMap_.TryGetValue(block, out var node)) {
                return node;
            }

            node = new DominatorTreeNode(block, block.Successors.Count);
            blockDomTreeNodeMap_[block] = node;
            return node;
        }

        private bool BuildQueryCache(BlockIR startBlock) {
            if (dominanceCache_ != null) {
                return true;
            }

            if (!options_.HasFlag(DominatorAlgorithmOptions.BuildQueryCache)) {
                return false;
            }

            dominanceCache_ = new HashSet<Tuple<BlockIR, BlockIR>>(function_.Blocks.Count * 4);

            foreach (var block in postorderList_) {
                int blockId = GetBlockId(block);
                int immDom = immDoms_[blockId];

                while (immDom != -1) {
                    var immDomBlock = postorderNumberBlockMap_[immDom];
                    dominanceCache_.Add(new Tuple<BlockIR, BlockIR>(immDomBlock, block));

                    if (immDomBlock == startBlock) {
                        break;
                    }

                    immDom = immDoms_[immDom];
                }
            }

            return true;
        }
    }
}
