// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Core.IR;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Client {
    public sealed class BlockBackgroundHighlighter : IBackgroundRenderer {
        private TextSegmentCollection<IRSegment> segments_;
        private Pen blockSeparatorPen_;
        private Brush oddBlockBrush_;

        public BlockBackgroundHighlighter(bool showSeparatorLine, Color separatorLineColor,
                                          Color evenBlockColor, Color oddBlockColor) {
            segments_ = new TextSegmentCollection<IRSegment>();
            oddBlockBrush_ = ColorBrushes.GetBrush(oddBlockColor);

            if (showSeparatorLine) {
                blockSeparatorPen_ = Pens.GetPen(separatorLineColor);
            }
        }

        public void Add(IRElement elem) {
            segments_.Add(new IRSegment(elem));
        }

        public void Clear() {
            segments_.Clear();
        }

        public KnownLayer Layer {
            get { return KnownLayer.Background; }
        }

        public object SaveState() {
            return segments_.ToList();
        }

        public void LoadState(object stateObject) {
            segments_ = new TextSegmentCollection<IRSegment>();
            var list = stateObject as List<IRSegment>;
            list.ForEach((item) => Add(item.Element));
        }

        private BackgroundGeometryBuilder CreateGeometryBuilder() {
            var geoBuilder = new BackgroundGeometryBuilder();
            geoBuilder.ExtendToFullWidthAtLineEnd = true;
            geoBuilder.AlignToWholePixels = true;
            geoBuilder.BorderThickness = 0;
            geoBuilder.CornerRadius = 0;
            return geoBuilder;
        }

        public void Draw(TextView textView, DrawingContext drawingContext) {
            if (textView.Document == null) {
                return;
            }

            textView.EnsureVisualLines();
            var visualLines = textView.VisualLines;

            if (visualLines.Count == 0) {
                return;
            }

            int viewStart = visualLines[0].FirstDocumentLine.Offset;
            int viewEnd = visualLines[visualLines.Count - 1].LastDocumentLine.EndOffset;

            var firstLinePos = visualLines[0].GetVisualPosition(0, VisualYPosition.LineTop);
            var scrollOffsetY = textView.ScrollOffset.Y % textView.DefaultLineHeight;
            var lineAdjustmentY = firstLinePos.Y + scrollOffsetY;

            int minOffset = viewStart;
            int maxOffset = visualLines[visualLines.Count - 1].LastDocumentLine.EndOffset;
            var maxViewHeight = textView.ActualHeight;

            var oddGeoBuilder = CreateGeometryBuilder();
            Span<Rect> separatorLines = stackalloc Rect[visualLines.Count + 1];
            int separatorLineCount = 0;

            foreach (var segment in segments_.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
                var block = (segment as IRSegment).Element as BlockIR;

                if (block == null) {
                    continue;
                }

                var startLine = textView.Document.GetLineByOffset(Math.Max(minOffset, segment.StartOffset));
                var endLine = textView.Document.GetLineByOffset(Math.Min(maxOffset, segment.EndOffset));
                var startLineVisual = textView.GetVisualLine(startLine.LineNumber);
                var endLineVisual = textView.GetVisualLine(endLine.LineNumber);
                var startLinePos = startLineVisual.GetVisualPosition(0, VisualYPosition.LineTop);
                var endLinePos = endLineVisual.GetVisualPosition(0, VisualYPosition.LineBottom);

                var blockRect = new Rect(0, startLinePos.Y - lineAdjustmentY, textView.ActualWidth,
                                         Math.Min(maxViewHeight, endLinePos.Y - startLinePos.Y));

                if ((block.Number & 1) == 1) {
                    oddGeoBuilder.AddRectangle(textView, blockRect);
                }

                // Draw separator line between blocks, if it doesn't end up outside the view.
                if (blockRect.Bottom < maxViewHeight) {
                    separatorLines[separatorLineCount] = blockRect;
                    separatorLineCount++;
                }
            }

            Geometry oddGeometry = oddGeoBuilder.CreateGeometry();

            if (oddGeometry != null) {
                drawingContext.DrawGeometry(oddBlockBrush_, null, oddGeometry);
            }

            if (blockSeparatorPen_ != null) {
                for (int i = 0; i < separatorLineCount; i++) {
                    drawingContext.DrawLine(blockSeparatorPen_, separatorLines[i].BottomLeft,
                        new Point(textView.ActualWidth, separatorLines[i].Bottom));

                }
            }
        }
    }
}
