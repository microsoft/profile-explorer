// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerUI.Profile;
using IRExplorerUI.Profile.Document;

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

  public static void CreateInstancesMenu(MenuItem menu, IRTextSection section,
                                         FunctionProfileData funcProfile,
                                         RoutedEventHandler menuClickHandler,
                                         TextViewSettingsBase settings, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();

    var valueTemplate = (DataTemplate)Application.Current.FindResource("BlockPercentageValueTemplate");
    var markerSettings = settings.ProfileMarkerSettings;
    int order = 0;
    double maxWidth = 0;

    var nodes = session.ProfileData.CallTree.GetSortedCallTreeNodes(section.ParentFunction);
    int maxCallers = nodes.Count >= 2 ? CommonParentCallerIndex(nodes[0], nodes[1]) : Int32.MaxValue;

    foreach (var node in nodes) {
      double weightPercentage = funcProfile.ScaleWeight(node.Weight);

      if (!markerSettings.IsVisibleValue(order++, weightPercentage) ||
          !node.HasCallers) {
        break;
      }

      var (title, tooltip) = GenerateInstancePreviewText(node, session, maxCallers);
      string text = $"({markerSettings.FormatWeightValue(null, node.Weight)})";

      var value = new ProfileMenuItem(text, node.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
      };

      var item = new MenuItem {
        Header = value,
        IsCheckable = true,
        StaysOpenOnClick = true,
        Tag = node,
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += menuClickHandler;
      defaultItems.Add(item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      double width = Utils.MeasureString(title, settings.FontName, settings.FontSize).Width;
      maxWidth = Math.Max(width, maxWidth);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    menu.Items.Clear();
    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }

  public static void CreateThreadsMenu(MenuItem menu, IRTextSection section,
                                       FunctionProfileData funcProfile,
                                       RoutedEventHandler menuClickHandler,
                                       TextViewSettingsBase settings, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();

    var valueTemplate = (DataTemplate)Application.Current.FindResource("BlockPercentageValueTemplate");
    var markerSettings = settings.ProfileMarkerSettings;
    var timelineSettings = App.Settings.TimelineSettings;
    int order = 0;
    double maxWidth = 0;

    var node = session.ProfileData.CallTree.GetCombinedCallTreeNode(section.ParentFunction);

    foreach (var thread in node.SortedByWeightPerThreadWeights) {
      double weightPercentage = funcProfile.ScaleWeight(thread.Values.Weight);


      var threadInfo = session.ProfileData.FindThread(thread.ThreadId);
      var backColor = timelineSettings.GetThreadBackgroundColors(threadInfo, thread.ThreadId).Margin;

      string text = $"({markerSettings.FormatWeightValue(null, thread.Values.Weight)})";
      string tooltip = threadInfo is {HasName: true} ? threadInfo.Name : null;
      string title = !string.IsNullOrEmpty(tooltip) ? $"{thread.ThreadId} ({tooltip})" : $"{thread.ThreadId}";

      var value = new ProfileMenuItem(text, node.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
      };

      var item = new MenuItem {
        Header = value,
        IsCheckable = true,
        StaysOpenOnClick = true,
        Tag = thread.ThreadId,
        Background = backColor,
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += menuClickHandler;
      defaultItems.Add(item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      double width = Utils.MeasureString(title, settings.FontName, settings.FontSize).Width;
      maxWidth = Math.Max(width, maxWidth);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    menu.Items.Clear();
    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }

  public static void CreateInlineesMenu(MenuItem menu, IRTextSection section,
                                        List<InlineeListItem> inlineeList,
                                        FunctionProfileData funcProfile,
                                        RoutedEventHandler menuClickHandler,
                                        TextViewSettingsBase settings, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();
    var valueTemplate = (DataTemplate)Application.Current.FindResource("BlockPercentageValueTemplate");
    var markerSettings = settings.ProfileMarkerSettings;
    int order = 0;
    double maxWidth = 0;

    // Compute time spent in non-inlinee parts.
    TimeSpan inlineeWeightSum = TimeSpan.Zero;

    foreach (var node in inlineeList) {
      inlineeWeightSum += node.ExclusiveWeight;
    }

    var nonInlineeWeight = funcProfile.Weight - inlineeWeightSum;
    double nonInlineeWeightPercentage = funcProfile.ScaleWeight(nonInlineeWeight);
    string nonInlineeText = $"({markerSettings.FormatWeightValue(null, nonInlineeWeight)})";

    var nonInlineeValue = new ProfileMenuItem(nonInlineeText, nonInlineeWeight.Ticks, nonInlineeWeightPercentage) {
      PrefixText = "Non-Inlinee Code",
      ToolTip = "Time for code not originating from an inlined function",
      ShowPercentageBar = markerSettings.ShowPercentageBar(nonInlineeWeightPercentage),
      TextWeight = markerSettings.PickTextWeight(nonInlineeWeightPercentage),
      PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
    };
    profileItems.Add(nonInlineeValue);

    if (defaultItems.Count > 0 &&
      defaultItems[0] is MenuItem nonInlineeItem) {
      nonInlineeItem.Header = nonInlineeValue;
      nonInlineeItem.HeaderTemplate = valueTemplate;
    }

    foreach (var node in inlineeList) {
      double weightPercentage = funcProfile.ScaleWeight(node.ExclusiveWeight);

      if (!markerSettings.IsVisibleValue(order++, weightPercentage)) {
        break;
      }

      var title = node.InlineeFrame.Function.FormatFunctionName(session, 80);
      string text = $"({markerSettings.FormatWeightValue(null, node.ExclusiveWeight)})";
      string tooltip = $"File {Utils.TryGetFileName(node.InlineeFrame.FilePath)}:{node.InlineeFrame.Line}\n";
      tooltip += GenerateInlineeFunctionDescription(node, funcProfile, settings.ProfileMarkerSettings, session);

      var value = new ProfileMenuItem(text, node.ExclusiveWeight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
      };

      var item = new MenuItem {
        Header = value,
        Tag = node,
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += menuClickHandler;
      defaultItems.Add(item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      double width = Utils.MeasureString(title, settings.FontName, settings.FontSize).Width;
      maxWidth = Math.Max(width, maxWidth);
    }

    if (profileItems.Count == 0) {
      defaultItems.Add(new MenuItem() {
        Header = "No significant inlined functions",
        IsHitTestVisible = false,
        Tag = true, // Give it a tag so it can be removed later.
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      });
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    menu.Items.Clear();
    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }

  private static int CommonParentCallerIndex(ProfileCallTreeNode a, ProfileCallTreeNode b) {
    int index = 0;

    do {
      index++;
      a = a.Caller;
      b = b.Caller;
    } while (a != b && a != null && b != null);

    return index;
  }

  public static (string Short, string Long)
    GenerateInstancePreviewText(ProfileCallTreeNode node, ISession session, int maxCallers = int.MaxValue) {
    return GenerateInstancePreviewText(node, maxCallers, 50, 30, 1000, 50, session);
  }
  private static (string Short, string Long)
    GenerateInstancePreviewText(ProfileCallTreeNode node, int maxCallers,
                                int maxLength, int maxSingleLength,
                                int maxCompleteLength, int maxCompleteLineLength, ISession session) {
    const string Separator = " \ud83e\udc70 "; // Arrow character.
    var sb = new StringBuilder();
    var completeSb = new StringBuilder();
    var nameProvider = session.CompilerInfo.NameProvider;
    int remaining = maxLength;
    int completeRemaining = maxCompleteLength;
    int completeLineRemaining = maxCompleteLineLength;
    int index = 0;
    node = node.Caller;

    while (node != null) {
      // Build the shorter title stack trace.
      if (index < maxCallers && remaining > 0) {
        int maxNameLength = Math.Min(remaining, maxSingleLength);
        var name = node.FormatFunctionName(nameProvider.FormatFunctionName, maxNameLength);
        remaining -= name.Length;

        if (index == 0) {
          sb.Append(name);
        }
        else {
          sb.Append($"{Separator}{name}");
        }
      }

      // Build the longer tooltip stack trace.
      if (completeRemaining > 0) {
        int maxNameLength = Math.Min(completeRemaining, maxSingleLength);
        var name = node.FormatFunctionName(nameProvider.FormatFunctionName, maxNameLength);

        if (index == 0) {
          completeSb.Append(name);
        }
        else {
          completeSb.Append($"{Separator}{name}");
        }

        completeRemaining -= name.Length;
        completeLineRemaining -= name.Length;

        if(completeLineRemaining < 0) {
          completeSb.Append("\n");
          completeLineRemaining = maxCompleteLineLength;
        }
      }

      node = node.Caller;
      index++;
    }

    return (sb.ToString(), completeSb.ToString());
  }

  public static void HandleInstanceMenuItemChanged(MenuItem menuItem, MenuItem menu,
                                                   ProfileSampleFilter instanceFilter) {
    if (menuItem.Tag is ProfileCallTreeNode node) {
      instanceFilter ??= new ProfileSampleFilter();

      if (menuItem.IsChecked) {
        instanceFilter.AddInstance(node);
      }
      else {
        instanceFilter.RemoveInstance(node);
      }
    }
    else {
      instanceFilter.ClearInstances();
      UncheckMenuItems(menu, menuItem);
    }
  }

  public static void HandleThreadMenuItemChanged(MenuItem menuItem, MenuItem menu,
                                                 ProfileSampleFilter instanceFilter) {
    if (menuItem.Tag is int threadId) {
      instanceFilter ??= new ProfileSampleFilter();

      if (menuItem.IsChecked) {
        instanceFilter.AddThread(threadId);
      }
      else {
        instanceFilter.RemoveThread(threadId);
      }
    }
    else {
      instanceFilter.ClearThreads();
      UncheckMenuItems(menu, menuItem);
    }
  }

  private static void UncheckMenuItems(MenuItem menu, MenuItem excludedItem) {
    foreach (var item in menu.Items) {
      if (item is MenuItem menuItem && menuItem != excludedItem) {
        menuItem.IsChecked = false;
      }
    }
  }

  public static void SyncThreadsMenuWithFilter(MenuItem menu, ProfileSampleFilter instanceFilter) {
    foreach (var item in menu.Items) {
      if (item is MenuItem menuItem && menuItem.Tag is int threadId) {
        menuItem.IsChecked = instanceFilter != null && instanceFilter.IncludesThread(threadId);
      }
    }
  }

  public static async Task<(int, int)> FindFunctionSourceLineRange(IRTextFunction function, ISession session) {
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

    return (firstSourceLineIndex, lastSourceLineIndex);
  }

  public static string GenerateProfileFilterTitle(ProfileSampleFilter instanceFilter, ISession session) {
    if (instanceFilter == null) {
      return "";
    }

    return !instanceFilter.IncludesAll ? "Instance: " : "";
  }

  public static string GenerateProfileFilterDescription(ProfileSampleFilter instanceFilter, ISession session) {
    if (instanceFilter == null) {
      return "";
    }

    var sb = new StringBuilder("\n");

    if (instanceFilter.HasInstanceFilter) {
      sb.AppendLine("\nInstances included:");

      foreach (var node in instanceFilter.FunctionInstances) {
        sb.AppendLine($" - {DocumentUtils.GenerateInstancePreviewText(node, session).Short}");
      }
    }

    if (instanceFilter.HasThreadFilter) {
      sb.AppendLine("\nThreads included:");

      foreach (var threadId in instanceFilter.ThreadIds) {
        var threadInfo = session.ProfileData.FindThread(threadId);
        string threadName = threadInfo is {HasName: true} ? threadInfo.Name : null;

        if (!string.IsNullOrEmpty(threadName)) {
          sb.AppendLine($" - {threadId} ({threadName})");
        }
        else {
          sb.AppendLine($" - {threadId}");
        }
      }
    }

    return sb.ToString();
  }

  public static string GenerateProfileFunctionDescription(FunctionProfileData funcProfile,
                                                          ProfileDocumentMarkerSettings settings,ISession session) {
    return GenerateProfileDescription(funcProfile.Weight, funcProfile.ExclusiveWeight,
                                      settings, session.ProfileData.ScaleFunctionWeight);
  }

  public static string GenerateInlineeFunctionDescription(InlineeListItem inlinee,
                                                          FunctionProfileData funcProfile,
                                                          ProfileDocumentMarkerSettings settings,ISession session) {
    return GenerateProfileDescription(inlinee.Weight, inlinee.ExclusiveWeight,
                                      settings, funcProfile.ScaleWeight);
  }

  public static string GenerateProfileDescription(TimeSpan weight, TimeSpan exclusiveWeight,
                                                  ProfileDocumentMarkerSettings settings,
                                                  Func<TimeSpan, double> weightFunc) {
    var weightPerc = weightFunc(weight);
    var exclusiveWeightPerc = weightFunc(exclusiveWeight);
    var weightText = $"{weightPerc.AsPercentageString()} ({settings.FormatWeightValue(null, weight)})";
    var exclusiveWeightText = $"{exclusiveWeightPerc.AsPercentageString()} ({settings.FormatWeightValue(null, exclusiveWeight)})";
    return $"Total time: {weightText}\nSelf time: {exclusiveWeightText}";
  }

  public static void SyncInstancesMenuWithFilter(MenuItem menu, ProfileSampleFilter instanceFilter) {
    foreach (var item in menu.Items) {
      if(item is MenuItem menuItem  && menuItem.Tag is ProfileCallTreeNode node) {
        menuItem.IsChecked =  instanceFilter != null && instanceFilter.IncludesInstance(node);
      }
    }
  }
}