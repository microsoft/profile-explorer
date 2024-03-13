// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerUI.Document;

public static class DocumentUtils {
  public static IRElement FindElement(int offset, List<IRElement> list) {
    if (list == null) {
      return null;
    }

    //? TODO: Use binary search
    foreach (var token in list) {
      if (offset >= token.TextLocation.Offset &&
          offset < token.TextLocation.Offset + token.TextLength) {
        return token;
      }
    }

    return null;
  }

  public static bool FindElement(int offset, List<IRElement> list, out IRElement result) {
    result = FindElement(offset, list);
    return result != null;
  }

  public static IRElement FindPointedElement(Point position, TextEditor editor, List<IRElement> list) {
    int offset = GetOffsetFromMousePosition(position, editor, out _);
    return offset != -1 ? FindElement(offset, list) : null;
  }

  public static int GetOffsetFromMousePosition(Point positionRelativeToTextView, TextEditor editor,
                                               out int visualColumn) {
    visualColumn = 0;
    var textView = editor.TextArea.TextView;
    var pos = positionRelativeToTextView;

    if (pos.Y < 0) {
      pos.Y = 0;
    }

    if (pos.Y > textView.ActualHeight) {
      pos.Y = textView.ActualHeight;
    }

    pos += textView.ScrollOffset;

    if (pos.Y >= textView.DocumentHeight) {
      pos.Y = textView.DocumentHeight - 0.01;
    }

    var line = textView.GetVisualLineFromVisualTop(pos.Y);

    if (line != null) {
      visualColumn = line.GetVisualColumn(pos, false);
      return line.GetRelativeOffset(visualColumn) + line.FirstDocumentLine.Offset;
    }

    return -1;
  }

  public static FormattedText CreateFormattedText(FrameworkElement element, string text, Typeface typeface,
                                                  double emSize, Brush foreground, FontWeight? fontWeight = null) {
    var formattedText = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                          typeface, emSize, foreground, null,
                                          TextOptions.GetTextFormattingMode(element),
                                          VisualTreeHelper.GetDpi(element).PixelsPerDip);

    if (fontWeight.HasValue) {
      formattedText.SetFontWeight(fontWeight.Value);
    }

    return formattedText;
  }

  public static IEnumerable<T> FindOverlappingSegments<T>(this TextSegmentCollection<T> list, TextView textView)
    where T : IRSegment {
    if (!FindVisibleTextLineAndOffsets(textView, out int viewStart, out int viewEnd,
                                       out int viewStartLine, out int viewEndLine)) {
      yield break;
    }

    foreach (var segment in list.FindOverlappingSegments(viewStart, viewEnd - viewStart)) {
      // Blocks can start on a line that is out of view and the overlay
      // is meant to be associated with the start line, while GetRectsForSegment
      // would use the first line still in view, so skip manually over it.
      if (segment.Element is BlockIR &&
          segment.Element.TextLocation.Line < viewStartLine) {
        continue;
      }

      yield return segment;
    }
  }

  public static bool FindVisibleTextOffsets(TextView textView, out int viewStart, out int viewEnd) {
    textView.EnsureVisualLines();
    var visualLines = textView.VisualLines;

    if (visualLines.Count == 0) {
      viewStart = viewEnd = 0;
      return false;
    }

    viewStart = visualLines[0].FirstDocumentLine.Offset;
    viewEnd = visualLines[^1].LastDocumentLine.EndOffset;
    return true;
  }

  public static bool FindVisibleTextLineAndOffsets(TextView textView, out int viewStart, out int viewEnd,
                                                   out int viewStartLine, out int viewEndLine) {
    textView.EnsureVisualLines();
    var visualLines = textView.VisualLines;

    if (visualLines.Count == 0) {
      viewStart = viewEnd = 0;
      viewStartLine = viewEndLine = 0;
      return false;
    }

    viewStartLine = visualLines[0].FirstDocumentLine.LineNumber;
    viewEndLine = visualLines[^1].LastDocumentLine.LineNumber;
    viewStart = visualLines[0].FirstDocumentLine.Offset;
    viewEnd = visualLines[^1].LastDocumentLine.EndOffset;
    return true;
  }

  public static ReferenceFinder CreateReferenceFinder(FunctionIR function, ISession session,
                                                      DocumentSettings settings) {
    var irInfo = session.CompilerInfo.IR;
    IReachableReferenceFilter filter = null;

    if (settings.FilterSourceDefinitions ||
        settings.FilterDestinationUses) {
      filter = irInfo.CreateReferenceFilter(function);

      if (filter != null) {
        filter.FilterUses = settings.FilterDestinationUses;
        filter.FilterDefinitions = settings.FilterSourceDefinitions;
      }
    }

    return new ReferenceFinder(function, irInfo, filter);
  }

  public static List<object> SaveDefaultMenuItems(MenuItem menu) {
    // Save the menu items that are always present, they are either
    // separators or menu items without an object tag.
    var defaultItems = new List<object>();

    foreach (object item in menu.Items) {
      if (item is MenuItem menuItem) {
        if (menuItem.Tag == null) {
          defaultItems.Add(item);
        }
      }
      else if (item is Separator) {
        defaultItems.Add(item);
      }
    }

    return defaultItems;
  }

  public static void RestoreDefaultMenuItems(MenuItem menu, List<object> defaultItems) {
    defaultItems.ForEach(item => menu.Items.Add(item));
  }

  public static string GenerateElementPreviewText(IRElement element, ReadOnlyMemory<char> documentText,
    int maxLength = 0) {
    var instr = element.ParentInstruction;
    string text = "";

    if (instr != null) {
      text = instr.GetText(documentText).ToString();
    }
    else {
      if (element is OperandIR op) {
        // This is usually a parameter.
        text = op.GetText(documentText).ToString();
      }
      else {
        return "";
      }
    }

    int start = 0;
    int length = text.Length;

    if (instr != null) {
      // Set range start to cover destination.
      if (instr.Destinations.Count > 0) {
        var firstDest = instr.Destinations[0];
        start = firstDest.TextLocation.Offset - instr.TextLocation.Offset;
        start = Math.Min(instr.OpcodeLocation.Offset - instr.TextLocation.Offset, start); // Include opcode.

      }
      else {
        start = instr.OpcodeLocation.Offset - instr.TextLocation.Offset; // Include opcode.
      }
    }

    start = Math.Max(0, start); //? TODO: Workaround for offset not being right

    // Extend range to cover all sources.
    if (instr != null && instr.Sources.Count > 0) {
      var lastSource = instr.Sources.FindLast(s => s.TextLocation.Offset != 0);

      if (lastSource != null) {
        length = lastSource.TextLocation.Offset -
                 instr.TextLocation.Offset +
                 lastSource.TextLength;

        if (length <= 0) {
          length = text.Length;
        }

        length = Math.Min(text.Length, length); //? TODO: Workaround for offset not being right
      }
    }

    // Extract the text in the range.
    if (start != 0 || length > 0) {
      int actualLength = Math.Min(length - start, text.Length - start);

      if (actualLength > 0) {
        text = text.Substring(start, actualLength);
      }
    }

    text = text.RemoveNewLines();
    return maxLength != 0 ? text.TrimToLength(maxLength) : text;
  }

  public static IRElement FindTupleOnSourceLine(int line, IRDocument textView) {
    var pair1 = textView.ProfileProcessingResult.SampledElements.
      Find(e => e.Item1.TextLocation.Line == line - 1);

    if (pair1.Item1 != null) {
      return pair1.Item1;
    }

    // Look into performance counters.
    var pair2 = textView.ProfileProcessingResult.CounterElements.
      Find(e => e.Item1.TextLocation.Line == line - 1);
    return pair2.Item1;
  }
}