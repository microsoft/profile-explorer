// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using CoreLib.IR;
using ICSharpCode.AvalonEdit.Rendering;

namespace Client {
    public sealed class RemarkHighlighter : IBackgroundRenderer {
        //? TODO: Change to Set to have faster Remove (search panel)
        private List<HighlightedSegmentGroup> groups_;
        private Dictionary<Color, Brush> remarkBrushCache_;
        private int remarkBackgroundOpacity_;
        private bool useTransparentRemarkBackground_;

        public RemarkHighlighter(HighlighingType type) {
            Type = type;
            groups_ = new List<HighlightedSegmentGroup>(64);
            remarkBrushCache_ = new Dictionary<Color, Brush>();
            Version = 1;
        }

        private void InvalidateRemarkBrushCache() {
            if (App.Settings.RemarkSettings.UseTransparentRemarkBackground != useTransparentRemarkBackground_ ||
                App.Settings.RemarkSettings.RemarkBackgroundOpacity != remarkBackgroundOpacity_) {
                remarkBrushCache_.Clear();
                useTransparentRemarkBackground_ = App.Settings.RemarkSettings.UseTransparentRemarkBackground;
                remarkBackgroundOpacity_ = App.Settings.RemarkSettings.RemarkBackgroundOpacity;
            }
        }

        private Brush GetRemarkBackgroundBrush(HighlightedSegmentGroup group) {
            if (!App.Settings.RemarkSettings.UseRemarkBackground) {
                return Brushes.Transparent;
            }

            var color = ((SolidColorBrush)group.BackColor).Color;

            if (color == Colors.Black || color == Colors.Transparent) {
                return Brushes.Transparent;
            }

            if (remarkBrushCache_.TryGetValue(color, out var brush)) {
                return brush;
            }

            if (useTransparentRemarkBackground_) {
                var alpha = (byte)(255.0 * ((double)remarkBackgroundOpacity_ / 100.0));
                brush = ColorBrushes.GetBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            } else {
                brush = ColorBrushes.GetBrush(color);
            }

            remarkBrushCache_[color] = brush;
            return brush;
        }

        public HighlighingType Type { get; set; }
        public int Version { get; set; }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext) {
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

            int viewStart = visualLines[0].FirstDocumentLine.Offset;
            int viewEnd = visualLines[^1].LastDocumentLine.EndOffset;
            InvalidateRemarkBrushCache();

            // Query and draw visible segments from each group.
            foreach (var group in groups_) {
                DrawGroup(group, textView, drawingContext, viewStart, viewEnd);
            }
        }

        public void Add(HighlightedGroup group, bool saveToFile = true) {
            groups_.Add(new HighlightedSegmentGroup(group, saveToFile));
            Version++;
        }

        public void AddFront(HighlightedGroup group, bool saveToFile = true) {
            groups_.Insert(0, new HighlightedSegmentGroup(group, saveToFile));
            Version++;
        }

        public void CopyFrom(RemarkHighlighter other) {
            foreach (var item in other.groups_) {
                groups_.Add(new HighlightedSegmentGroup(item.Group));
            }

            Version++;
        }

        public void Remove(HighlightedGroup group) {
            for (int i = 0; i < groups_.Count; i++) {
                if (groups_[i].Group == group) {
                    groups_.RemoveAt(i);
                    break;
                }
            }

            Version++;
        }

        public void Remove(IRElement element) {
            for (int i = 0; i < groups_.Count; i++) {
                groups_[i].Group.Remove(element);

                if (groups_[i].Group.IsEmpty()) {
                    groups_.RemoveAt(i);
                    i--;
                }
            }

            Version++;
        }

        public void Clear() {
            groups_.Clear();
            Version++;
        }

        public void ForEachElement(Action<IRElement> action) {
            groups_.ForEach(group => { group.Group.Elements.ForEach(action); });
        }

        public void ForEachStyledElement(Action<IRElement, HighlightingStyle> action) {
            groups_.ForEach(group => {
                group.Group.Elements.ForEach(element => { action(element, group.Group.Style); });
            });
        }

        private void DrawGroup(HighlightedSegmentGroup group, TextView textView,
                               DrawingContext drawingContext, int viewStart, int viewEnd) {
            var geoBuilder = new BackgroundGeometryBuilder {
                AlignToWholePixels = true,
                BorderThickness = 0,
                CornerRadius = 0
            };

            foreach (var segment in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
                // segment.Element
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
                    var actualRect = Utils.SnapToPixels(rect, -1, 0, 2, 0);
                    geoBuilder.AddRectangle(textView, actualRect);
                }
            }

            var geometry = geoBuilder.CreateGeometry();

            if (geometry != null) {
                var brush = GetRemarkBackgroundBrush(group);
                drawingContext.DrawGeometry(brush, group.Border, geometry);
            }
        }
    }
}
