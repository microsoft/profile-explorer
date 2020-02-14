// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using Core.IR;
using Core.Analysis;
using System.Diagnostics;

namespace Core.GraphViz {
    class DominatorTreePrinter : GraphVizPrinter {
        private FunctionIR function_;
        private DominatorAlgorithmOptions options_;
        public Dictionary<string, BlockIR> BlockNameMap { get; set; }

        public DominatorTreePrinter(FunctionIR function, DominatorAlgorithmOptions options) {
            function_ = function;
            options_ = options;
            BlockNameMap = new Dictionary<string, BlockIR>();
        }

        void CreateNode(BlockIR block, StringBuilder builder) {
            var blockName = base.CreateNode(block.Id, block.Number.ToString(), builder);
            BlockNameMap[blockName] = block;
        }

        void CreateEdge(BlockIR block1, BlockIR block2, StringBuilder builder) {
            base.CreateEdge(block1.Id, block2.Id, builder);
        }

        protected override void PrintGraph(StringBuilder builder) {
            var cache = FunctionAnalysisCache.Get(function_);
            var dominatorAlgo = options_.HasFlag(DominatorAlgorithmOptions.Dominators) ?
                                cache.GetDominators() :
                                cache.GetPostDominators();

            if (dominatorAlgo.DomTreeRootNode == null) {
                Trace.TraceWarning($"Invalid DomTree {ObjectTracker.Track(dominatorAlgo)}");
                return; // Invalid CFG.
            }
            
            PrintDomTree(dominatorAlgo.DomTreeRootNode, builder);
        }

        void PrintDomTree(Core.Analysis.DominatorTreeNode node, StringBuilder builder) {
            CreateNode(node.Block, builder);

            foreach (var child in node.Children) {
                PrintDomTree(child, builder);
                CreateEdge(node.Block, child.Block, builder);
            }
        }

        public override Dictionary<string, BlockIR> CreateBlockNodeMap() {
            Dictionary<string, BlockIR> map = new Dictionary<string, BlockIR>();

            foreach (var block in function_.Blocks) {
                map[GetNodeName(block.Id)] = block;
            }

            return map;
        }
    }
}
