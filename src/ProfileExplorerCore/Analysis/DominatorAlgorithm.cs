// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.Core.Analysis;

[Flags]
public enum DominatorAlgorithmOptions {
  Dominators = 1 << 0,
  PostDominators = 1 << 1,
  BuildTree = 1 << 2,
  BuildQueryCache = 1 << 3
}

public class DominatorTreeNode {
  public DominatorTreeNode(BlockIR block, int childCount = 0) {
    Block = block;
    Children = new List<DominatorTreeNode>(childCount);
  }

  public BlockIR Block { get; set; }
  public DominatorTreeNode ImmediateDominator { get; set; }
  public List<DominatorTreeNode> Children { get; set; }
}

public class DominatorAlgorithm {
  private readonly FunctionIR function_;
  private readonly DominatorAlgorithmOptions options_;
  private readonly Func<BlockIR, List<BlockIR>> nextBlocks_;
  private Dictionary<BlockIR, DominatorTreeNode> blockDomTreeNodeMap_;
  private Dictionary<BlockIR, int> blockIdMap_;
  private CFGBlockOrdering blockOrdering_;
  private HashSet<Tuple<BlockIR, BlockIR>> dominanceCache_;
  private List<int> immDoms_;
  private List<BlockIR> postorderList_;
  private Dictionary<int, BlockIR> postorderNumberBlockMap_;
  private BlockIR treeStartBlock_;
  private DominatorTreeNode treeRootNode_;

  public DominatorAlgorithm(FunctionIR function, DominatorAlgorithmOptions options) {
    function_ = function;
    options_ = options;
    bool usePostDominators = options.HasFlag(DominatorAlgorithmOptions.PostDominators);
    List<BlockIR> patchedBlocks = null;

    if (usePostDominators) {
      nextBlocks_ = block => block.Successors;

      // With multiple function exit blocks, create one fake exit block
      // that acts as the single exit and change each exit block
      // to have it as a successor.
      var exitBlocks = new List<BlockIR>();

      foreach (var block in function.Blocks) {
        if (block.IsReturnBlock) {
          exitBlocks.Add(block);
        }
      }

      if (exitBlocks.Count == 1) {
        treeStartBlock_ = exitBlocks[0];
      }
      else {
        patchedBlocks = new List<BlockIR>();
        var mergedExitBlock = new BlockIR(IRElementId.FromLong(0), function.BlockCount, function);
        mergedExitBlock.Number = function.BlockCount;
        treeStartBlock_ = mergedExitBlock;

        foreach (var exitBlock in exitBlocks) {
          mergedExitBlock.Predecessors.Add(exitBlock);
          exitBlock.Successors.Add(mergedExitBlock);
          patchedBlocks.Add(exitBlock); // To remove fake successor later.
        }
      }
    }
    else {
      // It is assumed there is a single entry block.
      nextBlocks_ = block => block.Predecessors;
      treeStartBlock_ = function.EntryBlock;
    }

    // Build a list of the blocks in postorder.
    blockOrdering_ = new CFGBlockOrdering(function);
    postorderList_ = blockOrdering_.PostorderList;

    // For post-dominators, the blocks are walked in reverse-postorder.
    if (usePostDominators) {
      postorderList_.Reverse();
    }

    // Build map of block to its ID in the immDom array.
    int blockCount = function.Blocks.Count;
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

    Compute();

    if (options.HasFlag(DominatorAlgorithmOptions.BuildTree)) {
      BuildTree(treeStartBlock_);
    }

    // Remove the fake successor merged exit block from the patched blocks.
    if (usePostDominators && patchedBlocks != null) {
      foreach (var block in patchedBlocks) {
        block.Predecessors.RemoveAt(block.Predecessors.Count - 1);
      }
    }
  }

  public DominatorTreeNode DomTreeRootNode => treeRootNode_;
  public bool IsValid => treeRootNode_ != null;

  public BlockIR GetImmediateDominator(BlockIR block) {
    int blockId = GetBlockId(block);

    if (blockId == -1) {
      return null; // CFG is invalid;
    }

    int immDom = immDoms_[blockId];
    return immDom != -1 ? postorderNumberBlockMap_[immDom] : null;
  }

  public IEnumerable<BlockIR> EnumerateDominators(BlockIR block) {
    while (block != treeStartBlock_) {
      block = GetImmediateDominator(block);

      if (block == null) {
        yield break;
      }

      yield return block;
    }
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

      if (immDomBlock == treeStartBlock_) {
        return false;
      }

      immDom = immDoms_[immDom];
    }

    return false;
  }

  public List<BlockIR> NextBlocks(BlockIR block) {
    return nextBlocks_(block);
  }

  private void Compute() {
    bool changed = true;

    while (changed) {
      changed = false;

      // Iterate over the block list. Note that we don't start with the last node
      // because we want to skip over the entry or exit block.
      for (int i = postorderList_.Count - 2; i >= 0; i--) {
        // We need to choose the first predecessor that was processed.
        // Then we intersect its dominator set with the sets of all
        // the other predecessors that have been processed.
        int newIdomId = -1;
        var block = postorderList_[i];

        foreach (var nextBlock in NextBlocks(block)) {
          UpdateImmediateDominator(nextBlock, ref newIdomId);
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