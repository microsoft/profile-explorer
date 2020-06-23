// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows.Media;
using CoreLib.Graph;
using CoreLib.GraphViz;
using CoreLib.IR;

namespace Client {
    public class FlowGraphStyleProvider : IGraphStyleProvider {
        private const double DefaultEdgeThickness = 0.025;
        private const double BoldEdgeThickness = 0.05;
        private const double DashedEdgeThickness = 0.035;
        private HighlightingStyle branchBlockStyle_;
        private Pen branchEdgeStyle_;
        private Brush defaultNodeBackground_;
        private HighlightingStyle defaultNodeStyle_;
        private Brush defaultTextColor_;
        private Pen edgeStyle_;
        private HighlightingStyle emptyBlockStyle_;
        private Pen immDomEdgeStyle_;
        private HighlightingStyle loopBackedgeBlockStyle_;
        private List<HighlightingStyle> loopBlockStyles_;
        private Pen loopEdgeStyle_;

        private GraphKind graphKind_;
        private FlowGraphSettings options_;
        private HighlightingStyle returnBlockStyle_;
        private Pen returnEdgeStyle_;
        private HighlightingStyle switchBlockStyle_;

        public FlowGraphStyleProvider(GraphKind kind, FlowGraphSettings options) {
            graphKind_ = kind;
            options_ = options;
            defaultTextColor_ = ColorBrushes.GetBrush(options.TextColor);
            defaultNodeBackground_ = ColorBrushes.GetBrush(options.NodeColor);

            defaultNodeStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                      Pens.GetPen(options.NodeBorderColor,
                                                                  DefaultEdgeThickness));

            branchBlockStyle_ =
                new HighlightingStyle(defaultNodeBackground_,
                                      Pens.GetPen(options.BranchNodeBorderColor, 0.035));

            switchBlockStyle_ =
                new HighlightingStyle(defaultNodeBackground_,
                                      Pens.GetPen(options.SwitchNodeBorderColor, 0.035));

            loopBackedgeBlockStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                            Pens.GetPen(
                                                                options.LoopNodeBorderColor,
                                                                BoldEdgeThickness));

            returnBlockStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                      Pens.GetPen(options.ReturnNodeBorderColor,
                                                                  BoldEdgeThickness));

            emptyBlockStyle_ = new HighlightingStyle(Colors.Gainsboro,
                                                     Pens.GetPen(options.NodeBorderColor,
                                                                 DefaultEdgeThickness));

            edgeStyle_ = Pens.GetPen(options.EdgeColor, DefaultEdgeThickness);
            branchEdgeStyle_ = Pens.GetPen(options.BranchNodeBorderColor, BoldEdgeThickness);
            loopEdgeStyle_ = Pens.GetPen(options.LoopNodeBorderColor, BoldEdgeThickness);

            immDomEdgeStyle_ =
                Pens.GetDashedPen(options.DominatorEdgeColor, DashStyles.Dot, DashedEdgeThickness);

            returnEdgeStyle_ = Pens.GetPen(options.ReturnNodeBorderColor, DefaultEdgeThickness);

            if (options.MarkLoopBlocks) {
                loopBlockStyles_ = new List<HighlightingStyle>();

                foreach (var color in options.LoopNodeColors) {
                    loopBlockStyles_.Add(
                        new HighlightingStyle(
                            color, Pens.GetPen(options.NodeBorderColor, DefaultEdgeThickness)));
                }
            }
        }

        public HighlightingStyle GetDefaultNodeStyle() {
            return defaultNodeStyle_;
        }

        public Brush GetDefaultNodeBackground() {
            return defaultNodeBackground_;
        }

        public Brush GetDefaultTextColor() {
            return defaultTextColor_;
        }

        public HighlightingStyle GetNodeStyle(Node node) {
            var element = node.Element;

            return element switch {
                null          => defaultNodeStyle_,
                BlockIR block => GetBlockNodeStyle(block),
                _             => defaultNodeStyle_
            };
        }

        public GraphEdgeKind GetEdgeKind(Edge edge) {
            if (!options_.ColorizeEdges) {
                return GraphEdgeKind.Default;
            }

            if (edge.Style == Edge.EdgeKind.Dotted) {
                return GraphEdgeKind.ImmediateDominator;
            }

            if(graphKind_ == GraphKind.FlowGraph) { 
                var fromBlock = edge.NodeFrom?.Element as BlockIR;
                var toBlock = edge.NodeTo?.Element as BlockIR;

                if (fromBlock != null && toBlock != null) {
                    if (toBlock.Number <= fromBlock.Number) {
                        return GraphEdgeKind.Loop;
                    }
                    else if (toBlock.IsReturnBlock) {
                        return GraphEdgeKind.Return;
                    }
                    else if (fromBlock.Successors.Count == 2) {
                        var targetBlock = fromBlock.BranchTargetBlock;

                        if (targetBlock == toBlock) {
                            return GraphEdgeKind.Branch;
                        }
                    }
                }
            }

            return GraphEdgeKind.Default;
        }

        public Pen GetEdgeStyle(GraphEdgeKind kind) {
            switch (kind) {
                case GraphEdgeKind.Loop: {
                    return loopEdgeStyle_;
                }
                case GraphEdgeKind.Branch: {
                    return branchEdgeStyle_;
                }
                case GraphEdgeKind.Return: {
                    return returnEdgeStyle_;
                }
                case GraphEdgeKind.ImmediateDominator:
                case GraphEdgeKind.ImmediatePostDominator: {
                    return immDomEdgeStyle_;
                }
            }

            return edgeStyle_;
        }

        public bool ShouldRenderEdges(GraphEdgeKind kind) {
            if (kind == GraphEdgeKind.ImmediateDominator || kind == GraphEdgeKind.ImmediatePostDominator) {
                return options_.ShowImmDominatorEdges;
            }

            return true;
        }

        public HighlightingStyle GetBlockNodeStyle(BlockIR block) {
            var loopTag = block.GetTag<LoopBlockTag>();

            if (loopTag != null && options_.MarkLoopBlocks) {
                if (loopTag.NestingLevel < loopBlockStyles_.Count - 1) {
                    return loopBlockStyles_[loopTag.NestingLevel];
                }
                else {
                    return loopBlockStyles_[^1];
                }
            }

            if (options_.ColorizeNodes) {
                if (block.HasLoopBackedge) {
                    return loopBackedgeBlockStyle_;
                }
                else if (block.IsBranchBlock) {
                    return branchBlockStyle_;
                }
                else if (block.IsSwitchBlock) {
                    return switchBlockStyle_;
                }
                else if (block.IsReturnBlock) {
                    return returnBlockStyle_;
                }
                else if (block.IsEmpty) {
                    return emptyBlockStyle_;
                }
            }

            return defaultNodeStyle_;
        }
    }
}
