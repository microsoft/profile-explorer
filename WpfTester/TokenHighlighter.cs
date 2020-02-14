// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Core.IR;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace WpfTester {
    class IRSegment : TextSegment {
        public IRElement Element { get; set; }
    }

    class IRHighlighter : IBackgroundRenderer {
        private List<IRElement> elements_;
        private TextEditor editor_;
        private TextSegmentCollection<TextSegment> segments_;

        public IRHighlighter(List<IRElement> elems, TextEditor editor) {
            elements_ = elems;
            editor_ = editor;
            segments_ = new TextSegmentCollection<TextSegment>();

            if (elems == null) return;

            foreach (var elem in elems) {
                AddElement(elem);
            }
        }

        public void AddElement(IRElement elem) {
            segments_.Add(new IRSegment {
                StartOffset = elem.TextLocation.Offset,
                Length = elem.TextLength,
                Element = elem
            });
        }

        public void ClearTokens() {
            segments_.Clear();
        }

        public KnownLayer Layer {
            get { return KnownLayer.Background; }
        }

        Brush GetTokenColor(IRElement elem) {
            if (elem is BlockIR block) {
                if (block.Id % 2 == 0) {
                    return Brushes.OldLace;
                }
                else return Brushes.WhiteSmoke;
            }
            else if (elem is TupleIR tuple) {
                switch (tuple.Kind) {
                    case TupleKind.Instruction: {
                        var instr = tuple as InstructionIR;

                        switch (instr.Kind) {
                            case InstructionKind.Unary: {
                                return Brushes.Lavender;
                            }
                            case InstructionKind.Binary: {
                                return Brushes.Thistle;
                            }
                            case InstructionKind.Call: {
                                return Brushes.Tomato;
                            }
                            case InstructionKind.Branch: {
                                return Brushes.LightSalmon;
                            }
                            case InstructionKind.Goto: {
                                return Brushes.Khaki; ;
                            }
                            case InstructionKind.Phi: {
                                return Brushes.PaleTurquoise;
                            }
                            case InstructionKind.Return: {
                                return Brushes.LightPink;
                            }
                        }
                        break;
                    }
                    case TupleKind.Label: {
                        return Brushes.SandyBrown;
                    }
                    case TupleKind.Metadata: {
                        return Brushes.Silver;
                    }
                }
            }
            else if (elem is OperandIR op) {
                switch (op.Kind) {
                    case OperandKind.Address:
                    case OperandKind.LabelAddress: {
                        return Brushes.IndianRed;
                    }
                    case OperandKind.Variable: {
                        return Brushes.Gold;
                    }
                    case OperandKind.Temporary: {
                        return Brushes.Wheat;
                    }
                    case OperandKind.IntConstant:
                    case OperandKind.FloatConstant: {
                        return Brushes.LightSkyBlue;
                    }
                    case OperandKind.Indirection: {
                        return Brushes.PaleGreen;
                    }
                }
            }

            return Brushes.Yellow;
        }

        public void Draw(TextView textView, DrawingContext drawingContext) {
            if (editor_.Document == null)
                return;

            if (editor_.Document.TextLength == 0)
                return;

            textView.EnsureVisualLines();

            var visualLines = textView.VisualLines;

            if (visualLines.Count == 0) {
                return;
            }

            int viewStart = visualLines[0].FirstDocumentLine.Offset;
            int viewEnd = visualLines[visualLines.Count - 1].LastDocumentLine.EndOffset;

            foreach (var result in segments_.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
                BackgroundGeometryBuilder geoBuilder = new BackgroundGeometryBuilder();
                geoBuilder.AlignToWholePixels = true;
                geoBuilder.BorderThickness = 0;
                geoBuilder.CornerRadius = 0;
                geoBuilder.AddSegment(textView, result);
                Geometry geometry = geoBuilder.CreateGeometry();
                if (geometry != null) {
                    var elem = (result as IRSegment).Element;
                    Pen p = null;

                    if (elem is OperandIR op) {
                        switch (op.Kind) {
                            case OperandKind.Variable: {
                                p = new Pen(Brushes.Gray, 1);
                                break;
                            }
                            case OperandKind.Indirection: {
                                p = new Pen(Brushes.Black, 1);
                                break;
                            }
                        }
                    }

                    drawingContext.DrawGeometry(GetTokenColor(elem), p, geometry);

                    if (elem is BlockIR block) {
                        var blockSeparator = new Pen(Brushes.Gray, 1);
                        blockSeparator.DashStyle = DashStyles.Dot;
                        var currentLine = textView.Document.GetLineByOffset(block.TextLocation.Offset);


                        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, currentLine)) {
                            drawingContext.DrawLine(blockSeparator, rect.TopLeft, new Point(textView.ActualWidth, rect.Top));
                            break;
                        }
                        // 
                    }
                }
            }


            //foreach (var line in textView.VisualLines) {
            //    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line.FirstDocumentLine)) {
            //        drawingContext.DrawRectangle(c % 2 == 0 ? Brushes.LightGray : Brushes.LightBlue, null,
            //                                     new Rect(rect.Location, new Size(textView.ActualWidth, rect.Height)));
            //    }

            //    //line.ValidateVisualColumn()
            //    c++;
            //}
        }
    }
}
