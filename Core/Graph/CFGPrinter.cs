// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Analysis;
using Core.IR;

namespace Core.GraphViz {
    public sealed class CFGPrinter : GraphVizPrinter {
        private static readonly string LargeGraphSettings = @"
splines=polyline;
maxiter=8;
        ";

        private static readonly string HugeGraphSettings = @"
splines=polyline;
maxiter=4;
mclimit=2;
nslimit=2;
        ";

        private static readonly int LargeGraphThresholdMin = 500;
        private static readonly int LargeGraphThresholdMax = 1000;

        protected override string GetExtraSettings() {
            var count = function_.Blocks.Count;

            if (count > LargeGraphThresholdMin) {
                if (count < LargeGraphThresholdMax) {
                    return LargeGraphSettings;
                }
                else return HugeGraphSettings;
            }

            return "";
        }

        private FunctionIR function_;
        public Dictionary<string, BlockIR> BlockNameMap { get; set; }

        public CFGPrinter(FunctionIR function) {
            function_ = function;
            BlockNameMap = new Dictionary<string, BlockIR>();
        }

        void CreateNode(BlockIR block, StringBuilder builder) {
            var blockName = base.CreateNode(block.Id, block.Number.ToString(), builder);
            BlockNameMap[blockName] = block;
        }

        void CreateEdge(BlockIR block1, BlockIR block2, StringBuilder builder) {
            base.CreateEdge(block1.Id, block2.Id, builder);
        }

        void CreateEdgeWithStyle(BlockIR block1, BlockIR block2, StringBuilder builder) {
            base.CreateEdgeWithStyle(block1.Id, block2.Id, "dotted", builder);
        }

        protected override async void PrintGraph(StringBuilder builder) {
            foreach (var block in function_.Blocks) {
                CreateNode(block, builder);
            }

            foreach (var block in function_.Blocks) {
                foreach (var successorBlock in block.Successors) {
                    CreateEdge(block, successorBlock, builder);
                }
            }

            var domEdges = PrintDominatorEdges(DominatorAlgorithmOptions.Dominators);
            builder.AppendLine(domEdges);
        }

        string PrintDominatorEdges(DominatorAlgorithmOptions options) {
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

        public override Dictionary<string, BlockIR> CreateBlockNodeMap() {
            Dictionary<string, BlockIR> map = new Dictionary<string, BlockIR>();

            foreach (var block in function_.Blocks) {
                map[GetNodeName(block.Id)] = block;
            }

            return map;
        }
    }
}
