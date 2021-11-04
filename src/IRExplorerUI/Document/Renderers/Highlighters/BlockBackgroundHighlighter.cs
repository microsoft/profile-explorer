// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public sealed class BlockBackgroundHighlighter : IBackgroundRenderer {
        private Pen blockSeparatorPen_;
        private Brush oddBlockBrush_;
        private TextSegmentCollection<IRSegment> segments_;

        public BlockBackgroundHighlighter(bool showSeparatorLine, Color separatorLineColor,
                                          Color evenBlockColor, Color oddBlockColor) {
            segments_ = new TextSegmentCollection<IRSegment>();
            oddBlockBrush_ = ColorBrushes.GetBrush(oddBlockColor);

            if (showSeparatorLine) {
                blockSeparatorPen_ = ColorPens.GetPen(separatorLineColor);
            }
        }

        public KnownLayer Layer => KnownLayer.Background;

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
            int viewEnd = visualLines[^1].LastDocumentLine.EndOffset;

            var firstLinePos = visualLines[0].GetVisualPosition(0, VisualYPosition.LineTop);
            double scrollOffsetY = textView.ScrollOffset.Y % textView.DefaultLineHeight;
            

            // When a new line becomes the first in the view
            // it seems to happen after this code runs, so consider
            // the next one instead as first to get proper coordinates.
            //if (scrollOffsetY == 0 && textView.ScrollOffset.Y > 0 &&
            //    visualLines.Count > 1) {
            //    Trace.WriteLine($"=> Instead of {firstLinePos}");
            //    firstLinePos.Y -= textView.DefaultLineHeight;
            //    Trace.WriteLine($"    use  {firstLinePos}");
            //}

            double lineAdjustmentY = firstLinePos.Y + scrollOffsetY;
            int minOffset = viewStart;
            int maxOffset = visualLines[^1].LastDocumentLine.EndOffset;
            double maxViewHeight = textView.ActualHeight;
            var oddGeoBuilder = CreateGeometryBuilder();
            Span<Rect> separatorLines = stackalloc Rect[visualLines.Count + 1];
            int separatorLineCount = 0;

            foreach (var segment in segments_.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
                var block = segment.Element as BlockIR;

                if (block == null) {
                    continue;
                }

                var startLine = textView.Document.GetLineByOffset(Math.Max(minOffset, segment.StartOffset));
                var endLine = textView.Document.GetLineByOffset(Math.Min(maxOffset, segment.EndOffset));
                var startLineVisual = textView.GetVisualLine(startLine.LineNumber);
                var endLineVisual = textView.GetVisualLine(endLine.LineNumber);
                var startLinePos = startLineVisual.GetVisualPosition(0, VisualYPosition.LineTop);
                var endLinePos = endLineVisual.GetVisualPosition(0, VisualYPosition.LineBottom);

                var blockRect = Utils.SnapRectToPixels(0, startLinePos.Y - lineAdjustmentY, textView.ActualWidth,
                                                       Math.Min(maxViewHeight, endLinePos.Y - startLinePos.Y));

                if (block.HasOddIndexInFunction) {
                    oddGeoBuilder.AddRectangle(textView, blockRect);
                }

                // Draw separator line between blocks, if it doesn't end up outside the view.
                if (blockRect.Bottom <= maxViewHeight && separatorLineCount < separatorLines.Length) {
                    separatorLines[separatorLineCount] = blockRect;
                    separatorLineCount++;
                }
            }

            var oddGeometry = oddGeoBuilder.CreateGeometry();

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

        public void Add(IRElement elem) {
            segments_.Add(new IRSegment(elem));
        }

        public void Clear() {
            segments_.Clear();
        }

        public object SaveState() {
            return segments_.ToList();
        }

        public void LoadState(object stateObject) {
            segments_ = new TextSegmentCollection<IRSegment>();
            var list = stateObject as List<IRSegment>;
            list.ForEach(item => Add(item.Element));
        }

        private BackgroundGeometryBuilder CreateGeometryBuilder() {
            var geoBuilder = new BackgroundGeometryBuilder();
            geoBuilder.ExtendToFullWidthAtLineEnd = true;
            geoBuilder.AlignToWholePixels = true;
            geoBuilder.BorderThickness = 0;
            geoBuilder.CornerRadius = 0;
            return geoBuilder;
        }
    }
}
