// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Profile.Document;

namespace ProfileExplorer.UI.Document;

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
          segment.Element.TextLocation.Line < viewStartLine - 1) {
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

    if (settings != null) {
      if (settings.FilterSourceDefinitions ||
          settings.FilterDestinationUses) {
        filter = irInfo.CreateReferenceFilter(function);

        if (filter != null) {
          filter.FilterUses = settings.FilterDestinationUses;
          filter.FilterDefinitions = settings.FilterSourceDefinitions;
        }
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

  public static void RemoveNonDefaultMenuItems(MenuItem menu) {
    var items = SaveDefaultMenuItems(menu);
    menu.Items.Clear();
    RestoreDefaultMenuItems(menu, items);
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

  public static void CreateBackMenu(MenuItem menu, Stack<ProfileFunctionState> states,
                                    RoutedEventHandler menuClickHandler,
                                    TextViewSettingsBase settings, ISession session) {
    var defaultItems = SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();

    var valueTemplate = (DataTemplate)Application.Current.FindResource("ProfileMenuItemValueTemplate");
    var markerSettings = settings.ProfileMarkerSettings;
    var profile = session.ProfileData;
    int order = 0;
    double maxWidth = 0;

    foreach (var state in states) {
      double weightPercentage = profile.ScaleFunctionWeight(state.Weight);

      string title = state.Section.ParentFunction.Name.FormatFunctionName(session, 80);
      string text = $"({markerSettings.FormatWeightValue(state.Weight)})";

      var value = new ProfileMenuItem(text, state.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = "",
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush()
      };

      var item = new MenuItem {
        Header = value,
        IsCheckable = true,
        StaysOpenOnClick = true,
        Tag = state,
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += menuClickHandler;
      defaultItems.Add(item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    menu.Items.Clear();
    RestoreDefaultMenuItems(menu, defaultItems);
  }

  public static async Task<(int, int)>
    FindFunctionSourceLineRange(IRTextFunction function, IRDocument textView) {
    int lineCount = textView.Document.LineCount;
    var session = textView.Session;

    return await Task.Run(async () => {
      var debugInfo = await session.GetDebugInfoProvider(function).ConfigureAwait(false);
      var funcProfile = session.ProfileData?.GetFunctionProfile(function);

      if (debugInfo == null || funcProfile == null) {
        return (0, 0);
      }

      int firstSourceLineIndex = 0;
      int lastSourceLineIndex = 0;

      if (debugInfo.PopulateSourceLines(funcProfile.FunctionDebugInfo)) {
        firstSourceLineIndex = funcProfile.FunctionDebugInfo.FirstSourceLine.Line;
        lastSourceLineIndex = funcProfile.FunctionDebugInfo.LastSourceLine.Line;
      }

      // Ensure source lines are within document bounds
      // just in case values are not right.
      firstSourceLineIndex = Math.Clamp(firstSourceLineIndex, 1, lineCount);
      lastSourceLineIndex = Math.Clamp(lastSourceLineIndex, 1, lineCount);
      return (firstSourceLineIndex, lastSourceLineIndex);
    });
  }

  public static string FormatLongFunctionName(string name) {
    return FormatLongFunctionName(name, 80, 10, 1000);
  }

  public static string FormatLongFunctionName(string name, int maxLineLength,
                                              int maxSplitPointAdjustment,
                                              int maxLength) {
    if (name.Length <= maxLineLength) {
      return name;
    }

    // If name is really long cut out from the middle part
    // to make it fit into the maxLength.
    if (name.Length > maxLength) {
      int diff = name.Length - maxLength;
      int middle = name.Length / 2;
      name = name.Substring(0, middle - diff / 2) + " ... " +
             name.Substring(middle + diff / 2, middle - diff / 2);
    }

    // Try to split the name each maxLineLength letters.
    // If the split point happens to be inside a identifier,
    // look left or right for a template separator like < > : ,
    // to pick as a splitting point since it looks better than
    // cutting a class/function name.
    var sb = new StringBuilder();

    for (int i = 0; i < name.Length; i++) {
      int splitPoint = i + Math.Min(maxLineLength, name.Length - i);

      if (name.Length - splitPoint < maxSplitPointAdjustment) {
        // Split point is close to the name end, don't split anymore.
        return sb.ToString().Trim() + name.Substring(i, name.Length - i);
      }

      bool foundNew = false;

      // Look for a separator cahr on the right.
      for (int k = splitPoint + 1, distance = 0;
           k < name.Length && distance < maxSplitPointAdjustment;
           k++, distance++) {
        if (!char.IsLetterOrDigit(name[k])) {
          // Found a separator char as a splitting point.
          splitPoint = k;
          foundNew = true;
          break;
        }
      }

      if (!foundNew && splitPoint < name.Length - 1) {
        // Look for a separator char on the left.
        for (int k = splitPoint - 1, distance = 0;
             k > i && distance < maxSplitPointAdjustment;
             k--, distance++) {
          if (!char.IsLetterOrDigit(name[k])) {
            // Found a separator char as a splitting point.
            splitPoint = k;
            break;
          }
        }
      }

      int length = splitPoint - i;
      sb.AppendLine(name.Substring(i, length));
      i += length - 1;
    }

    return sb.ToString().Trim();
  }

  public static IRTextSection FindCallTargetSection(IRElement element, IRTextSection section, ISession session) {
    if (!element.HasName) {
      return null;
    }

    // Function names in the summary are mangled, while the document
    // has them demangled, run the demangler while searching for the target.
    var summary = section.ParentFunction.ParentSummary;
    string searchedName = element.Name;
    var targetFunc = summary.FindFunction(searchedName);

    if (targetFunc == null) {
      var nameProvider = session.CompilerInfo.NameProvider;
      targetFunc = summary.FindFunction(searchedName, nameProvider.FormatFunctionName);
    }

    if (targetFunc == null) {
      return null;
    }

    // Prefer the same section as this document if there are multiple.
    return targetFunc.SectionCount == 0 ? null : targetFunc.Sections[0];
  }
}