// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerUI.Utilities;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;
using System;

namespace IRExplorerUI.Document {
    public class OverlayRenderer : Canvas, IBackgroundRenderer {
        class VisualHost : UIElement {
            public Visual Visual { get; set; }

            protected override int VisualChildrenCount => Visual != null ? 1 : 0;

            protected override Visual GetVisualChild(int index) {
                return Visual;
            }
        }

        class RemarkSegment : TextSegment {
            public RemarkSegment(Remark remark) {
                Remark = remark;
                var element = remark.ReferencedElements[0];
                StartOffset = element.TextLocation.Offset;
                Length = element.TextLength;
            }

            public Remark Remark { get; set; }
        }

        private static readonly Typeface DefaultFont = new Typeface("Consolas");
        private ElementHighlighter highlighter_;
        private List<Remark> contextRemarks_;
        private TextSegmentCollection<RemarkSegment> contextRemarkSegments_;
        private Dictionary<Remark, RemarkSegment> contextRemarkMap_;

        public OverlayRenderer(ElementHighlighter highlighter) {
            SnapsToDevicePixels = true;
            Background = null;
            highlighter_ = highlighter;
            contextRemarks_ = new List<Remark>();
            contextRemarkSegments_ = new TextSegmentCollection<RemarkSegment>();
            contextRemarkMap_ = new Dictionary<Remark, RemarkSegment>();

            //MouseMove += OverlayRenderer_MouseMove;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void AddContextRemark(Remark remark) {
            contextRemarks_.Add(remark);
            var segment = new RemarkSegment(remark);
            contextRemarkSegments_.Add(segment);
            contextRemarkMap_[remark] = segment;
        }

        public void ClearContextRemarks() {
            contextRemarks_.Clear();
            contextRemarkSegments_.Clear();
            contextRemarkMap_.Clear();
        }

        private Rect GetRemarkSegmentRect(Remark remark, TextView textView) {
            var segment = contextRemarkMap_[remark];

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
                return rect;
            }

            return Rect.Empty;
        }

        private double FindBezierControlPointFactor(Point a, Point b) {
            const double startLength = 20;
            const double startFactor = 0.5;
            const double endLength = 1000;
            const double endFactor = 0.2;
            const double slope = (endFactor - startFactor) / (endLength - startLength);
            const double intercept = slope * startLength - startFactor;
            double distance = (a - b).Length;
            return Math.Clamp(distance * slope + intercept, endFactor, startFactor);
        }

        public void Draw(TextView textView, DrawingContext drawingContext) {
            Width = textView.RenderSize.Width;
            Height = textView.RenderSize.Height;
            Reset();

            if (textView.Document == null) {
                return;
            }

            if (textView.Document.TextLength == 0) {
                return;
            }

            // Find start/end index of visible lines.
            textView.EnsureVisualLines();
            var visualLines = textView.VisualLines;

            if (visualLines.Count == 0) {
                return;
            }

            var visual = new DrawingVisual();
            var overlayDC = visual.RenderOpen();

            if (highlighter_.Groups.Count > 0) {
                int viewStart = visualLines[0].FirstDocumentLine.Offset;
                int viewEnd = visualLines[^1].LastDocumentLine.EndOffset;

                // Query and draw visible segments from each group.
                foreach (var group in highlighter_.Groups) {
                    DrawGroup(group, textView, overlayDC, viewStart, viewEnd);
                }
            }

            double dotSize = 8;

            var edgeGeometry = new StreamGeometry();
            var edgeSC = edgeGeometry.Open();

            var dotBackground = ColorBrushes.GetTransparentBrush(Colors.LightBlue, 200);
            var dotPen = Pens.GetTransparentPen(Colors.DimGray, 200);

            // Draw extra annotations for remarks in the same context.
            for (int i = 1; i < contextRemarks_.Count; i++) {
                var prevRemark = contextRemarks_[i - 1];
                var remark = contextRemarks_[i];
                var prevSegmentRect = GetRemarkSegmentRect(prevRemark, textView);
                var segmentRect = GetRemarkSegmentRect(remark, textView);
                var element = prevRemark.ReferencedElements[0];

                double horizontalOffset = 10;
                double verticalOffset = prevSegmentRect.Height / 2;

                var startPoint = Utils.SnapPointToPixels(prevSegmentRect.Right + horizontalOffset, prevSegmentRect.Top + verticalOffset);
                //var middlePoint = Utils.SnapPointToPixels(prevSegmentRect.Right + horizontalOffset, segmentRect.Top + verticalOffset);
                var endPoint = Utils.SnapPointToPixels(segmentRect.Right + horizontalOffset, segmentRect.Top + verticalOffset);

                var edgeStartPoint = startPoint;
                var edgeEndPoint = endPoint;

                double dx = endPoint.X - startPoint.X;
                double dy = endPoint.Y - startPoint.Y;
                var vect = new Vector(dy, -dx);
                var middlePoint = new Point(startPoint.X + dx / 2, startPoint.Y + dy / 2);

                double factor = FindBezierControlPointFactor(startPoint, endPoint);
                //double direction = remark.ReferencedElements[0].TextLocation.Line >
                //                   prevRemark.ReferencedElements[0].TextLocation.Line ? -0.5 : 0.5;
                var controlPoint = middlePoint + (-factor * vect);

                //overlayDC.DrawLine(Pens.GetPen(Colors.Red, 2), startPoint, middlePoint);
                //overlayDC.DrawLine(Pens.GetPen(Colors.Red, 2), middlePoint, endPoint);

                var startOrientation = FindArrowOrientation(new Point[] { edgeEndPoint, edgeStartPoint, controlPoint }, out var _);
                var orientation = FindArrowOrientation(new Point[] { edgeStartPoint, controlPoint, edgeEndPoint }, out var _);


                edgeStartPoint = edgeStartPoint + (startOrientation * dotSize);
                edgeEndPoint = edgeEndPoint - (orientation * dotSize * 1.75);
                edgeSC.BeginFigure(edgeStartPoint, false, false);
                edgeSC.BezierTo(edgeStartPoint, controlPoint, edgeEndPoint, true, false);
                DrawEdgeArrow(new Point[] { edgeStartPoint, controlPoint, edgeEndPoint }, edgeSC);

                //overlayDC.DrawLine(Pens.GetPen(Colors.Red, 2), startPoint, endPoint);
                //overlayDC.DrawLine(Pens.GetPen(Colors.Red, 2), point, point2);

                overlayDC.DrawEllipse(dotBackground, dotPen, startPoint, dotSize, dotSize);

                //var text = DocumentUtils.CreateFormattedText(textView, i.ToString(), DefaultFont, 11, Brushes.Black);
                //overlayDC.DrawText(text, Utils.SnapPointToPixels(startPoint.X - text.Width / 2, startPoint.Y - text.Height / 2));
            }

            if (contextRemarks_.Count >= 2) {
                var prevRemark = contextRemarks_[^1];
                var prevSegmentRect = GetRemarkSegmentRect(prevRemark, textView);
                var element = prevRemark.ReferencedElements[0];

                double horizontalOffset = 10;
                double verticalOffset = prevSegmentRect.Height / 2;

                var startPoint = Utils.SnapPointToPixels(prevSegmentRect.Right + horizontalOffset, prevSegmentRect.Top + verticalOffset);
                overlayDC.DrawEllipse(dotBackground, dotPen, startPoint, dotSize, dotSize);

                //var text = DocumentUtils.CreateFormattedText(textView, contextRemarks_.Count.ToString(), DefaultFont, 11, Brushes.Black);
                //overlayDC.DrawText(text, Utils.SnapPointToPixels(startPoint.X - text.Width / 2, startPoint.Y - text.Height / 2));
            }


            edgeSC.Close();
            edgeGeometry.Freeze();
            overlayDC.DrawGeometry(ColorBrushes.GetTransparentBrush(Colors.DarkRed, 180), Pens.GetTransparentPen(Colors.DarkRed, 180, 2), edgeGeometry);

            overlayDC.Close();
            Add(visual);
        }


        private void DrawEdgeArrow(Point[] tempPoints, StreamGeometryContext sc) {
            // Draw arrow head with a slope matching the line,
            // this uses the last two points to find the angle.
            Point start;
            Vector v = FindArrowOrientation(tempPoints, out start);

            sc.BeginFigure(start + v * 5, true, true);
            double t = v.X;
            v.X = v.Y;
            v.Y = -t; // Rotate 90
            sc.LineTo(start + v * 5, true, true);
            sc.LineTo(start + v * -5, true, true);
        }

        private Vector FindArrowOrientation(Point[] tempPoints, out Point start) {
            for (int i = tempPoints.Length - 1; i > 0; i--) {
                start = tempPoints[i];
                var v = start - tempPoints[i - 1];

                if (v.LengthSquared != 0) {
                    v.Normalize();
                    return v;
                }
            }

            return new Vector(0, 0);
        }

        //private void OverlayRenderer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        //{
        //    if (!FindVisibleText(out int viewStart, out int viewEnd))
        //    {
        //        return;
        //    }

        //    var point = e.GetPosition(this);

        //    foreach (var group in highlighter_.Groups)
        //    {
        //        foreach(var segment in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart))
        //        {
        //            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView_, segment))
        //            {
        //                // check intersection
        //                // mark as hovered
        //                // redraw
        //            }
        //        }
        //    }
        //}

        public void Reset() {
            Children.Clear();
        }

        public void Add(Visual drawingVisual) {
            Children.Add(new VisualHost { Visual = drawingVisual });
        }

        public void Add(UIElement element) {
            Children.Add(element);
        }

        private void DrawGroup(HighlightedSegmentGroup group, TextView textView,
                               DrawingContext drawingContext, int viewStart, int viewEnd) {
            IRElement element = null;
            double fontSize = App.Settings.DocumentSettings.FontSize;

            foreach (var segment in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
                element = segment.Element;

                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
                    var notesTag = element.GetTag<NotesTag>();

                    if (notesTag != null) {
                        string label = notesTag.Title;

                        //? TODO: Show only on hover
                        //if(notesTag.Notes.Count > 0)
                        //{
                        //    label += $", {notesTag.Notes[0]}";
                        //}

                        var pen = group.Border ?? Pens.GetPen(Colors.Gray);
                        var text = DocumentUtils.CreateFormattedText(textView, label, DefaultFont,
                                                                     fontSize, Brushes.Black);
                        drawingContext.DrawRectangle(group.BackColor, pen,
                                                     Utils.SnapRectToPixels(rect.X + rect.Width + 8, rect.Y,
                                                                            text.Width + 10,
                                                                            textView.DefaultLineHeight + 1));
                        drawingContext.DrawText(text, Utils.SnapPointToPixels(rect.X + rect.Width + 12, rect.Y + 1));
                    }
                }
            }
        }
    }
}
