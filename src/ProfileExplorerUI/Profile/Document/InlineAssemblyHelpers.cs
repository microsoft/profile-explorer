// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using ProfileExplorer.UI.Document;

namespace ProfileExplorer.UI.Profile.Document;

// A custom line margin that that overrides the default one, with support
// for block folding - the lines part of a block folding are not numbered.
sealed class SourceLineNumberMargin : LineNumberMargin {
  private readonly IRDocument textView_;
  private SourceLineProfileResult sourceLineProfileResult_;

  static SourceLineNumberMargin() {
    DefaultStyleKeyProperty.OverrideMetadata(typeof(SourceLineNumberMargin),
                                             new FrameworkPropertyMetadata(typeof(SourceLineNumberMargin)));
  }

  public SourceLineNumberMargin(IRDocument textView, SourceLineProfileResult sourceLineProfileResult) {
    textView_ = textView;
    sourceLineProfileResult_ = sourceLineProfileResult;
  }

  protected override void OnRender(DrawingContext drawingContext) {
    var textView = TextView;
    var renderSize = RenderSize;

    if (textView == null || !textView.VisualLinesValid) {
      return;
    }

    var foreground = textView_.LineNumbersForeground;

    foreach (var line in textView.VisualLines) {
      int lineNumber = line.FirstDocumentLine.LineNumber;

      if (sourceLineProfileResult_ != null) {
        if (lineNumber < sourceLineProfileResult_.SourceLineResult.FirstLineIndex) {
          // Line numbers before function start are unchanged.
        }
        else if (lineNumber > sourceLineProfileResult_.SourceLineResult.LastLineIndex +
          sourceLineProfileResult_.AssemblyLineCount) {
          // For lines after function end, subtract the assembly line count.
          lineNumber -= sourceLineProfileResult_.AssemblyLineCount;
        }
        else if (sourceLineProfileResult_.LineToOriginalLineMap.TryGetValue(lineNumber, out int mappedLine)) {
          lineNumber = mappedLine;
        }
        else {
          // Don't show line numbers for inline assembly.
          continue;
        }
      }

      var text = DocumentUtils.CreateFormattedText(this, lineNumber.ToString(),
                                                   typeface, emSize, foreground);
      double y = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop);
      drawingContext.DrawText(text, new Point(renderSize.Width - text.Width, y - textView.VerticalOffset));
    }
  }
}

// Creates block foldings for the specified ranges, used to create
// the foldings for inline assembly sections in the source code document.
sealed class RangeFoldingStrategy : IBlockFoldingStrategy {
  private List<(int StartOffset, int EndOffset)> ranges_;
  private bool defaultClosed_;

  public RangeFoldingStrategy(List<(int StartOffset, int EndOffset)> ranges, bool defaultClosed = false) {
    ranges_ = ranges;
    defaultClosed_ = defaultClosed;
  }

  public void UpdateFoldings(FoldingManager manager, TextDocument document) {
    var newFoldings = CreateNewFoldings(document);
    manager.UpdateFoldings(newFoldings, -1);
  }

  private IEnumerable<NewFolding> CreateNewFoldings(TextDocument document) {
    foreach (var range in ranges_) {
      yield return new NewFolding(range.StartOffset, range.EndOffset) {
        DefaultClosed = defaultClosed_
      };
    }
  }
}

sealed class RangeColorizer : DocumentColorizingTransformer {
  private List<(int StartOffset, int EndOffset)> ranges_;
  private Brush textColor_;
  private Brush backColor_;
  private Typeface typeface_;
  private CompareRanges comparer_;

  public RangeColorizer(List<(int StartOffset, int EndOffset)> ranges,
                        Brush textColor, Brush backColor = null, Typeface typeface = null) {
    ranges_ = ranges;
    textColor_ = textColor;
    backColor_ = backColor;
    typeface_ = typeface;
    comparer_ = new CompareRanges();
    ranges.Sort((a, b) => a.CompareTo(b));
  }

  protected override void ColorizeLine(DocumentLine line) {
    if (line.Length == 0) {
      return;
    }

    var query = (line.Offset, line.EndOffset);
    int result = ranges_.BinarySearch(query, comparer_);

    if (result >= 0) {
      var range = ranges_[result];

      if (line.Offset < range.StartOffset ||
          line.Offset > range.EndOffset) {
        return;
      }

      int start = line.Offset > range.StartOffset ? line.Offset : range.StartOffset;
      int end = range.EndOffset > line.EndOffset ? line.EndOffset : range.EndOffset;
      ChangeLinePart(start, end, element => {
        if (textColor_ != null) {
          element.TextRunProperties.SetForegroundBrush(textColor_);
        }

        if (backColor_ != null) {
          element.TextRunProperties.SetBackgroundBrush(backColor_);
        }

        if (typeface_ != null) {
          element.TextRunProperties.SetTypeface(typeface_);
        }
      });
    }
  }

  private class CompareRanges : IComparer<(int StartOffset, int EndOffset)> {
    public int Compare((int StartOffset, int EndOffset) x, (int StartOffset, int EndOffset) y) {
      if (x.EndOffset < y.StartOffset) {
        return -1;
      }
      else if (x.StartOffset > y.EndOffset) {
        return 1;
      }

      return 0;
    }
  }
}