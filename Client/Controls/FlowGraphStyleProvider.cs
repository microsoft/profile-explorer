// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows.Media;
using Core.GraphViz;
using Core.IR;

namespace Client
{
    public class FlowGraphStyleProvider : GraphStyleProvider
    {
        private const double DefaultEdgeThickness = 0.025;
        private const double BoldEdgeThickness = 0.05;
        private const double DashedEdgeThickness = 0.035;

        FlowGraphSettings options_;
        Brush defaultTextColor_;
        Brush defaultNodeBackground_;
        HighlightingStyle defaultNodeStyle_;
        HighlightingStyle emptyBlockStyle_;
        HighlightingStyle branchBlockStyle_;
        HighlightingStyle switchBlockStyle_;
        HighlightingStyle loopBackedgeBlockStyle_;
        HighlightingStyle returnBlockStyle_;
        List<HighlightingStyle> loopBlockStyles_;
        Pen edgeStyle_;
        Pen loopEdgeStyle_;
        Pen branchEdgeStyle_;
        Pen immDomEdgeStyle_;
        Pen returnEdgeStyle_;


        public FlowGraphStyleProvider(FlowGraphSettings options)
        {
            options_ = options;
            defaultTextColor_ = ColorBrushes.GetBrush(options.TextColor);
            defaultNodeBackground_ = ColorBrushes.GetBrush(options.NodeColor);
            defaultNodeStyle_ = new HighlightingStyle(defaultNodeBackground_, Pens.GetPen(options.NodeBorderColor, DefaultEdgeThickness)); ;
            branchBlockStyle_ = new HighlightingStyle(defaultNodeBackground_, Pens.GetPen(options.BranchNodeBorderColor, 0.035));
            switchBlockStyle_ = new HighlightingStyle(defaultNodeBackground_, Pens.GetPen(options.SwitchNodeBorderColor, 0.035));
            loopBackedgeBlockStyle_ = new HighlightingStyle(defaultNodeBackground_, Pens.GetPen(options.LoopNodeBorderColor, BoldEdgeThickness));
            returnBlockStyle_ = new HighlightingStyle(defaultNodeBackground_, Pens.GetPen(options.ReturnNodeBorderColor, BoldEdgeThickness));
            emptyBlockStyle_ = new HighlightingStyle(Colors.Gainsboro, Pens.GetPen(options.NodeBorderColor, DefaultEdgeThickness));
            edgeStyle_ = Pens.GetPen(options.EdgeColor, DefaultEdgeThickness);
            branchEdgeStyle_ = Pens.GetPen(options.BranchNodeBorderColor, BoldEdgeThickness);
            loopEdgeStyle_ = Pens.GetPen(options.LoopNodeBorderColor, BoldEdgeThickness);
            immDomEdgeStyle_ = Pens.GetDashedPen(options.DominatorEdgeColor, DashStyles.Dot, DashedEdgeThickness);
            returnEdgeStyle_ = Pens.GetPen(options.ReturnNodeBorderColor, DefaultEdgeThickness);

            if (options.MarkLoopBlocks)
            {
                loopBlockStyles_ = new List<HighlightingStyle>();

                foreach (var color in options.LoopNodeColors)
                {
                    loopBlockStyles_.Add(new HighlightingStyle(color, Pens.GetPen(options.NodeBorderColor, DefaultEdgeThickness)));
                }
            }
        }

        public HighlightingStyle GetDefaultNodeStyle()
        {
            return defaultNodeStyle_;
        }

        public Brush GetDefaultNodeBackground()
        {
            return defaultNodeBackground_;
        }

        public Brush GetDefaultTextColor()
        {
            return defaultTextColor_;
        }

        public HighlightingStyle GetNodeStyle(Node node)
        {
            var element = node.Element;

            if (element == null)
            {
                return defaultNodeStyle_;
            }

            if (element is BlockIR block)
            {
                return GetBlockNodeStyle(block);
            }

            return defaultNodeStyle_;
        }

        public HighlightingStyle GetBlockNodeStyle(BlockIR block)
        {
            var loopTag = block.GetTag<LoopBlockTag>();

            if (loopTag != null && options_.MarkLoopBlocks)
            {
                if (loopTag.NestingLevel < loopBlockStyles_.Count - 1)
                {
                    return loopBlockStyles_[loopTag.NestingLevel];
                }
                else return loopBlockStyles_[loopBlockStyles_.Count - 1];
            }

            if (options_.ColorizeNodes)
            {
                if (block.HasLoopBackedge) return loopBackedgeBlockStyle_;
                else if (block.IsBranchBlock) return branchBlockStyle_;
                else if (block.IsSwitchBlock) return switchBlockStyle_;
                else if (block.IsReturnBlock) return returnBlockStyle_;
                else if (block.IsEmpty) return emptyBlockStyle_;
            }

            return defaultNodeStyle_;
        }

        public GraphEdgeKind GetEdgeKind(Edge edge)
        {
            if (!options_.ColorizeEdges)
            {
                return GraphEdgeKind.Default;
            }

            if (edge.Style == Edge.EdgeKind.Dotted)
            {
                return GraphEdgeKind.ImmediateDominator;
            }

            var fromBlock = edge.NodeFrom?.Element as BlockIR;
            var toBlock = edge.NodeTo?.Element as BlockIR;

            if (fromBlock != null && toBlock != null)
            {
                if (toBlock.Number <= fromBlock.Number)
                {
                    return GraphEdgeKind.Loop;
                }
                else if (toBlock.IsReturnBlock)
                {
                    return GraphEdgeKind.Return;
                }
                else if (fromBlock.Successors.Count == 2)
                {
                    var targetBlock = fromBlock.BranchTargetBlock;

                    if (targetBlock == toBlock)
                    {
                        return GraphEdgeKind.Branch;
                    }
                }
            }

            return GraphEdgeKind.Default;
        }

        public Pen GetEdgeStyle(GraphEdgeKind kind)
        {
            switch(kind)
            {
                case GraphEdgeKind.Loop:
                    {
                        return loopEdgeStyle_;
                    }
                case GraphEdgeKind.Branch:
                    {
                        return branchEdgeStyle_;
                    }
                case GraphEdgeKind.Return:
                    {
                        return returnEdgeStyle_;
                    }
                case GraphEdgeKind.ImmediateDominator:
                case GraphEdgeKind.ImmediatePostDominator:
                    {
                        return immDomEdgeStyle_;
                    }
                
            }

            return edgeStyle_;
        }

        public bool ShouldRenderEdges(GraphEdgeKind kind)
        {
            if (kind == GraphEdgeKind.ImmediateDominator ||
                kind == GraphEdgeKind.ImmediatePostDominator)
            {
                return options_.ShowImmDominatorEdges;
            }

            return true;
        }
    }
}
