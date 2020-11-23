﻿// Copyright (c) Microsoft Corporation. All rights reserved.
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
using System.Diagnostics;
using ICSharpCode.AvalonEdit;

namespace IRExplorerUI.Document {
    public class OverlayRenderer : Canvas, IBackgroundRenderer {
        class VisualHost : UIElement {
            public Visual Visual { get; set; }

            protected override int VisualChildrenCount => Visual != null ? 1 : 0;

            protected override Visual GetVisualChild(int index) {
                return Visual;
            }
        }

        class ConnectedElement {
            public ConnectedElement(IRElement element, HighlightingStyle style) {
                Element = element;
                Style = style;
            }

            public IRElement Element { get; set; }
            public HighlightingStyle Style { get; set; }
        }

        public class IROverlaySegment : IRSegment {
            public IROverlaySegment(IRElement element) : base(element) {
                Overlays = new List<IElementOverlay>();
            }

            public IROverlaySegment(IRElement element, IElementOverlay overlay) : this(element) {
                Overlays.Add(overlay);
            }

            public List<IElementOverlay> Overlays { get; set; }
        }

        private static readonly Typeface DefaultFont = new Typeface("Consolas");
        private ElementHighlighter highlighter_;
        private ConnectedElement rootConnectedElement_;
        private List<ConnectedElement> connectedElements_;
        private TextSegmentCollection<IRSegment> segments_;
        private Dictionary<IRElement, IRSegment> segmentMap_;
        
        private TextSegmentCollection<IROverlaySegment> overlaySegments_;

        public OverlayRenderer(ElementHighlighter highlighter) {
            overlaySegments_ = new TextSegmentCollection<IROverlaySegment>();
            SnapsToDevicePixels = true;
            Background = null;
            highlighter_ = highlighter;
            ClearConnectedElements();
            //MouseMove += OverlayRenderer_MouseMove;
        }

        public KnownLayer Layer => KnownLayer.Background;
        public int Version { get; set; }

        public void AddElementOverlay(IRElement element, IElementOverlay overlay) {
            overlaySegments_.Add(new IROverlaySegment(element, overlay));
        }

        public void SetRootElement(IRElement element, HighlightingStyle style) {
            ClearConnectedElements();
            rootConnectedElement_ = new ConnectedElement(element, style);
            var segment = new IRSegment(element);
            segments_.Add(segment);
            segmentMap_[element] = segment;
        }

        public void AddConnectedElement(IRElement element, HighlightingStyle style) {
            connectedElements_.Add(new ConnectedElement(element, style));
            var segment = new IRSegment(element);
            segments_.Add(segment);
            segmentMap_[element] = segment;
        }

        public void ClearConnectedElements() {
            rootConnectedElement_ = null;
            connectedElements_ = new List<ConnectedElement>();
            segments_ = new TextSegmentCollection<IRSegment>();
            segmentMap_ = new Dictionary<IRElement, IRSegment>();
        }

        private Rect GetRemarkSegmentRect(IRElement element, TextView textView) {
            var segment = segmentMap_[element];

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
                return rect;
            }

            // The segment is outside the visible document.
            // A visual line must be created for it, then extract the location.
            var line = textView.Document.GetLineByOffset(segment.StartOffset);
            var visualLine = textView.GetOrConstructVisualLine(line);
            var start = new TextViewPosition(textView.Document.GetLocation(segment.StartOffset));
            var end = new TextViewPosition(textView.Document.GetLocation(segment.StartOffset + segment.Length));
            int startColumn = visualLine.ValidateVisualColumn(start, false);
            int endColumn = visualLine.ValidateVisualColumn(end, false);

            foreach (var rect in BackgroundGeometryBuilder.
                                 GetRectsFromVisualSegment(textView, visualLine, 
                                                           startColumn, endColumn)) {
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
            Children.Clear();

            if (textView.Document == null || textView.Document.TextLength == 0) {
                return;
            }

            // Find start/end index of visible lines.
            if (!DocumentUtils.FindVisibleText(textView, out int viewStart, out int viewEnd)) {
                return;
            }

            var visual = new DrawingVisual();
            var overlayDC = visual.RenderOpen();

            if (highlighter_.Groups.Count > 0) {
                // Query and draw visible segments from each group.
                foreach (var group in highlighter_.Groups) {
                    DrawGroup(group, textView, overlayDC, viewStart, viewEnd);
                }
            }

            foreach (var segment in overlaySegments_.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
                    foreach(var overlay in segment.Overlays) {
                        overlay.Draw(rect, segment.Element, false, overlayDC);
                    }
                }
            }

            double dotSize = 3;
            var dotBackground = ColorBrushes.GetTransparentBrush(Colors.DarkRed, 255);
            var dotPen = Pens.GetTransparentPen(Colors.DarkRed, 255);
            bool first = true;

            // Draw extra annotations for remarks in the same context.
            foreach (var connectedElement in connectedElements_) {
                var prevSegmentRect = GetRemarkSegmentRect(rootConnectedElement_.Element, textView);
                var segmentRect = GetRemarkSegmentRect(connectedElement.Element, textView);

                double horizontalOffset = 4;
                double verticalOffset = prevSegmentRect.Height / 2;

                var startPoint = Utils.SnapPointToPixels(prevSegmentRect.Right + horizontalOffset, prevSegmentRect.Top + verticalOffset);
                var endPoint = Utils.SnapPointToPixels(segmentRect.Right + horizontalOffset, segmentRect.Top + verticalOffset);

                var edgeStartPoint = startPoint;
                var edgeEndPoint = endPoint;

                double dx = endPoint.X - startPoint.X;
                double dy = endPoint.Y - startPoint.Y;
                var vect = new Vector(dy, -dx);
                var middlePoint = new Point(startPoint.X + dx / 2, startPoint.Y + dy / 2);

                double factor = FindBezierControlPointFactor(startPoint, endPoint);
                var controlPoint = middlePoint + (-factor * vect);

                // Keep the control point in the horizontal bounds of the document.
                if(controlPoint.X < 0 || controlPoint.X > Width) {
                    controlPoint = new Point(Math.Clamp(controlPoint.X, 0, Width), controlPoint.Y);
                }

                //overlayDC.DrawLine(Pens.GetPen(Colors.Green, 2), startPoint, middlePoint);
                //overlayDC.DrawLine(Pens.GetPen(Colors.Green, 2), middlePoint, endPoint);

                var startOrientation = FindArrowOrientation(new Point[] { edgeEndPoint, edgeStartPoint, controlPoint }, out var _);
                var orientation = FindArrowOrientation(new Point[] { edgeStartPoint, controlPoint, edgeEndPoint }, out var _);

                edgeStartPoint = edgeStartPoint + (startOrientation * (dotSize - 1));
                edgeEndPoint = edgeEndPoint - (orientation * dotSize * 2);

                var edgeGeometry = new StreamGeometry();
                var edgeSC = edgeGeometry.Open();
                edgeSC.BeginFigure(edgeStartPoint, false, false);
                edgeSC.BezierTo(edgeStartPoint, controlPoint, edgeEndPoint, true, false);
                DrawEdgeArrow(new Point[] { edgeStartPoint, controlPoint, edgeEndPoint }, edgeSC);

                //edgeSC.BeginFigure(edgeStartPoint, false, false);
                //edgeSC.LineTo(edgeEndPoint, true, false);

                //edgeSC.BeginFigure(startPoint, false, false);
                //edgeSC.LineTo(endPoint, true, false);

                // overlayDC.DrawLine(Pens.GetPen(Colors.Red, 2), startPoint, endPoint);
                //overlayDC.DrawLine(Pens.GetPen(Colors.Red, 2), point, point2);

                // overlayDC.DrawEllipse(connectedElement.Style.BackColor, connectedElement.Style.Border, endPoint, dotSize, dotSize);

                if (first) {
                    overlayDC.DrawEllipse(rootConnectedElement_.Style.BackColor, rootConnectedElement_.Style.Border, startPoint, dotSize, dotSize);
                    first = false;
                }

                edgeSC.Close();
                edgeGeometry.Freeze();
                overlayDC.DrawGeometry(connectedElement.Style.BackColor, connectedElement.Style.Border, edgeGeometry);

                //var text = DocumentUtils.CreateFormattedText(textView, i.ToString(), DefaultFont, 11, Brushes.Black);
                //overlayDC.DrawText(text, Utils.SnapPointToPixels(startPoint.X - text.Width / 2, startPoint.Y - text.Height / 2));
            }

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

        //private void OverlayRenderer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
        //    if (!DocumentUtils.FindVisibleText(TextView, out int viewStart, out int viewEnd)) {
        //        return;
        //    }

        //    var point = e.GetPosition(this);

        //    foreach (var group in highlighter_.Groups) {
        //        foreach (var segment in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
        //            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView_, segment)) {
        //                // check intersection
        //                // mark as hovered
        //                // redraw
        //            }
        //        }
        //    }
        //}

        public void Clear() {
            Children.Clear();
            ClearConnectedElements();
            ClearElementOverlays();
        }

        private void ClearElementOverlays() {
            overlaySegments_.Clear();
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
