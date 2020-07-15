// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows.Media;
using IRExplorerCore.Graph;
using IRExplorerCore.GraphViz;
using IRExplorerCore.IR;
using IRExplorerCore.UTC;

namespace IRExplorer {
    public class ExpressionGraphStyleProvider : IGraphStyleProvider {
        private const double DefaultEdgeThickness = 0.025;
        private const double BoldEdgeThickness = 0.05;
        private HighlightingStyle addressOperandNodeStyle_;
        private HighlightingStyle binaryNodeStyle_;
        private HighlightingStyle copyNodeStyle_;
        private Brush defaultNodeBackground_;
        private HighlightingStyle defaultNodeStyle_;
        private Brush defaultTextColor_;
        private Pen edgeStyle_;
        private HighlightingStyle indirectOperandNodeStyle_;
        private Pen loopEdgeStyle_;
        private HighlightingStyle numberOperandNodeStyle_;
        private HighlightingStyle operandNodeStyle_;

        private ICompilerInfoProvider compilerInfo_;
        private GraphKind graphKind_;
        private ExpressionGraphSettings options_;
        private HighlightingStyle phiNodeStyle_;
        private HighlightingStyle loadNodeStyle_;
        private HighlightingStyle callNodeStyle_;
        private HighlightingStyle unaryNodeStyle_;

        public ExpressionGraphStyleProvider(GraphKind kind, ExpressionGraphSettings options,
                                            ICompilerInfoProvider compilerInfo) {
            compilerInfo_ = compilerInfo;
            graphKind_ = kind;
            options_ = options;
            defaultTextColor_ = ColorBrushes.GetBrush(options.TextColor);
            defaultNodeBackground_ = ColorBrushes.GetBrush(options.NodeColor);

            defaultNodeStyle_ = new HighlightingStyle(defaultNodeBackground_,
                                                      Pens.GetPen(options.NodeBorderColor,
                                                                  DefaultEdgeThickness));

            copyNodeStyle_ = new HighlightingStyle(options.CopyInstructionNodeColor,
                                                   Pens.GetPen(options.NodeBorderColor, 0.035));

            phiNodeStyle_ = new HighlightingStyle(options.PhiInstructionNodeColor,
                                                  Pens.GetPen(options.NodeBorderColor, 0.035));

            unaryNodeStyle_ = new HighlightingStyle(options.UnaryInstructionNodeColor,
                                                    Pens.GetPen(options.NodeBorderColor, 0.035));

            binaryNodeStyle_ = new HighlightingStyle(options.BinaryInstructionNodeColor,
                                                     Pens.GetPen(options.NodeBorderColor, 0.035));

            callNodeStyle_ = new HighlightingStyle(options.CallInstructionNodeColor,
                                                     Pens.GetPen(options.NodeBorderColor, 0.035));

            loadNodeStyle_ = new HighlightingStyle(options.LoadStoreInstructionNodeColor,
                                                   Pens.GetPen(options.NodeBorderColor, 0.035));

            binaryNodeStyle_ = new HighlightingStyle(options.BinaryInstructionNodeColor,
                                                     Pens.GetPen(options.NodeBorderColor, 0.035));

            operandNodeStyle_ =
                new HighlightingStyle(options.OperandNodeColor, Pens.GetPen(options.NodeBorderColor, 0.035));

            numberOperandNodeStyle_ =
                new HighlightingStyle(options.NumberOperandNodeColor,
                                      Pens.GetPen(options.NodeBorderColor, 0.035));

            addressOperandNodeStyle_ =
                new HighlightingStyle(options.AddressOperandNodeColor,
                                      Pens.GetPen(options.NodeBorderColor, 0.035));

            indirectOperandNodeStyle_ = new HighlightingStyle(options.IndirectionOperandNodeColor,
                                                              Pens.GetPen(options.NodeBorderColor, 0.035));

            edgeStyle_ = Pens.GetPen(options.EdgeColor, DefaultEdgeThickness);
            loopEdgeStyle_ = Pens.GetPen(options.LoopPhiBackedgeColor, BoldEdgeThickness);
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

            switch (element) {
                case null:
                    return defaultNodeStyle_;
                case OperandIR op: {
                    switch (op.Kind) {
                        case OperandKind.Variable:
                        case OperandKind.Temporary: {
                            return operandNodeStyle_;
                        }
                        case OperandKind.IntConstant:
                        case OperandKind.FloatConstant: {
                            return numberOperandNodeStyle_;
                        }
                        case OperandKind.Indirection: {
                            return indirectOperandNodeStyle_;
                        }
                        case OperandKind.Address:
                        case OperandKind.LabelAddress: {
                            return addressOperandNodeStyle_;
                        }
                    }

                    break;
                }
                case InstructionIR instr: {
                    if (compilerInfo_.IR.IsCopyInstruction(instr)) {
                        return copyNodeStyle_;
                    }
                    else if (compilerInfo_.IR.IsPhiInstruction(instr)) {
                        return phiNodeStyle_;
                    }
                    else if (compilerInfo_.IR.IsLoadInstruction(instr) ||
                             compilerInfo_.IR.IsStoreInstruction(instr)) {
                        return loadNodeStyle_;
                    }
                    else if (compilerInfo_.IR.IsCallInstruction(instr) ||
                             compilerInfo_.IR.IsIntrinsicCallInstruction(instr)) {
                        return callNodeStyle_;
                    }
                    else if (instr.IsUnary) {
                        return unaryNodeStyle_;
                    }
                    else if (instr.IsBinary) {
                        return binaryNodeStyle_;
                    }

                    break;
                }
            }

            return defaultNodeStyle_;
        }

        public GraphEdgeKind GetEdgeKind(Edge edge) {
            if (!options_.ColorizeEdges) {
                return GraphEdgeKind.Default;
            }

            // Mark edges of PHIs with values incoming from loops.
            var sourceInstr = edge.NodeTo.Element.ParentInstruction;

            if (sourceInstr != null && sourceInstr.OpcodeIs(UTCOpcode.OPPHI)) {
                var sourceBlock = sourceInstr.ParentBlock;
                var destBlock = edge.NodeFrom.Element.ParentBlock;

                if (destBlock != null) {
                    if (destBlock.Number >= sourceBlock.Number) {
                        return GraphEdgeKind.Loop;
                    }
                }
            }

            return GraphEdgeKind.Default;
        }

        public Pen GetEdgeStyle(GraphEdgeKind kind) {
            if (kind == GraphEdgeKind.Loop) {
                return loopEdgeStyle_;
            }

            return edgeStyle_;
        }

        public bool ShouldRenderEdges(GraphEdgeKind kind) {
            return true;
        }
    }
}
