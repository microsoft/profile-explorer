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
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerUI.Profile;
using IRExplorerUI.Profile.Document;
using Dia2Lib;

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
    var defaultItems = SaveDefaultMenuItems(menu);
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
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    menu.Items.Clear();
    RestoreDefaultMenuItems(menu, defaultItems);
  }

  public static void CreateBackMenu(MenuItem menu, Stack<ProfileFunctionState> states,
                                         RoutedEventHandler menuClickHandler,
                                         TextViewSettingsBase settings, ISession session) {
    var defaultItems = SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();

    var valueTemplate = (DataTemplate)Application.Current.FindResource("BlockPercentageValueTemplate");
    var markerSettings = settings.ProfileMarkerSettings;
    var profile = session.ProfileData;
    int order = 0;
    double maxWidth = 0;


    foreach (var state in states) {
      double weightPercentage = profile.ScaleFunctionWeight(state.Weight);

      var title = state.Section.ParentFunction.Name.FormatFunctionName(session, 80);
      string text = $"({markerSettings.FormatWeightValue(null, state.Weight)})";

      var value = new ProfileMenuItem(text, state.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = "",
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
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

  public static void CreateThreadsMenu(MenuItem menu, IRTextSection section,
                                       FunctionProfileData funcProfile,
                                       RoutedEventHandler menuClickHandler,
                                       TextViewSettingsBase settings, ISession session) {
    var defaultItems = SaveDefaultMenuItems(menu);
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
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    menu.Items.Clear();
    RestoreDefaultMenuItems(menu, defaultItems);
  }

  public static void CreateInlineesMenu(MenuItem menu, IRTextSection section,
                                        List<InlineeListItem> inlineeList,
                                        FunctionProfileData funcProfile,
                                        RoutedEventHandler menuClickHandler,
                                        TextViewSettingsBase settings, ISession session) {
    var defaultItems = SaveDefaultMenuItems(menu);
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
      tooltip += CreateInlineeFunctionDescription(node, funcProfile, settings.ProfileMarkerSettings, session);

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
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    // If no items were added (besides "Non-Inlinee Code"),
    // add an entry about there being no significant inlinees.
    if (profileItems.Count == 1) {
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
    RestoreDefaultMenuItems(menu, defaultItems);
  }
  
  public static void CreateMarkedModulesMenu(MenuItem menu,
                                       RoutedEventHandler menuClickHandler,
                                       FunctionMarkingSettings settings, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();
    var separatorIndex = defaultItems.FindIndex(item => item is Separator);
    var markerSettings = App.Settings.DocumentSettings.ProfileMarkerSettings;
    var valueTemplate = (DataTemplate)Application.Current.FindResource("BlockPercentageValueTemplate");
    double maxWidth = 0;

    // Sort modules by weight in decreasing order.
    var sortedModules = new List<(FunctionMarkingStyle Module, TimeSpan Weight)>();

    foreach (var moduleStyle in settings.ModuleColors) {
      var moduleWeight = session.ProfileData.FindModulesWeight(name =>
        moduleStyle.NameMatches(name));
      sortedModules.Add((moduleStyle, moduleWeight));
    }

    sortedModules.Sort((a, b) => a.Weight.CompareTo(b.Weight));

    // Insert module markers after separator.
    foreach (var pair in sortedModules) {
      double weightPercentage = session.ProfileData.ScaleModuleWeight(pair.Weight);
      string text = $"({markerSettings.FormatWeightValue(null, pair.Weight)})";
      string tooltip = "Click to remove module marking";
      string title = pair.Module.Name;

      if (pair.Module.IsRegex) {
        title += " (Regex)";
      }

      var value = new ProfileMenuItem(text, pair.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
      };

      var item = new MenuItem {
        Header = value,
        Tag = pair.Module,
        Icon = CreateMarkedMenuIcon(pair.Module),
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += menuClickHandler;
      defaultItems.Insert(separatorIndex + 1, item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    // Populate the module menu.
    menu.Items.Clear();
    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }

  
  public static void CreateMarkedFunctionsMenu(MenuItem menu,
                                             RoutedEventHandler menuClickHandler,
                                             FunctionMarkingSettings settings, ISession session) {
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    var profileItems = new List<ProfileMenuItem>();
    var separatorIndex = defaultItems.FindIndex(item => item is Separator);
    var nameProvider = session.CompilerInfo.NameProvider;
    var markerSettings = App.Settings.DocumentSettings.ProfileMarkerSettings;
    var valueTemplate = (DataTemplate)Application.Current.FindResource("BlockPercentageValueTemplate");
    double maxWidth = 0;
    
    // Sort functions by weight in decreasing order.
    var sortedFuncts = new List<(FunctionMarkingStyle Function, TimeSpan Weight)>();

    foreach (var funcStyle in settings.FunctionColors) {
      // Find all functions matching the marked name. There can be multiple
      // since the same func. name may be used in multiple modules,
      // and also because the name matching may use Regex.
      var weight = TimeSpan.Zero;

      foreach (var loadedDoc in session.SessionState.Documents) {
        if (loadedDoc.Summary == null) {
          continue;
        }

        var funcList = loadedDoc.Summary.FindFunctions(name =>
          funcStyle.NameMatches(nameProvider.FormatFunctionName(name)));

        foreach (var func in funcList) {
          var funcProfile = session.ProfileData.GetFunctionProfile(func);

          if (funcProfile != null) {
            weight += funcProfile.Weight;
          }
        }
      }

      sortedFuncts.Add((funcStyle, weight));
    }

    sortedFuncts.Sort((a, b) => a.Weight.CompareTo(b.Weight));

    foreach (var pair in sortedFuncts) {
      double weightPercentage = session.ProfileData.ScaleFunctionWeight(pair.Weight);
      string text = $"({markerSettings.FormatWeightValue(null, pair.Weight)})";
      string tooltip = "Click to remove function marking";
      string title = pair.Function.Name.TrimToLength(80);

      if (pair.Function.IsRegex) {
        title += " (Regex)";
      }

      var value = new ProfileMenuItem(text, pair.Weight.Ticks, weightPercentage) {
        PrefixText = title,
        ToolTip = tooltip,
        ShowPercentageBar = markerSettings.ShowPercentageBar(weightPercentage),
        TextWeight = markerSettings.PickTextWeight(weightPercentage),
        PercentageBarBackColor = markerSettings.PercentageBarBackColor.AsBrush(),
      };

      var item = new MenuItem {
        Header = value,
        Tag = pair.Function,
        Icon = CreateMarkedMenuIcon(pair.Function),
        HeaderTemplate = valueTemplate,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Click += menuClickHandler;
      defaultItems.Insert(separatorIndex + 1, item);
      profileItems.Add(value);

      // Make sure percentage rects are aligned.
      Utils.UpdateMaxMenuItemWidth(title, ref maxWidth, menu);
    }

    foreach (var value in profileItems) {
      value.MinTextWidth = maxWidth;
    }

    // Populate the menu.
    menu.Items.Clear();
    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }
  
  private static Image CreateMarkedMenuIcon(FunctionMarkingStyle nodeMarkingStyle) {
    // Make a small square image with the marking background color.
    var visual = new DrawingVisual();

    using (var dc = visual.RenderOpen()) {
      dc.DrawRectangle(nodeMarkingStyle.Color.AsBrush(), ColorPens.GetPen(Colors.Black), new Rect(0, 0, 16, 16));
    }

    var targetBitmap = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Default);
    targetBitmap.Render(visual);
    return new Image { Source = targetBitmap };
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

    return (sb.ToString().Trim(), completeSb.ToString().Trim());
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

  public static string CreateProfileFilterTitle(ProfileSampleFilter instanceFilter, ISession session) {
    if (instanceFilter == null) {
      return "";
    }

    return !instanceFilter.IncludesAll ? "Instance: " : "";
  }

  public static string CreateProfileFilterDescription(ProfileSampleFilter instanceFilter, ISession session) {
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

    return sb.ToString().Trim();
  }

  public static string CreateProfileFunctionDescription(FunctionProfileData funcProfile,
                                                          ProfileDocumentMarkerSettings settings,ISession session) {
    return CreateProfileDescription(funcProfile.Weight, funcProfile.ExclusiveWeight,
                                      settings, session.ProfileData.ScaleFunctionWeight);
  }

  public static string CreateInlineeFunctionDescription(InlineeListItem inlinee,
                                                          FunctionProfileData funcProfile,
                                                          ProfileDocumentMarkerSettings settings,ISession session) {
    return CreateProfileDescription(inlinee.Weight, inlinee.ExclusiveWeight,
                                      settings, funcProfile.ScaleWeight);
  }

  public static string CreateProfileDescription(TimeSpan weight, TimeSpan exclusiveWeight,
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

    for (var i = 0; i < name.Length; i++) {
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
}