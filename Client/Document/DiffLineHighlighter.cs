// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Client {
    public enum DiffKind {
        None,
        Insertion,
        Deletion,
        Modification,
        MinorModification,
        Placeholder
    }

    public class DiffTextSegment : TextSegment {
        public DiffKind Kind { get; set; }

        public DiffTextSegment(DiffKind kind, int startOffset, int length) {
            Kind = kind;
            StartOffset = startOffset;
            Length = length;
        }

        public bool IsContinuation(DiffTextSegment otherSegment) {
            if (Kind != otherSegment.Kind) {
                return false;
            }

            // Whole-line changes don't include the new line characters,
            // take those into consideration to properly identify a block of changes.
            var otherEndOffset = otherSegment.StartOffset + otherSegment.Length;
            return Math.Abs(StartOffset - otherEndOffset) <= Environment.NewLine.Length;
        }
    }

    public sealed class DiffLineHighlighter : IBackgroundRenderer {
        private TextSegmentCollection<DiffTextSegment> segments_;
        private Pen placeholderPen_;
        private Pen deletionPen_;
        private Pen insertionPen_;
        private Pen modificationPen_;
        private Pen minorModificationPen_;
        private Brush placeholderBrush_;
        private Brush insertionBrush_;
        private Brush deletionBrush_;
        private Brush modificationBrush_;
        private Brush minorModificationBrush_;
        private DrawingBrush placeholderTileBrush_;

        public int Version { get; set; }


        public DiffLineHighlighter() {
            segments_ = new TextSegmentCollection<DiffTextSegment>();
            placeholderPen_ = null;
            deletionPen_ = Pens.GetBoldPen("#B33232");
            insertionPen_ = Pens.GetPen("#7FA72E");
            modificationPen_ = Pens.GetPen("#ff6f00");
            minorModificationPen_ = Pens.GetPen("#8F8F8F");
            placeholderBrush_ = Utils.BrushFromString("#DFDFDF");
            insertionBrush_ = Utils.BrushFromString("#c5e1a5");
            deletionBrush_ = Utils.BrushFromString("#FFD6D9");
            modificationBrush_ = Utils.BrushFromString("#FFF6D9");
            minorModificationBrush_ = Utils.BrushFromString("#E1E1E1");
        }

        public void ForEachDiffSegment(Action<DiffTextSegment, Color> action) {
            foreach (var segment in segments_) {
                var pen = GetSegmentColor(segment, fromDrawing: false);
                var color = pen != null ? ((SolidColorBrush)pen).Color : Colors.Transparent;
                action(segment, color);
            }
        }

        public void Add(DiffTextSegment segment) {
            segments_.Add(segment);
            Version++;
        }

        public void Add(List<DiffTextSegment> segments) {
            foreach (var segment in segments) {
                segments_.Add(segment);
            }

            Version++;
        }

        public void Clear() {
            segments_.Clear();
            Version++;
        }

        public KnownLayer Layer {
            get { return KnownLayer.Background; }
        }

        void CreatePlaceholderTiledBrush(double tileSize) {
            if (placeholderTileBrush_ != null) {
                return;
            }
            
            tileSize = Math.Ceiling(tileSize);
            var line = new LineSegment(new Point(0, 0), true);
            line.IsSmoothJoin = false;
            line.Freeze();

            var figure = new PathFigure();
            figure.IsClosed = false;
            figure.StartPoint = new Point(tileSize, tileSize);
            figure.Segments.Add(line);
            figure.Freeze();

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            var drawing = new GeometryDrawing();
            drawing.Geometry = geometry;
            drawing.Pen = new Pen(Brushes.DarkGray, 0.5);
            drawing.Freeze();

            var brush = new DrawingBrush();
            brush.Drawing = drawing;
            brush.Stretch = Stretch.None;
            brush.TileMode = TileMode.Tile;
            brush.Viewbox = new Rect(0, 0, tileSize, tileSize);
            brush.ViewboxUnits = BrushMappingMode.Absolute;
            brush.Viewport = new Rect(0, 0, tileSize, tileSize);
            brush.ViewportUnits = BrushMappingMode.Absolute;

            RenderOptions.SetCachingHint(brush, CachingHint.Cache);
            brush.Freeze();
            placeholderTileBrush_ = brush;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Brush GetSegmentColor(DiffTextSegment segment, bool fromDrawing = true) {
            switch (segment.Kind) {
                case DiffKind.Deletion: return deletionBrush_;
                case DiffKind.Insertion: return insertionBrush_;
                case DiffKind.Placeholder: {
                    if (fromDrawing) {
                        return placeholderTileBrush_;
                    }
                    return placeholderBrush_;
                }
                case DiffKind.Modification: return modificationBrush_;
                case DiffKind.MinorModification: return minorModificationBrush_;
            }

            return Brushes.Transparent;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Pen GetSegmentPen(DiffTextSegment segment) {
            switch (segment.Kind) {
                case DiffKind.Deletion: return deletionPen_;
                case DiffKind.Insertion: return insertionPen_;
                case DiffKind.Modification: return modificationPen_;
                case DiffKind.MinorModification: return minorModificationPen_;
                case DiffKind.Placeholder: return placeholderPen_;
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private BackgroundGeometryBuilder CreateGeometryBuilder() {
            var geoBuilder = new BackgroundGeometryBuilder();
            geoBuilder.ExtendToFullWidthAtLineEnd = false;
            geoBuilder.AlignToWholePixels = true;
            geoBuilder.BorderThickness = 0;
            geoBuilder.CornerRadius = 0;
            return geoBuilder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawGeometry(BackgroundGeometryBuilder geoBuilder, DiffTextSegment segment,
                                  DrawingContext drawingContext) {
            var geometry = geoBuilder.CreateGeometry();

            if (geometry != null) {
                drawingContext.DrawGeometry(GetSegmentColor(segment), 
                                            GetSegmentPen(segment), geometry);
            }
        }

        public void Draw(TextView textView, DrawingContext drawingContext) {
            if(textView.Document == null) {
                return;
            }

            if (textView.Document.TextLength == 0) {
                return;
            }

            textView.EnsureVisualLines();
            var visualLines = textView.VisualLines;

            if (visualLines.Count == 0) {
                return;
            }

            int viewStart = visualLines[0].FirstDocumentLine.Offset;
            int viewEnd = visualLines[visualLines.Count - 1].LastDocumentLine.EndOffset;

            BackgroundGeometryBuilder geoBuilder = null;
            DiffTextSegment prevSegment = null;

            //? TODO: Can be made more efficient by having one GeometryGBuilder for each type of diff,
            //? then at the end render each one that was used
            foreach (var segment in segments_.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
                if (segment.Kind == DiffKind.Placeholder) {
                    CreatePlaceholderTiledBrush(textView.DefaultLineHeight / 2);
                }

                if (geoBuilder == null) {
                    geoBuilder = CreateGeometryBuilder();
                }
                else if (prevSegment != null && !segment.IsContinuation(prevSegment)) {
                    DrawGeometry(geoBuilder, prevSegment, drawingContext);
                    geoBuilder = CreateGeometryBuilder();
                }

                geoBuilder.AddSegment(textView, segment);
                prevSegment = segment;
            }

            if (geoBuilder != null && prevSegment != null) {
                DrawGeometry(geoBuilder, prevSegment, drawingContext);
            }
        }
    }
}
