// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.Core.Graph;

public sealed class DominatorTreePrinter : GraphVizPrinter {
  private FunctionIR function_;
  private DominatorAlgorithmOptions options_;
  private Dictionary<string, TaggedObject> blockNameMap_;

  public DominatorTreePrinter(FunctionIR function, DominatorAlgorithmOptions options) {
    function_ = function;
    options_ = options;
    blockNameMap_ = new Dictionary<string, TaggedObject>();
  }

  public override Dictionary<string, TaggedObject> CreateNodeDataMap() {
    if (blockNameMap_.Count > 0) {
      return blockNameMap_;
    }

    var map = new Dictionary<string, TaggedObject>();

    foreach (var block in function_.Blocks) {
      map[GetNodeName(block.Id)] = block;
    }

    return map;
  }

  public override Dictionary<TaggedObject, List<TaggedObject>> CreateNodeDataGroupsMap() {
    return null;
  }

  protected override void PrintGraph(StringBuilder builder) {
    var cache = FunctionAnalysisCache.Get(function_);

    var dominatorAlgo = options_.HasFlag(DominatorAlgorithmOptions.Dominators)
      ? cache.GetDominators()
      : cache.GetPostDominators();

    if (dominatorAlgo.DomTreeRootNode == null) {
      Trace.TraceWarning($"Invalid DomTree {ObjectTracker.Track(dominatorAlgo)}");
      return; // Invalid CFG.
    }

    PrintDomTree(dominatorAlgo.DomTreeRootNode, builder);
  }

  private void CreateNode(BlockIR block, StringBuilder builder) {
    string blockName = CreateNode(block.Id, block.Number.ToString(), builder, "B");
    blockNameMap_[blockName] = block;
  }

  private void CreateEdge(BlockIR block1, BlockIR block2, StringBuilder builder) {
    CreateEdge(block1.Id, block2.Id, builder);
  }

  private void PrintDomTree(DominatorTreeNode node, StringBuilder builder) {
    CreateNode(node.Block, builder);

    foreach (var child in node.Children) {
      PrintDomTree(child, builder);
      CreateEdge(node.Block, child.Block, builder);
    }
  }
}