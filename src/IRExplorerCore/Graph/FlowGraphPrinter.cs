// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph;

public sealed class FlowGraphPrinter : GraphVizPrinter {
  private const int LargeGraphThresholdMin = 500;
  private const int LargeGraphThresholdMax = 1000;
  private const string LargeGraphSettings = @"
maxiter=8;
        ";
  private const string HugeGraphSettings = @"
maxiter=4;
mclimit=2;
nslimit=2;
        ";
  private FunctionIR function_;
  private Dictionary<string, TaggedObject> blockNameMap_;
  private DominatorAlgorithm dominatorAlgo_;

  public FlowGraphPrinter(FunctionIR function) {
    function_ = function;
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

  protected override string GetExtraSettings() {
    int count = function_.Blocks.Count;

    if (count > LargeGraphThresholdMin) {
      return count < LargeGraphThresholdMax ? LargeGraphSettings : HugeGraphSettings;
    }

    return "";
  }

  protected override void PrintGraph(StringBuilder builder) {
    foreach (var block in function_.Blocks) {
      CreateNode(block, builder);
    }

    // Compute the dominator tree, used to mark loop back-edges and immediate dominators.
    var cache = FunctionAnalysisCache.Get(function_);
    dominatorAlgo_ = cache.GetDominators();

    if (!dominatorAlgo_.IsValid) {
      dominatorAlgo_ = null;
    }

    foreach (var block in function_.Blocks) {
      foreach (var successorBlock in block.Successors) {
        CreateEdge(block, successorBlock, builder);
      }
    }

    string domEdges = PrintDominatorEdges(DominatorAlgorithmOptions.Dominators);
    builder.AppendLine(domEdges);
  }

  private void CreateNode(BlockIR block, StringBuilder builder) {
    string blockName = CreateNode(block.Id, block.Number.ToString(), builder, "B");
    blockNameMap_[blockName] = block;
  }

  private void CreateEdge(BlockIR block1, BlockIR block2, StringBuilder builder) {
    // A loop back-edge is an edge from a block to a block with a lower number,
    // with the lower number block dominating the other block.
    if (block2.Number <= block1.Number) {
      bool accept = true;

      if (dominatorAlgo_ != null) {
        // Use dominator tree for the complete check, otherwise
        // mark edge only using the block numbers, which is not always correct.
        accept = dominatorAlgo_.Dominates(block1, block2);
      }

      if (accept) {
        CreateEdgeWithStyle(block1.Id, block2.Id, "dashed", builder);
        return;
      }
    }

    CreateEdge(block1.Id, block2.Id, builder);
  }

  private void CreateEdgeWithStyle(BlockIR block1, BlockIR block2,
                                   StringBuilder builder) {
    CreateEdgeWithStyle(block1.Id, block2.Id, "dotted", builder);
  }

  private string PrintDominatorEdges(DominatorAlgorithmOptions options) {
    if (dominatorAlgo_ == null) {
      return ""; // Invalid CFG.
    }

    var builder = new StringBuilder();

    foreach (var block in function_.Blocks) {
      // Ignore blocks with single predecessor, immediate dom. is obvious.
      if (block.Predecessors.Count <= 1) {
        continue;
      }

      var immDomBlock = dominatorAlgo_.GetImmediateDominator(block);

      if (immDomBlock != null) {
        CreateEdgeWithStyle(block, immDomBlock, builder);
      }
    }

    return builder.ToString();
  }
}
