// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows.Media;
using IRExplorerCore.Graph;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public sealed class FlowGraphStyleProvider : IGraphStyleProvider {
        private const int PolylineEdgeThreshold = 100;
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

        private Graph graph_;
        private FlowGraphSettings options_;
        private HighlightingStyle returnBlockStyle_;
        private Pen returnEdgeStyle_;
        private HighlightingStyle switchBlockStyle_;

        public FlowGraphStyleProvider(Graph graph, FlowGraphSettings options) {
            graph_ = graph;
            options_ = options;
            defaultTextColor_ = ColorBrushes.GetBrush(options.TextColor);
            defaultNodeBackground_ = ColorBrushes.GetBrush(options.NodeColor);

            defaultNodeStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                      ColorPens.GetPen(options.NodeBorderColor,
                                                                  DefaultEdgeThickness));

            branchBlockStyle_ =
                new HighlightingStyle(defaultNodeBackground_,
                                      ColorPens.GetPen(options.BranchNodeBorderColor, 0.035));

            switchBlockStyle_ =
                new HighlightingStyle(defaultNodeBackground_,
                                      ColorPens.GetPen(options.SwitchNodeBorderColor, 0.035));

            loopBackedgeBlockStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                            ColorPens.GetPen(
                                                                options.LoopNodeBorderColor,
                                                                BoldEdgeThickness));

            returnBlockStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                      ColorPens.GetPen(options.ReturnNodeBorderColor,
                                                                  BoldEdgeThickness));

            emptyBlockStyle_ = new HighlightingStyle(Colors.Gainsboro,
                                                     ColorPens.GetPen(options.NodeBorderColor,
                                                                 DefaultEdgeThickness));

            edgeStyle_ = ColorPens.GetPen(options.EdgeColor, DefaultEdgeThickness);
            branchEdgeStyle_ = ColorPens.GetPen(options.BranchNodeBorderColor, BoldEdgeThickness);
            loopEdgeStyle_ = ColorPens.GetPen(options.LoopNodeBorderColor, BoldEdgeThickness);

            immDomEdgeStyle_ =
                ColorPens.GetDashedPen(options.DominatorEdgeColor, DashStyles.Dot, DashedEdgeThickness);

            returnEdgeStyle_ = ColorPens.GetPen(options.ReturnNodeBorderColor, DefaultEdgeThickness);

            if (options.MarkLoopBlocks) {
                loopBlockStyles_ = new List<HighlightingStyle>();

                foreach (var color in options.LoopNodeColors) {
                    loopBlockStyles_.Add(
                        new HighlightingStyle(
                            color, ColorPens.GetPen(options.NodeBorderColor, DefaultEdgeThickness)));
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
            var element = node.ElementData;

            return element switch
            {
                null => defaultNodeStyle_,
                BlockIR block => GetBlockNodeStyle(block),
                _ => defaultNodeStyle_
            };
        }

        public HighlightingStyle GetEdgeLabelStyle(Edge edge) {
            return defaultNodeStyle_;
        }

        public GraphEdgeKind GetEdgeKind(Edge edge) {
            if (!options_.ColorizeEdges) {
                return GraphEdgeKind.Default;
            }

            if (edge.Style == Edge.EdgeKind.Dotted) {
                return GraphEdgeKind.ImmediateDominator;
            }

            if (graph_.Kind == GraphKind.FlowGraph) {
                var fromBlock = edge.NodeFrom?.ElementData as BlockIR;
                var toBlock = edge.NodeTo?.ElementData as BlockIR;

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

        public bool ShouldUsePolylines() {
            return graph_.Nodes.Find(node => node.InEdges != null &&
                                     node.InEdges.Count > PolylineEdgeThreshold) != null;
        }
    }
}