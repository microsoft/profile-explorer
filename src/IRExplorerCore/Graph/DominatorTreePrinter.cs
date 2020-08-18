// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.GraphViz {
    class DominatorTreePrinter : GraphVizPrinter {
        private FunctionIR function_;
        private DominatorAlgorithmOptions options_;

        public DominatorTreePrinter(FunctionIR function, DominatorAlgorithmOptions options) {
            function_ = function;
            options_ = options;
            BlockNameMap = new Dictionary<string, object>();
        }

        private Dictionary<string, object> BlockNameMap { get; set; }

        private void CreateNode(BlockIR block, StringBuilder builder) {
            string blockName =
                base.CreateNode(block.Id, block.Number.ToString(), builder, "B");

            BlockNameMap[blockName] = block;
        }

        private void CreateEdge(BlockIR block1, BlockIR block2, StringBuilder builder) {
            base.CreateEdge(block1.Id, block2.Id, builder);
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

        private void PrintDomTree(DominatorTreeNode node, StringBuilder builder) {
            CreateNode(node.Block, builder);

            foreach (var child in node.Children) {
                PrintDomTree(child, builder);
                CreateEdge(node.Block, child.Block, builder);
            }
        }

        public override Dictionary<string, object> CreateBlockNodeMap() {
            if (BlockNameMap.Count > 0) {
                return BlockNameMap;
            }

            var map = new Dictionary<string, object>();

            foreach (var block in function_.Blocks) {
                map[GetNodeName(block.Id)] = block;
            }

            return map;
        }

        public override Dictionary<object, List<object>> CreateBlockNodeGroupsMap() {
            return null;
        }
    }
}
