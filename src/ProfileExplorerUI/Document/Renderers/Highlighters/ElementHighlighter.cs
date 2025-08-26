// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using ProfileExplorerCore.IR;
using ProfileExplorer.UI.Document;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract]
public class ElementGroupState {
  [ProtoMember(1)]
  public List<IRElementReference> Elements;
  [ProtoMember(2)]
  public HighlightingStyle Style;

  public ElementGroupState() {
    Elements = new List<IRElementReference>();
  }
}

[ProtoContract]
public class ElementHighlighterState {
  [ProtoMember(1)]
  public List<ElementGroupState> Groups;

  public ElementHighlighterState() {
    Groups = new List<ElementGroupState>();
  }

  public ElementHighlighterState(List<ElementGroupState> groups) {
    Groups = groups;
  }

  public bool HasAnnotations => Groups.Count > 0;
}

public sealed class ElementHighlighter : IBackgroundRenderer {
  //? TODO: Change to Set to have faster Remove (search panel)
  private List<HighlightedSegmentGroup> groups_;

  public ElementHighlighter(HighlighingType type) {
    Type = type;
    groups_ = new List<HighlightedSegmentGroup>(64);
    Version = 1;
  }

  public HighlighingType Type { get; set; }
  public int Version { get; set; }

  public List<HighlightedSegmentGroup> Groups {
    get => groups_;
    set => groups_ = value;
  }

  public KnownLayer Layer => KnownLayer.Background;

  public void Draw(TextView textView, DrawingContext drawingContext) {
    if (textView.Document == null || textView.Document.TextLength == 0) {
      return;
    }

    // Find start/end index of visible lines.
    if (!DocumentUtils.FindVisibleTextOffsets(textView, out int viewStart, out int viewEnd)) {
      return;
    }

    // Query and draw visible segments from each group.
    foreach (var group in groups_) {
      DrawGroup(group, textView, drawingContext, viewStart, viewEnd);
    }
  }

  public void Add(HighlightedElementGroup group, bool saveToFile = true) {
    groups_.Add(new HighlightedSegmentGroup(group, saveToFile));
    Version++;
  }

  public void Add(HighlightedSegmentGroup group) {
    groups_.Add(group);
    Version++;
  }

  public void AddFront(HighlightedElementGroup group, bool saveToFile = true) {
    groups_.Insert(0, new HighlightedSegmentGroup(group, saveToFile));
    Version++;
  }

  public void CopyFrom(ElementHighlighter other) {
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

  public ElementHighlighterState SaveState(FunctionIR function) {
    return new ElementHighlighterState(StateSerializer.SaveElementGroupState(groups_));
  }

  public void LoadState(ElementHighlighterState state, FunctionIR function) {
    if (state == null) {
      return; // Most likely a file from an older version of the app.
    }

    groups_ = StateSerializer.LoadElementGroupState(state.Groups);
    Version++;
  }

  private void DrawGroup(HighlightedSegmentGroup group, TextView textView,
                         DrawingContext drawingContext, int viewStart, int viewEnd) {
    // Create BackgroundGeometryBuilder only if needed.
    BackgroundGeometryBuilder geoBuilder = null;

    var mergedRect = new Rect();
    int prevMergedLine = -1;
    int mergedLineCount = 0;
    double prevMergedY = -1;

    foreach (var segment in group.Segments.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
      if (geoBuilder == null) {
        geoBuilder = new BackgroundGeometryBuilder {
          BorderThickness = 0
        };
      }

      if (segment.Element is BlockIR) {
        geoBuilder.AddSegment(textView, segment);
      }
      else if (segment.Element is TupleIR) {
        int line = segment.Element.TextLocation.Line;

        // Extend width to cover entire line.
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
          double width = textView.ActualWidth + textView.HorizontalOffset;
          var actualRect = Utils.SnapRectToPixels(rect.X - 1, rect.Y, width, rect.Height);

          // When selecting multiple consecutive tuples, create a single
          // rect covering the region of each individual selection rect.
          // This is done to avoid horizontal lines showing sometimes otherwise.
          if (mergedLineCount > 0) {
            if (prevMergedLine == line - 1) {
              // Don't extend the accumulated height if the Y position
              // is unchanged, this happens with lines that are in a collapsed block folding.
              if (Math.Abs(actualRect.Y - prevMergedY) > double.Epsilon) {
                mergedRect = new Rect(mergedRect.Left, mergedRect.Top,
                                      Math.Max(mergedRect.Width, actualRect.Width),
                                      mergedRect.Height + actualRect.Height);
              }

              mergedLineCount++;
              prevMergedLine = line;
              prevMergedY = actualRect.Y;
              continue;
            }
            else {
              // Disjoint line, commit the current merged rect and start a new region.
              geoBuilder.AddRectangle(textView, mergedRect);
            }
          }

          mergedRect = actualRect;
          prevMergedLine = line;
          prevMergedY = actualRect.Y;
          mergedLineCount = 1;
        }
      }
      else {
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
          var actualRect = Utils.SnapRectToPixels(rect, -1, 0, 2, 1);
          geoBuilder.AddRectangle(textView, actualRect);
        }
      }
    }

    if (mergedLineCount > 0) {
      geoBuilder.AddRectangle(textView, mergedRect);
    }

    if (geoBuilder != null) {
      var geometry = geoBuilder.CreateGeometry();

      if (geometry != null) {
        drawingContext.DrawGeometry(group.BackColor, group.Border, geometry);
      }
    }
  }
}