// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ProfileExplorer.Core.Document.Renderers.Highlighters;

public enum DiffKind {
  None,
  Insertion,
  Deletion,
  Modification,
  MinorModification,
  Placeholder
}

/*public sealed class DiffTextSegment : TextSegment {
  public DiffTextSegment(DiffKind kind, int startOffset, int length) {
    Kind = kind;
    StartOffset = startOffset;
    Length = length;
  }

  public DiffTextSegment(DiffTextSegment other) :
    this(other.Kind, other.StartOffset, other.Length) {
  }

  public DiffKind Kind { get; set; }

  public bool IsContinuation(DiffTextSegment otherSegment) {
    if (Kind != otherSegment.Kind) {
      return false;
    }

    // Whole-line changes don't include the new line characters,
    // take those into consideration to properly identify a block of changes.
    int otherEndOffset = otherSegment.StartOffset + otherSegment.Length;
    return Math.Abs(StartOffset - otherEndOffset) <= Environment.NewLine.Length;
  }
}

public sealed class DiffLineHighlighter : IBackgroundRenderer {
  private Brush deletionBrush_;
  private Pen deletionPen_;
  private Brush insertionBrush_;
  private Pen insertionPen_;
  private Brush minorModificationBrush_;
  private Pen minorModificationPen_;
  private Brush modificationBrush_;
  private Pen modificationPen_;
  private Pen placeholderPen_;
  private DrawingBrush placeholderTileBrush_;
  private TextSegmentCollection<DiffTextSegment> segments_;

  public DiffLineHighlighter() {
    segments_ = new TextSegmentCollection<DiffTextSegment>();
    placeholderPen_ = null;
    deletionPen_ = ColorPens.GetPen(App.Settings.DiffSettings.DeletionBorderColor);
    insertionPen_ = ColorPens.GetPen(App.Settings.DiffSettings.InsertionBorderColor);
    modificationPen_ = ColorPens.GetPen(App.Settings.DiffSettings.ModificationBorderColor);
    minorModificationPen_ = ColorPens.GetPen(App.Settings.DiffSettings.MinorModificationBorderColor);
    insertionBrush_ = ColorBrushes.GetBrush(App.Settings.DiffSettings.InsertionColor);
    deletionBrush_ = ColorBrushes.GetBrush(App.Settings.DiffSettings.DeletionColor);
    modificationBrush_ = ColorBrushes.GetBrush(App.Settings.DiffSettings.ModificationColor);
    minorModificationBrush_ = ColorBrushes.GetBrush(App.Settings.DiffSettings.MinorModificationColor);
  }

  public int Version { get; set; }
  public KnownLayer Layer => KnownLayer.Background;

  public void Draw(TextView textView, DrawingContext drawingContext) {
    if (textView.Document == null || textView.Document.TextLength == 0) {
      return;
    }

    // Find start/end index of visible lines.
    if (!DocumentUtils.FindVisibleTextOffsets(textView, out int viewStart, out int viewEnd)) {
      return;
    }

    BackgroundGeometryBuilder geoBuilder = null;
    DiffTextSegment prevSegment = null;
    CreatePlaceholderTiledBrush(textView.DefaultLineHeight / 2);

    foreach (var segment in segments_.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
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

  public void ForEachDiffSegment(Action<DiffTextSegment, Color> action) {
    foreach (var segment in segments_) {
      var pen = GetSegmentColor(segment, false);
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

  private void CreatePlaceholderTiledBrush(double tileSize) {
    // Create the brush once, freeze and reuse it everywhere.
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

    var penBrush = ColorBrushes.GetBrush(App.Settings.DiffSettings.PlaceholderBorderColor);
    drawing.Pen = new Pen(penBrush, 0.5);
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
  private Brush GetSegmentColor(DiffTextSegment segment, bool fromDrawing = true) {
    switch (segment.Kind) {
      case DiffKind.Deletion:
        return deletionBrush_;
      case DiffKind.Insertion:
        return insertionBrush_;
      case DiffKind.Placeholder: {
        return fromDrawing ? placeholderTileBrush_ : null;
      }
      case DiffKind.Modification:
        return modificationBrush_;
      case DiffKind.MinorModification:
        return minorModificationBrush_;
    }

    return ColorBrushes.Transparent;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private Pen GetSegmentPen(DiffTextSegment segment) {
    return segment.Kind switch {
      DiffKind.Deletion          => deletionPen_,
      DiffKind.Insertion         => insertionPen_,
      DiffKind.Modification      => modificationPen_,
      DiffKind.MinorModification => minorModificationPen_,
      DiffKind.Placeholder       => placeholderPen_,
      _                          => null
    };
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
      drawingContext.DrawGeometry(GetSegmentColor(segment), GetSegmentPen(segment), geometry);
    }
  }
}*/