// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph {
    class DominatorTreePrinter : GraphVizPrinter {
        private FunctionIR function_;
        private DominatorAlgorithmOptions options_;
        private Dictionary<string, TaggedObject> blockNameMap_;
        private HashSet<BlockIR> visited_;

        public DominatorTreePrinter(FunctionIR function, DominatorAlgorithmOptions options) {
            function_ = function;
            options_ = options;
            blockNameMap_ = new Dictionary<string, TaggedObject>();
            visited_ = new HashSet<BlockIR>();
        }

        private void CreateNode(BlockIR block, StringBuilder builder) {
            string blockName = CreateNode(block.Id, block.Number.ToString(), builder, "B");
            blockNameMap_[blockName] = block;
        }

        private void CreateEdge(BlockIR block1, BlockIR block2, StringBuilder builder) {
            CreateEdge(block1.Id, block2.Id, builder);
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
            visited_.Add(node.Block);

            foreach (var child in node.Children) {
                if (!visited_.Contains(child.Block)) {
                    PrintDomTree(child, builder);
                }
                else {
                    Trace.TraceWarning($"Loop in DomTree");
                    return;
                }

                CreateEdge(node.Block, child.Block, builder);
            }
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
    }
}
