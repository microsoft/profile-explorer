// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows.Media;
using Core.GraphViz;
using Core.IR;
using Core.UTC;

namespace Client
{
    public class ExpressionGraphStyleProvider : GraphStyleProvider
    {
        private const double DefaultEdgeThickness = 0.025;
        private const double BoldEdgeThickness = 0.05;

        ExpressionGraphSettings options_;
        Brush defaultTextColor_;
        Brush defaultNodeBackground_;
        HighlightingStyle defaultNodeStyle_;
        HighlightingStyle copyNodeStyle_;
        HighlightingStyle phiNodeStyle_;
        HighlightingStyle unaryNodeStyle_;
        HighlightingStyle binaryNodeStyle_;
        HighlightingStyle operandNodeStyle_;
        HighlightingStyle numberOperandNodeStyle_;
        HighlightingStyle addressOperandNodeStyle_;
        HighlightingStyle indirectOperandNodeStyle_;
        Pen edgeStyle_;
        Pen loopEdgeStyle_;

        public ExpressionGraphStyleProvider(ExpressionGraphSettings options)
        {
            options_ = options;
            defaultTextColor_ = ColorBrushes.GetBrush(options.TextColor);
            defaultNodeBackground_ = ColorBrushes.GetBrush(options.NodeColor);
            defaultNodeStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                     Pens.GetPen(options.NodeBorderColor, DefaultEdgeThickness)); ;
            copyNodeStyle_ = new HighlightingStyle(options.CopyInstructionNodeColor, Pens.GetPen(options.NodeBorderColor, 0.035));
            phiNodeStyle_ = new HighlightingStyle(options.PhiInstructionNodeColor, Pens.GetPen(options.NodeBorderColor, 0.035));
            unaryNodeStyle_ = new HighlightingStyle(options.UnaryInstructionNodeColor, Pens.GetPen(options.NodeBorderColor, 0.035));
            binaryNodeStyle_ = new HighlightingStyle(options.BinaryInstructionNodeColor, Pens.GetPen(options.NodeBorderColor, 0.035));
            operandNodeStyle_ = new HighlightingStyle(options.OperandNodeColor, Pens.GetPen(options.NodeBorderColor, 0.035));
            numberOperandNodeStyle_ = new HighlightingStyle(options.NumberOperandNodeColor, Pens.GetPen(options.NodeBorderColor, 0.035));
            addressOperandNodeStyle_ = new HighlightingStyle(options.AddressOperandNodeColor, Pens.GetPen(options.NodeBorderColor, 0.035));
            indirectOperandNodeStyle_ = new HighlightingStyle(options.IndirectionOperandNodeColor, Pens.GetPen(options.NodeBorderColor, 0.035));
            edgeStyle_ = Pens.GetPen(options.EdgeColor, DefaultEdgeThickness);
            loopEdgeStyle_ = Pens.GetPen(options.LoopPhiBackedgeColor, BoldEdgeThickness);
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

            if (element is InstructionIR instr)
            {
                if (instr.OpcodeIs(UTCOpcode.OPASSIGN))
                {
                    return copyNodeStyle_;
                }
                else if (instr.OpcodeIs(UTCOpcode.OPPHI))
                {
                    return phiNodeStyle_;
                }
                else if (instr.IsUnary)
                {
                    return unaryNodeStyle_;
                }
                else if (instr.IsBinary)
                {
                    return binaryNodeStyle_;
                }

                return defaultNodeStyle_;
            }
            else if (element is OperandIR op)
            {
                switch (op.Kind)
                {
                    case OperandKind.Variable:
                    case OperandKind.Temporary:
                        {
                            return operandNodeStyle_;
                        }
                    case OperandKind.IntConstant:
                    case OperandKind.FloatConstant:
                        {
                            return numberOperandNodeStyle_;
                        }
                    case OperandKind.Indirection:
                        {
                            return indirectOperandNodeStyle_;
                        }
                    case OperandKind.Address:
                    case OperandKind.LabelAddress:
                        {
                            return addressOperandNodeStyle_;
                        }
                }
            }

            return defaultNodeStyle_;
        }

        public GraphEdgeKind GetEdgeKind(Edge edge)
        {
            if (!options_.ColorizeEdges)
            {
                return GraphEdgeKind.Default;
            }

            // Mark edges of PHIs with values incoming from loops.
            var sourceInstr = edge.NodeTo.Element.ParentInstruction;

            if(sourceInstr != null && sourceInstr.OpcodeIs(UTCOpcode.OPPHI))
            {
                var sourceBlock = sourceInstr.ParentBlock;
                var destBlock = edge.NodeFrom.Element.ParentBlock;

                if(destBlock != null)
                {
                    if(destBlock.Number >= sourceBlock.Number)
                    {
                        return GraphEdgeKind.Loop;
                    }
                }
            }

            return GraphEdgeKind.Default;
        }

        public Pen GetEdgeStyle(GraphEdgeKind kind)
        {
            if(kind == GraphEdgeKind.Loop)
            {
                return loopEdgeStyle_;
            }

            return edgeStyle_;
        }

        public bool ShouldRenderEdges(GraphEdgeKind kind)
        {
            return true;
        }
    }
}
