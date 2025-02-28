﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.IR.Tags;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.UI.Document;
using TextLocation = ProfileExplorer.Core.TextLocation;

namespace ProfileExplorer.UI.Profile;

//? TODO: Use better names for members
public record SourceLineProfileResult(
  FunctionProcessingResult Result,
  SourceLineProcessingResult SourceLineResult,
  FunctionIR Function,
  Dictionary<int, IRElement> LineToElementMap,
  Dictionary<int, int> LineToOriginalLineMap,
  Dictionary<int, int> OriginalLineToLineMap,
  List<(int StartOffset, int EndOffset)> AssemblyRanges,
  int AssemblyLineCount);

public class InlineeListItem {
  public InlineeListItem(SourceStackFrame frame) {
    InlineeFrame = frame;
    ElementWeights = new List<(IRElement Element, TimeSpan Weight)>();
  }

  public SourceStackFrame InlineeFrame { get; set; }
  public ProfileCallTreeNode CallTreeNode { get; set; }
  public TimeSpan Weight { get; set; }
  public TimeSpan ExclusiveWeight { get; set; }
  public double Percentage { get; set; }
  public double ExclusivePercentage { get; set; }
  public List<(IRElement Element, TimeSpan Weight)> ElementWeights { get; }

  public List<IRElement> SortedElements {
    get {
      ElementWeights.Sort((a, b) => b.Weight.CompareTo(a.Weight));
      return ElementWeights.ConvertAll(item => item.Element);
    }
  }
}

public class ProfileDocumentMarker {
  private static readonly string ProfileOverlayTag = "ProfileTag";

  // Templates for the time columns defining the style.
  public static readonly OptionalColumn TimeColumnDefinition =
    OptionalColumn.Template("[TimeHeader]", "TimePercentageColumnValueTemplate",
                            "TimeHeader", "Time (ms)", "Instruction time",
                            null, 50.0, "TimeColumnHeaderTemplate");
  public static readonly OptionalColumn TimePercentageColumnDefinition =
    OptionalColumn.Template("[TimePercentageHeader]", "TimePercentageColumnValueTemplate",
                            "TimePercentageHeader", "Time (%)", "Instruction time percentage relative to function time",
                            null, 50.0, "TimeColumnHeaderTemplate");

  //? TODO: Should be customizable (at least JSON if not UI)
  //? TODO: Each column setting should have the abbreviation
  private static readonly (string, string)[] PerfCounterNameReplacements = {
    ("Instruction", "Instr"),
    ("Misprediction", "Mispred")
  };
  private FunctionProfileData profile_;
  private ProfileData globalProfile_;
  private ProfileDocumentMarkerSettings settings_;
  private OptionalColumnSettings columnSettings_;
  private ICompilerInfoProvider irInfo_;

  public ProfileDocumentMarker(FunctionProfileData profile, ProfileData globalProfile,
                               ProfileDocumentMarkerSettings settings,
                               OptionalColumnSettings columnSettings,
                               ICompilerInfoProvider ir) {
    profile_ = profile;
    globalProfile_ = globalProfile;
    settings_ = settings;
    columnSettings_ = columnSettings;
    irInfo_ = ir;
  }

  public OptionalColumn TimeColumnTemplate() {
    string timeUnit = settings_.ValueUnit switch {
      ProfileDocumentMarkerSettings.ValueUnitKind.Second      => "sec",
      ProfileDocumentMarkerSettings.ValueUnitKind.Millisecond => "ms",
      ProfileDocumentMarkerSettings.ValueUnitKind.Microsecond => "µs",
      ProfileDocumentMarkerSettings.ValueUnitKind.Nanosecond  => "ns",
      _                                                       => throw new ArgumentOutOfRangeException()
    };

    TimeColumnDefinition.Title = $"Time ({timeUnit})";
    TimeColumnDefinition.Style = columnSettings_.GetColumnStyle(TimeColumnDefinition) ??
                                 OptionalColumnSettings.DefaultTimeColumnStyle;
    return TimeColumnDefinition;
  }

  public OptionalColumn TimePercentageColumnTemplate() {
    TimePercentageColumnDefinition.Style = columnSettings_.GetColumnStyle(TimePercentageColumnDefinition) ??
                                           OptionalColumnSettings.DefaultTimePercentageColumnStyle;
    return TimePercentageColumnDefinition;
  }

  public OptionalColumn CounterColumnTemplate(PerformanceCounter counter, int index) {
    var column = OptionalColumn.Template($"[CounterHeader{counter.Id}]",
                                         "TimePercentageColumnValueTemplate",
                                         $"CounterHeader{counter.Id}",
                                         $"{ShortenPerfCounterName(counter.Name)}",
                                         /*counterInfo?.Config?.Description != null ? $"{counterInfo.Config.Description}" :*/
                                         $"{counter.Name}",
                                         null, 50, "TimeColumnHeaderTemplate");
    column.Style = columnSettings_.GetColumnStyle(column);

    if (column.Style == null) {
      column.Style = counter.IsMetric ?
        OptionalColumnSettings.DefaultMetricsColumnStyle(index) :
        OptionalColumnSettings.DefaultCounterColumnStyle(index);
    }

    return column;
  }

  public static bool IsPerfCounterVisible(PerformanceCounter counter) {
    //? TODO: Use a filter list from options
    return counter.Name != "Timer";
  }

  public static string FormatPerformanceMetric(double value, PerformanceMetric metric) {
    if (value == 0) {
      return "";
    }

    return metric.Config.IsPercentage ? value.AsPercentageString() : $"{value:F2}";
  }

  public static string FormatPerformanceCounter(long value, PerformanceCounter counter) {
    if (counter.Frequency >= 1000) {
      double valueK = (double)(value * counter.Frequency) / 1000;
      return $"{valueK:n0} K";
    }

    return $"{value * counter.Frequency}";
  }

  public static string ShortenPerfCounterName(string name) {
    foreach (var replacement in PerfCounterNameReplacements) {
      int index = name.LastIndexOf(replacement.Item1, StringComparison.Ordinal);

      if (index != -1) {
        string suffix = "";

        if (index + replacement.Item1.Length < name.Length) {
          suffix = name.Substring(index + replacement.Item1.Length);
        }

        return name.Substring(0, index) + replacement.Item2 + suffix;
      }
    }

    return name;
  }

  public async Task<IRDocumentColumnData> Mark(IRDocument document, FunctionIR function,
                                               IRTextFunction textFunction) {
    IRDocumentColumnData columnData = null;
    var metadataTag = function.GetTag<AssemblyMetadataTag>();
    bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

    if (hasInstrOffsetMetadata) {
      document.SuspendUpdate();

      var result = await Task.Run(() => profile_.Process(function, irInfo_.IR));
      columnData = await MarkProfiledElements(result, function, document);
      document.ProfileProcessingResult = result;
      document.ProfileColumnData = columnData;

      // Remove any overlays from a previous marking.
      foreach (var block in function.Blocks) {
        document.RemoveElementOverlays(block, ProfileOverlayTag);
      }

      foreach (var tuple in function.AllTuples) {
        document.RemoveElementOverlays(tuple, ProfileOverlayTag);
      }

      MarkProfiledBlocks(result.BlockSampledElements, document, false);
      MarkCallSites(document, function, textFunction, metadataTag, null, false);
      document.ResumeUpdate();
    }

    return columnData;
  }

  public async Task<IRDocumentColumnData> MarkSourceLines(IRDocument document,
                                                          SourceLineProfileResult processingResult) {
    document.ProfileColumnData =
      await MarkProfiledElements(processingResult.Result, processingResult.Function, document);
    return document.ProfileColumnData;
  }

  public async Task<SourceLineProfileResult>
    PrepareSourceLineProfile(FunctionProfileData profile, IRDocument document,
                             SourceLineProcessingResult sourceProcResult,
                             ParsedIRTextSection parsedSection = null) {
    var sourceLineWeights = sourceProcResult.SourceLineWeightList;

    if (sourceLineWeights.Count == 0) {
      return null;
    }

    // Check for cases where instead of the source code smth. like
    // a source server authentication failure response is displayed.
    if (sourceProcResult.FirstLineIndex > document.LineCount &&
        sourceProcResult.LastLineIndex > document.LineCount) {
      return null;
    }

    //? TODO: Pretty hacky approach that makes a fake function
    //? with IR elements to represent each source line.
    var ids = IRElementId.NewFunctionId();
    var dummyFunc = new FunctionIR();
    var dummyBlock = new BlockIR(ids.NewBlock(0), 0, dummyFunc);
    dummyFunc.Blocks.Add(dummyBlock);
    dummyFunc.AssignBlockIndices();

    var processingResult = new FunctionProcessingResult();
    var lineToElementMap = new Dictionary<int, IRElement>();
    var lineToOriginalLineMap = new Dictionary<int, int>();
    var originalLineToLineMap = new Dictionary<int, int>();
    var assemblyRanges = new List<(int StartOffset, int EndOffset)>();

    TupleIR MakeDummyTuple(TextLocation textLocation, DocumentLine documentLine) {
      var tupleIr = new TupleIR(ids.NextTuple(), TupleKind.Other, dummyBlock);
      tupleIr.TextLocation = textLocation;
      tupleIr.TextLength = documentLine.Length;
      dummyBlock.Tuples.Add(tupleIr);
      return tupleIr;
    }

    // If assembly should be inserted after each source line,
    // precompute the list of instructions mapping to each line.
    FunctionProcessingResult.SampledElementsToLineMapping instrToLineMap = null;

    if (parsedSection != null) {
      instrToLineMap = await Task.Run(() => {
        var funcProcResult = profile.Process(parsedSection.Function, irInfo_.IR);

        if (funcProcResult != null) {
          return funcProcResult.BuildSampledElementsToLineMapping(profile, parsedSection);
        }

        return null;
      });
    }

    // For each source line, accumulate the weight of all instructions
    // mapped to that line, for both samples and performance counters.
    int lastLine = Math.Min(sourceProcResult.LastLineIndex, document.LineCount);
    int inserted = 0;
    document.SuspendUpdate();

    for (int lineNumber = sourceProcResult.FirstLineIndex; lineNumber <= lastLine; lineNumber++) {
      var documentLine = document.GetLineByNumber(lineNumber + inserted);
      var location = new TextLocation(documentLine.Offset, lineNumber + inserted - 1, 0);
      var dummyTuple = MakeDummyTuple(location, documentLine);
      lineToElementMap[lineNumber + inserted] = dummyTuple;
      lineToOriginalLineMap[lineNumber + inserted] = lineNumber;
      originalLineToLineMap[lineNumber] = lineNumber + inserted;

      if (sourceProcResult.SourceLineWeight.TryGetValue(lineNumber, out var lineWeight)) {
        processingResult.SampledElements.Add((dummyTuple, lineWeight));
      }

      if (sourceProcResult.SourceLineCounters.TryGetValue(lineNumber, out var counters)) {
        processingResult.CounterElements.Add((dummyTuple, counters));
      }

      // Insert assembly instructions for each source line.
      if (dummyTuple == null || instrToLineMap == null ||
          !instrToLineMap.SampledElements.TryGetValue(lineNumber, out var lineInstrs)) {
        continue;
      }

      var instrLine = document.GetLineByNumber(lineNumber + inserted);
      int rangeStart = instrLine.EndOffset;
      int rangeEnd = instrLine.EndOffset;

      foreach (var pair in lineInstrs) {
        var instr = pair.Element;
        var instrWeight = pair.Profile.Weight;
        var instrCounters = pair.Profile.Counters;
        string instrText = parsedSection.Text.Slice(instr.TextLocation.Offset, instr.TextLength).ToString();
        document.Document.Insert(instrLine.EndOffset, $"\n{instrText.TrimEnd()}");

        inserted++;
        instrLine = document.GetLineByNumber(lineNumber + inserted);
        rangeEnd = instrLine.EndOffset;

        location = new TextLocation(instrLine.Offset, lineNumber + inserted - 1, 0);
        dummyTuple = MakeDummyTuple(location, instrLine);
        lineToElementMap[lineNumber + inserted] = dummyTuple;

        if (instrWeight != TimeSpan.Zero) {
          processingResult.SampledElements.Add((dummyTuple, instrWeight));
        }

        if (instrCounters != null) {
          processingResult.CounterElements.Add((dummyTuple, instrCounters));
        }
      }

      assemblyRanges.Add((rangeStart, rangeEnd));
    }

    document.ResumeUpdate();
    processingResult.SortSampledElements(); // Used for ordering.
    processingResult.FunctionCountersValue = sourceProcResult.FunctionCountersValue;
    document.ProfileProcessingResult = processingResult;
    return new SourceLineProfileResult(processingResult, sourceProcResult, dummyFunc,
                                       lineToElementMap, lineToOriginalLineMap,
                                       originalLineToLineMap, assemblyRanges, inserted);
  }

  public List<InlineeListItem> GenerateInlineeList(FunctionProcessingResult result) {
    // Group the sample elements by the deepest inlinee they originate from.
    // This computes the total time per inlinee and a list of elements associated with it.
    var inlineeMap = new Dictionary<string, InlineeListItem>();

    foreach (var pair in result.SampledElements) {
      var element = pair.Item1;

      if (!element.TryGetTag(out SourceLocationTag sourceTag) ||
          !sourceTag.HasInlinees) {
        continue;
      }

      for (int i = 0; i < sourceTag.Inlinees.Count; i++) {
        var inlinee = sourceTag.Inlinees[i];

        if (string.IsNullOrEmpty(inlinee.Function)) {
          continue;
        }

        if (!inlineeMap.TryGetValue(inlinee.Function, out var inlineeItem)) {
          inlineeItem = new InlineeListItem(inlinee);
          inlineeMap[inlinee.Function] = inlineeItem;
        }

        inlineeItem.Weight += pair.Item2;
        inlineeItem.ElementWeights.Add((element, pair.Item2));

        if (i == 0) {
          inlineeItem.ExclusiveWeight += pair.Item2;
        }
      }
    }

    // Sort by decreasing weight.
    var inlineeList = inlineeMap.ToValueList();
    inlineeList.Sort((a, b) => b.ExclusiveWeight.CompareTo(a.ExclusiveWeight));
    return inlineeList;
  }

  public static void UpdateColumnStyle(OptionalColumn column, IRDocumentColumnData columnData,
                                       FunctionIR function, IRDocument document,
                                       ProfileDocumentMarkerSettings settings,
                                       OptionalColumnSettings columnSettings) {
#if DEBUG
    Trace.WriteLine($"Update column {column.ColumnName}, is main column: {column.IsMainColumn}");
#endif

    column.IsVisible = columnSettings.IsColumnVisible(column);
    var elementColorPairs = new List<ValueTuple<IRElement, Brush>>(function.TupleCount);
    var cells = columnData.ColumnValues.GetValueOrNull(column);

    // Empty columns don't have any values, ignore.
    if (cells != null) {
      foreach (var value in cells) {
        value.BackColor = settings.PickDefaultBackColor(column);
      }
    }

    foreach (var tuple in function.AllTuples) {
      var value = columnData.GetColumnValue(tuple, column);
      if (value == null)
        continue;

      int order = value.ValueOrder;
      double percentage = value.ValuePercentage;

      if (value.CanShowBackgroundColor) {
        var color = settings.PickBackColor(column, order, percentage);

        if (column.IsMainColumn && percentage >= settings.ElementWeightCutoff) {
          elementColorPairs.Add(new ValueTuple<IRElement, Brush>(tuple, color));
        }

        // Don't override initial back color if no color is picked,
        // mostly done for perf metrics column which have an initial back color.
        if (!color.IsTransparent()) {
          value.BackColor = color;
        }
      }

      value.TextColor = settings.PickTextColor(column, order, percentage);
      value.TextWeight = settings.PickTextWeight(column, order, percentage);

      if (value.CanShowIcon) {
        value.Icon = settings.PickIcon(column, value.ValueOrder, value.ValuePercentage).Icon;
      }

      value.ShowPercentageBar = value.CanShowPercentageBar && // Disabled per value
                                settings.ShowPercentageBar(column, value.ValueOrder, value.ValuePercentage);
      value.PercentageBarBackColor = settings.PickPercentageBarColor(column);
      value.PercentageBarMaxWidth = settings.MaxPercentageBarWidth;
    }

    // Mark the elements themselves with a color.
    if (column.IsMainColumn && document != null) {
      document.ClearInstructionMarkers();

      if (settings.MarkElements) {
        document.MarkElements(elementColorPairs);
      }
    }
  }

  public void MarkCallSites(IRDocument document, FunctionIR function, IRTextFunction textFunction,
                            SourceLineProfileResult processingResult) {
    var metadataTag = function.GetTag<AssemblyMetadataTag>();
    bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

    if (hasInstrOffsetMetadata) {
      MarkCallSites(document, function, textFunction, metadataTag, processingResult);
    }
  }

  private void MarkCallSites(IRDocument document, FunctionIR function, IRTextFunction textFunction,
                             AssemblyMetadataTag metadataTag, SourceLineProfileResult processingResult = null,
                             bool suspendUpdates = true) {
    // Mark indirect call sites and list the hottest call targets.
    // Useful especially for virtual function calls.
    var callTree = globalProfile_.CallTree;

    if (callTree == null || !settings_.MarkCallTargets) {
      return;
    }

    var overlayMap = new Dictionary<IRElement, (List<ProfileCallTreeNode> List, bool HasIndirectCalls)>();
    var node = callTree.GetCombinedCallTreeNode(textFunction);

    if (node == null || !node.HasCallSites) {
      return;
    }

    foreach (var callsite in node.CallSites.Values) {
      if (!FunctionProfileData.TryFindElementForOffset(metadataTag, callsite.RVA - profile_.FunctionDebugInfo.RVA,
                                                       irInfo_.IR, out var element)) {
        continue;
      }

      //Trace.WriteLine($"Found CS for elem at RVA {callsite.RVA}, weight {callsite.Weight}: {element}");
      var instr = element as InstructionIR;
      if (instr == null || !irInfo_.IR.IsCallInstruction(instr))
        continue;

      // Mark direct, known call targets with a different icon.
      var callTarget = irInfo_.IR.GetCallTarget(instr);
      bool isDirectCall = callTarget is {HasName: true} &&
                          !callTarget.HasTag<RegisterTag>();

      // When annotating a source file, map the instruction to the
      // fake tuple used the represent the source line.
      if (processingResult != null) {
        if (!instr.TryGetTag(out SourceLocationTag sourceTag)) {
          continue; // Couldn't map for some reason, ignore.
        }

        // With assembly lines, source line numbers are shifted.
        int sourceLine = sourceTag.Line;

        if (processingResult.OriginalLineToLineMap.TryGetValue(sourceTag.Line, out int mappedLine)) {
          sourceLine = mappedLine;
        }

        if (!processingResult.LineToElementMap.TryGetValue(sourceLine, out element)) {
          continue; // Couldn't map for some reason, ignore.
        }
      }

      // Collect call targets and override the weight
      // to include only the weight at this call site.
      if (!overlayMap.TryGetValue(element, out var pair)) {
        pair = new ValueTuple<List<ProfileCallTreeNode>, bool>();
        pair.List = new List<ProfileCallTreeNode>();
        overlayMap[element] = pair;
      }

      // Mark if any of the calls are indirect.
      if (!isDirectCall) {
        pair.HasIndirectCalls = true;
        overlayMap[element] = pair; // Update dictionary, since pair is a value type.
      }

      foreach (var target in callsite.SortedTargets) {
        var callsiteNode = new ProfileCallTreeGroupNode(target.Node, target.Weight);
        pair.List.Add(callsiteNode);
      }
    }

    // Add the overlays to the document.
    var indirectIcon = IconDrawing.FromIconResource("ExecuteIconColor");
    var directIcon = IconDrawing.FromIconResource("ExecuteIcon");
    var overlayListMap = new Dictionary<IElementOverlay, List<ProfileCallTreeNode>>();

    if (suspendUpdates) {
      document.SuspendUpdate();
    }

    foreach (var (element, pair) in overlayMap) {
      var color = App.Settings.DocumentSettings.BackgroundColor;

      if (element.ParentBlock != null && !element.ParentBlock.HasEvenIndexInFunction) {
        color = App.Settings.DocumentSettings.AlternateBackgroundColor;
      }

      var icon = pair.HasIndirectCalls ? indirectIcon : directIcon;
      var overlay = document.RegisterIconElementOverlay(element, icon, 16, 16);
      overlay.Tag = ProfileOverlayTag;
      overlay.Background = color.AsBrush();
      overlay.IsLabelPinned = false;
      overlay.AllowLabelEditing = false;
      overlay.UseLabelBackground = true;
      overlay.ShowBackgroundOnMouseOverOnly = true;
      overlay.ShowBorderOnMouseOverOnly = true;
      overlay.AlignmentX = HorizontalAlignment.Left;
      overlay.MarginY = 2;
      overlayListMap[overlay] = pair.List;

      if (element is InstructionIR instr) {
        // Place before the call opcode.
        int lineOffset = instr.OpcodeLocation.Offset - instr.TextLocation.Offset;
        overlay.MarginX = Utils.MeasureString(lineOffset, Utils.GetTextTypeface(document),
                                              document.FontSize).Width - 20;
      }
    }

    // Show a popup on hover with the list of call targets.
    SetupCallSiteHoverPreview(overlayListMap, document);

    if (suspendUpdates) {
      document.ResumeUpdate();
    }
  }

  private void SetupCallSiteHoverPreview(Dictionary<IElementOverlay, List<ProfileCallTreeNode>> overlayListMap,
                                         IRDocument document) {
    // The overlay hover preview is somewhat of a hack,
    // since the hover event is fired over the entire document,
    // but the popup should be shown only if mouse is over the overlay.
    var view = document as UIElement;
    CallTreeNodePopup popup = null;
    (IElementOverlay Overlay, List<ProfileCallTreeNode> List) hoveredOverlay = (null, null);

    // Create a single hover handler for all overlays.
    var preview = new PopupHoverPreview(
      view, HoverPreview.HoverDuration,
      (mousePoint, previewPoint) => {
        if (hoveredOverlay.Overlay == null) {
          return null; // Nothing actually hovered.
        }

        if (popup == null) {
          var dummy = new DummyFunctionProfileInfoProvider();
          popup = new CallTreeNodePopup(null, dummy, previewPoint, view,
                                        document.Session);
          popup.TitleText = "Call Targets";
        }
        else {
          popup.UpdatePosition(previewPoint, view);
        }

        popup.ShowFunctions(hoveredOverlay.List, irInfo_.NameProvider.FormatFunctionName);
        return popup;
      },
      (mousePoint, popup) => true,
      popup => {
        document.Session.RegisterDetachedPanel(popup);
      });

    foreach (var pair in overlayListMap) {
      pair.Key.OnHover += (sender, e) => {
        hoveredOverlay.Overlay = sender as IElementOverlay;
        hoveredOverlay.List = overlayListMap[hoveredOverlay.Overlay];
      };

      pair.Key.OnHoverEnd += (sender, e) => {
        preview.HideDelayed();
        hoveredOverlay = (null, null);
      };
    }

    document.RegisterHoverPreview(preview);
  }

  private void MarkProfiledBlocks(List<(BlockIR, TimeSpan)> blockWeights, IRDocument document, bool suspendUpdates) {
    if (!settings_.MarkBlocks) {
      return;
    }

    if (suspendUpdates) {
      document.SuspendUpdate();
    }

    double overlayHeight = document.DefaultLineHeight;
    var blockPen = ColorPens.GetPen(settings_.BlockOverlayBorderColor,
                                    settings_.BlockOverlayBorderThickness);

    for (int i = 0; i < blockWeights.Count; i++) {
      var block = blockWeights[i].Item1;
      var weight = blockWeights[i].Item2;
      double weightPercentage = profile_.ScaleWeight(weight);

      var icon = settings_.PickIconForOrder(i, weightPercentage);
      var color = settings_.PickBackColorForPercentage(weightPercentage);

      if (color.IsTransparent()) {
        // Match the background color of the corresponding text line.
        color = block.HasEvenIndexInFunction ?
          App.Settings.DocumentSettings.BackgroundColor.AsBrush() :
          App.Settings.DocumentSettings.AlternateBackgroundColor.AsBrush();
      }

      bool markOnFlowGraph = settings_.IsSignificantValue(i, weightPercentage);
      string label = $"{weightPercentage.AsTrimmedPercentageString()}";
      string tooltip = settings_.FormatWeightValue(weight);
      var overlay = document.RegisterIconElementOverlay(block, icon, 0, overlayHeight, label, tooltip);
      overlay.Tag = ProfileOverlayTag;
      overlay.Background = color;
      overlay.Border = blockPen;
      overlay.IsLabelPinned = true;
      overlay.AllowLabelEditing = false;
      overlay.UseLabelBackground = true;
      overlay.ShowBackgroundOnMouseOverOnly = false;
      overlay.ShowBorderOnMouseOverOnly = false;
      overlay.AlignmentX = HorizontalAlignment.Left;
      overlay.MarginX = -1;
      overlay.Padding = 2;
      (overlay.TextColor, overlay.TextWeight) = settings_.PickBlockOverlayStyle(i, weightPercentage);

      // Mark the block itself with a color.
      document.MarkBlock(block, color, markOnFlowGraph);

      if (settings_.MarkBlocksInFlowGraph &&
          weightPercentage > settings_.ElementWeightCutoff) {
        block.AddTag(GraphNodeTag.MakeColor(weightPercentage.AsTrimmedPercentageString(),
                                            ((SolidColorBrush)color).Color,
                                            ((SolidColorBrush)overlay.TextColor).Color,
                                            ((SolidColorBrush)overlay.TextColor).Color,
                                            i < 3 || weightPercentage >= 0.1)); // Bold text for >10%.
      }
    }

    if (suspendUpdates) {
      document.ResumeUpdate();
    }
  }

  private async Task<IRDocumentColumnData>
    MarkProfiledElements(FunctionProcessingResult result,
                         FunctionIR function, IRDocument document) {
    // Add a time column.
    var elements = result.SampledElements;
    var columnData = new IRDocumentColumnData(function.InstructionCount);
    var percentageColumn = columnData.AddColumn(TimePercentageColumnTemplate());
    var timeColumn = columnData.AddColumn(TimeColumnTemplate());
    percentageColumn.IsMainColumn = true;

    await Task.Run(() => {
      for (int i = 0; i < elements.Count; i++) {
        var element = elements[i].Item1;
        var weight = elements[i].Item2;
        double weightPercentage = profile_.ScaleWeight(weight);

        string label = settings_.FormatWeightValue(weight);
        string percentageLabel = weightPercentage.AsTrimmedPercentageString();
        var columnValue = new ElementColumnValue(label, weight.Ticks, weightPercentage, i);
        var percentageColumnValue = new ElementColumnValue(percentageLabel, weight.Ticks, weightPercentage, i);
        columnValue.ToolTip = percentageLabel;
        percentageColumnValue.ToolTip = label;

        columnData.AddValue(percentageColumnValue, element, percentageColumn);
        var valueGroup = columnData.AddValue(columnValue, element, timeColumn);
        //? valueGroup.BackColor = Brushes.Bisque;
      }
    });

    // Handle performance counter columns.
    var counterElements = result.CounterElements;

    if (counterElements.Count == 0) {
      SetupColumnHeaderEvents(function, document, columnData);
      return columnData;
    }

    var perfCounters = globalProfile_.SortedPerformanceCounters;
    var counterColumns = new OptionalColumn[perfCounters.Count];
    var counterSortMap = new List<List<CounterSortHelper>>();

    await Task.Run(() => {
      // Add a column for each counter.
      for (int k = 0; k < perfCounters.Count; k++) {
        CreatePerfCounterColumn(function, document, columnData,
                                perfCounters, counterColumns, k);
      }

      // Build lists, sort lists by value, then go over lists and assign ValueOrder.
      for (int k = 0; k < perfCounters.Count; k++) {
        counterSortMap.Add(new List<CounterSortHelper>(counterElements.Count));
      }

      for (int i = 0; i < counterElements.Count; i++) {
        var element = counterElements[i].Item1;
        var counterSet = counterElements[i].Item2;

        for (int k = 0; k < perfCounters.Count; k++) {
          var counter = perfCounters[k];
          long value = 0;
          double valuePercentage = 0;
          string label = "";
          string tooltip = null;
          bool isValueBasedMetric = false;

          if (counter.IsMetric) {
            var metric = counter as PerformanceMetric;
            valuePercentage = metric.ComputeMetric(counterSet, out long baseValue, out long relativeValue);

            // Don't show metrics for counters with few hits,
            // they tend to be the ones that are the most inaccurate.
            double metricBasePercentage = result.ScaleCounterValue(baseValue, metric.BaseCounter);

            if (metricBasePercentage > 0.01) {
              label = FormatPerformanceMetric(valuePercentage, metric);
              value = (long)(valuePercentage * 10000);
              isValueBasedMetric = !metric.Config.IsPercentage;
              tooltip = "Per instruction";
            }
            else {
              valuePercentage = 0;
            }
          }
          else {
            value = counterSet.FindCounterValue(counter);

            if (value == 0) {
              continue;
            }

            valuePercentage = result.ScaleCounterValue(value, counter);
            label = valuePercentage.AsTrimmedPercentageString();
            tooltip = FormatPerformanceCounter(value, counter);
          }

          //? TODO: Could have a config for all/per-counter to pick % or value as label
          //var label = $"{value * counter.Interval}";
          var columnValue = new ElementColumnValue(label, value, valuePercentage, i, tooltip);
          columnValue.ValuePercentage = valuePercentage;
          columnValue.CanShowPercentageBar = !isValueBasedMetric &&
                                             valuePercentage >= settings_.ElementWeightCutoff;
          columnData.AddValue(columnValue, element, counterColumns[k]);

          var counterValueList = counterSortMap[k];
          counterValueList.Add(new CounterSortHelper(columnValue, value));
        }
      }
    });

    // Sort the counters from each column in decreasing order,
    // then assign the ValueOrder for each counter based on the sorting index.
    for (int k = 0; k < perfCounters.Count; k++) {
      var counterValueList = counterSortMap[k];
      counterValueList.Sort((a, b) => -a.Value.CompareTo(b.Value));

      for (int i = 0; i < counterValueList.Count; i++) {
        counterValueList[i].ColumnValue.ValueOrder = i;
      }
    }

    SetupColumnHeaderEvents(function, document, columnData);
    return columnData;
  }

  public void UpdateColumnStyles(IRDocumentColumnData columnData,
                                 FunctionIR function, IRDocument document) {
    document.SuspendUpdate();

    foreach (var column in columnData.Columns) {
      UpdateColumnStyle(column, columnData, function, document, settings_, columnSettings_);
    }

    document.ResumeUpdate();
  }

  private void SetupColumnHeaderEvents(FunctionIR function, IRDocument document,
                                       IRDocumentColumnData columnData) {
    foreach (var column in columnData.Columns) {
      column.HeaderClickHandler += ColumnHeaderClickHandler(document, function, columnData);
    }
  }

  private void CreatePerfCounterColumn(FunctionIR function, IRDocument document,
                                       IRDocumentColumnData columnData, List<PerformanceCounter> perfCounters,
                                       OptionalColumn[] counterColumns, int k) {
    var counterInfo = perfCounters[k];
    counterColumns[k] = CounterColumnTemplate(counterInfo, k);
    counterColumns[k].IsVisible = IsPerfCounterVisible(counterInfo);
    counterColumns[k].PerformanceCounter = counterInfo;
    columnData.AddColumn(counterColumns[k]);
  }

  private OptionalColumnEventHandler ColumnHeaderClickHandler(IRDocument document, FunctionIR function,
                                                              IRDocumentColumnData columnData) {
    return (column, columnHeader) => {
      var currentMainColumn = columnData.MainColumn;

      if (column == currentMainColumn) {
        return;
      }

      if (currentMainColumn != null) {
        currentMainColumn.IsMainColumn = false;
        UpdateColumnStyle(currentMainColumn, columnData, function, document, settings_, columnSettings_);
      }

      column.IsMainColumn = true;
      UpdateColumnStyle(column, columnData, function, document, settings_, columnSettings_);
    };
  }

  private class DummyFunctionProfileInfoProvider : IFunctionProfileInfoProvider {
    public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
      return new List<ProfileCallTreeNode>();
    }

    public (List<ProfileCallTreeNode>, List<ModuleProfileInfo> Modules) GetTopFunctionsAndModules(
      ProfileCallTreeNode node) {
      return new ValueTuple<List<ProfileCallTreeNode>, List<ModuleProfileInfo>>();
    }

    public List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
      return new List<ModuleProfileInfo>();
    }
  }

  private struct CounterSortHelper {
    public ElementColumnValue ColumnValue;
    public long Value;

    public CounterSortHelper(ElementColumnValue columnValue, long value) {
      ColumnValue = columnValue;
      Value = value;
    }
  }
}