// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerCore.Graph {
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

        public FlowGraphPrinter(FunctionIR function, GraphPrinterNameProvider nameProvider) :
            base(nameProvider) {
            function_ = function;
            nameProvider_ = nameProvider;
            blockNameMap_ = new Dictionary<string, TaggedObject>();
        }

        protected override string GetExtraSettings() {
            int count = function_.Blocks.Count;

            if (count > LargeGraphThresholdMin) {
                return count < LargeGraphThresholdMax ? LargeGraphSettings : HugeGraphSettings;
            }

            return "";
        }

        private void CreateNode(BlockIR block, StringBuilder builder) {
            string blockName = CreateNode(block.Id, nameProvider_.GetBlockNodeLabel(block), builder);
            blockNameMap_[blockName] = block;
        }

        private void CreateEdge(BlockIR block1, BlockIR block2, StringBuilder builder) {
            CreateEdge(block1.Id, block2.Id, builder);
        }

        private void CreateEdgeWithStyle(BlockIR block1, BlockIR block2,
                                         StringBuilder builder) {
            CreateEdgeWithStyle(block1.Id, block2.Id, "dotted", builder);
        }

        protected override void PrintGraph(StringBuilder builder) {
            foreach (var block in function_.Blocks) {
                CreateNode(block, builder);
            }

            foreach (var block in function_.Blocks) {
                foreach (var successorBlock in block.Successors) {
                    CreateEdge(block, successorBlock, builder);
                }
            }

            //string domEdges = PrintDominatorEdges(DominatorAlgorithmOptions.Dominators);
            //builder.AppendLine(domEdges);
        }

        private string PrintDominatorEdges(DominatorAlgorithmOptions options) {
            var cache = FunctionAnalysisCache.Get(function_);
            var dominatorAlgo = cache.GetDominators();

            if (dominatorAlgo.DomTreeRootNode == null) {
                Trace.TraceWarning($"Invalid DomTree {ObjectTracker.Track(dominatorAlgo)}");
                return ""; // Invalid CFG.
            }

            var builder = new StringBuilder();

            foreach (var block in function_.Blocks) {
                // Ignore blocks with single predecessor, immediate dom. is obvious.
                if (block.Predecessors.Count <= 1) {
                    continue;
                }

                var immDomBlock = dominatorAlgo.GetImmediateDominator(block);

                if (immDomBlock != null) {
                    CreateEdgeWithStyle(block, immDomBlock, builder);
                }
            }

            return builder.ToString();
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