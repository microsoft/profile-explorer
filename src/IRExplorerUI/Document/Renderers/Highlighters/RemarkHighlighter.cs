// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore.IR;
using IRExplorerUI.Document;

namespace IRExplorerUI;

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

  public HighlighingType Type { get; set; }
  public int Version { get; set; }
  public KnownLayer Layer => KnownLayer.Background;

  public void Add(HighlightedElementGroup group, bool saveToFile = true) {
    groups_.Add(new HighlightedSegmentGroup(group, saveToFile));
    Version++;
  }

  public void AddFront(HighlightedElementGroup group, bool saveToFile = true) {
    groups_.Insert(0, new HighlightedSegmentGroup(group, saveToFile));
    Version++;
  }

  public void CopyFrom(RemarkHighlighter other) {
    foreach (var item in other.groups_) {
      groups_.Add(new HighlightedSegmentGroup(item.Group));
    }

    Version++;
  }

  public void Remove(HighlightedElementGroup group) {
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

  public void ChangeStyle(IRElement element, HighlightingStyle newStyle) {
    for (int i = 0; i < groups_.Count; i++) {
      if (groups_[i].Group.Contains(element)) {
        groups_[i].Group.Style = newStyle;
        break;
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

  public void Draw(TextView textView, DrawingContext drawingContext) {
    if (textView.Document == null || textView.Document.TextLength == 0) {
      return;
    }

    // Find start/end index of visible lines.
    if (!DocumentUtils.FindVisibleTextOffsets(textView, out int viewStart, out int viewEnd)) {
      return;
    }

    InvalidateRemarkBrushCache();

    // Query and draw visible segments from each group.
    foreach (var group in groups_) {
      DrawGroup(group, textView, drawingContext, viewStart, viewEnd);
    }
  }

  private void DrawGroup(HighlightedSegmentGroup group, TextView textView,
                         DrawingContext drawingContext, int viewStart, int viewEnd) {
    // Create BackgroundGeometryBuilder only if needed.
    BackgroundGeometryBuilder geoBuilder = null;

    foreach (var segment in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
      if (geoBuilder == null) {
        geoBuilder = new BackgroundGeometryBuilder {
          AlignToWholePixels = true,
          BorderThickness = 0,
          CornerRadius = 0
        };
      }

      foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
        var actualRect = Utils.SnapRectToPixels(rect, -1, 0, 2, 1);
        geoBuilder.AddRectangle(textView, actualRect);
      }
    }

    if (geoBuilder != null) {
      var geometry = geoBuilder.CreateGeometry();

      if (geometry != null) {
        var brush = GetRemarkBackgroundBrush(group);
        drawingContext.DrawGeometry(brush, group.Border, geometry);
      }
    }
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
      byte alpha = (byte)(255.0 * (remarkBackgroundOpacity_ / 100.0));
      brush = ColorBrushes.GetBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }
    else {
      brush = ColorBrushes.GetBrush(color);
    }

    remarkBrushCache_[color] = brush;
    return brush;
  }
}
