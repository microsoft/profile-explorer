// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerUI.Compilers;
using IRExplorerUI.Document;
using TextLocation = IRExplorerCore.TextLocation;

namespace IRExplorerUI.Profile;

// Used as the interface for both the full IR document and lightweight version (source file).
public interface MarkedDocument {
  ISession Session { get; }
  FunctionIR Function { get; }
  double DefaultLineHeight { get; }
  int LineCount { get; }
  IRDocumentColumnData ProfileColumnData { get; set; }
  FunctionProcessingResult ProfileProcessingResult { get; set; }
  public void SuspendUpdate();
  public void ResumeUpdate();
  public void ClearInstructionMarkers();
  public void MarkElements(ICollection<ValueTuple<IRElement, Color>> elementColorPairs);
  public void MarkBlock(IRElement element, Color selectedColor, bool raiseEvent = true);
  public DocumentLine GetLineByNumber(int lineNumber);
  public DocumentLine GetLineByOffset(int offset);

  public IconElementOverlay RegisterIconElementOverlay(IRElement element, IconDrawing icon,
                                                       double width, double height,
                                                       string label = null, string tooltip = null);
}

public class ProfileDocumentMarker {
  // Templates for the time columns defining the style.
  public static readonly OptionalColumn TIME_COLUMN =
    OptionalColumn.Template("[TimeHeader]", "TimePercentageColumnValueTemplate",
                            "TimeHeader", "Time (ms)", "Instruction time",
                            null, 50.0, "TimeColumnHeaderTemplate");
  public static readonly OptionalColumn TIME_PERCENTAGE_COLUMN =
    OptionalColumn.Template("[TimePercentageHeader]", "TimePercentageColumnValueTemplate",
                            "TimePercentageHeader", "Time (%)", "Instruction time percentage relative to function time",
                            null, 50.0, "TimeColumnHeaderTemplate");

  public OptionalColumn TimeColumnTemplate() {
    TIME_COLUMN.Style = columnSettings_.GetColumnStyle(TIME_COLUMN) ??
                        OptionalColumnSettings.DefaultTimeColumnStyle;

    return TIME_COLUMN;
  }

  public OptionalColumn TimePerrcentageColumnTemplate() {
    TIME_PERCENTAGE_COLUMN.Style = columnSettings_.GetColumnStyle(TIME_PERCENTAGE_COLUMN) ??
                                   OptionalColumnSettings.DefaultTimePercentageColumnStyle;
    return TIME_PERCENTAGE_COLUMN;
  }

  public OptionalColumn CounterColumnTemplate(PerformanceCounter counter, int index) {
    var column = OptionalColumn.Template($"[CounterHeader{counter.Id}]",
                                         "TimePercentageColumnValueTemplate",
                                         $"CounterHeader{counter.Name}",
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

    //? TODO: UI part
    if (!string.IsNullOrEmpty(column.Style.Abbreviation)) {
      column.Title = column.Style.Abbreviation;
    }

    return column;
  }

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
    if (counter.Frequency > 1000) {
      double valueK = (double)(value * counter.Frequency) / 1000;
      return $"{valueK:##}K";
    }

    return $"{value * counter.Frequency}";
  }

  public static string ShortenPerfCounterName(string name) {
    foreach (var replacement in PerfCounterNameReplacements) {
      int index = name.LastIndexOf(replacement.Item1);

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

  public async Task<IRDocumentColumnData> Mark(MarkedDocument document, FunctionIR function,
                                               IRTextFunction textFunction) {
    document.SuspendUpdate();
    IRDocumentColumnData columnData = null;
    var metadataTag = function.GetTag<AssemblyMetadataTag>();
    bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

    if (hasInstrOffsetMetadata) {
      var result = await Task.Run(() => profile_.Process(function, irInfo_.IR));
      columnData = await MarkProfiledElements(result, function, document);
      document.ProfileProcessingResult = result;
      document.ProfileColumnData = columnData;

      //? TODO: UI option to display these
      MarkProfiledBlocks(result.BlockSampledElements, document);
      MarkCallSites(document, function, textFunction, metadataTag);
    }

    document.ResumeUpdate();
    return columnData;
  }

  public async Task<IRDocumentColumnData> MarkSourceLines(MarkedDocument document,
                                                          SourceLineProfileResult processingResult) {
    document.ProfileColumnData =
      await MarkProfiledElements(processingResult.Result, processingResult.Function, document);
    return document.ProfileColumnData;
  }

  public record SourceLineProfileResult(
    FunctionProcessingResult Result,
    SourceLineProcessingResult SourceLineResult,
    FunctionIR Function,
    Dictionary<int, IRElement> LineToElementMap);

  public SourceLineProfileResult
    PrepareSourceLineProfile(FunctionProfileData profile, MarkedDocument document, IDebugInfoProvider debugInfo) {
    var result = profile.ProcessSourceLines(debugInfo, irInfo_.IR);
    var sourceLineWeights = result.SourceLineWeightList;

    if (sourceLineWeights.Count == 0) {
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

    TupleIR MakeDummyTuple(TextLocation textLocation, DocumentLine documentLine) {
      var tupleIr = new TupleIR(ids.NextTuple(), TupleKind.Other, dummyBlock);
      tupleIr.TextLocation = textLocation;
      tupleIr.TextLength = documentLine.Length;
      dummyBlock.Tuples.Add(tupleIr);
      return tupleIr;
    }

    // For each source line, accumulate the weight of all instructions
    // mapped to that line, for both samples and performance counters.
    for (int lineNumber = result.FirstLineIndex; lineNumber <= result.LastLineIndex; lineNumber++) {
      TupleIR dummyTuple = null;

      if (result.SourceLineWeight.TryGetValue(lineNumber, out var lineWeight)) {
        var documentLine = document.GetLineByNumber(lineNumber);
        var location = new TextLocation(documentLine.Offset, lineNumber - 1, 0);
        dummyTuple = MakeDummyTuple(location, documentLine);
        processingResult.SampledElements.Add((dummyTuple, lineWeight));
        lineToElementMap[lineNumber] = dummyTuple;
      }

      if (result.SourceLineCounters.TryGetValue(lineNumber, out var counters)) {
        var documentLine = document.GetLineByNumber(lineNumber);
        var location = new TextLocation(documentLine.Offset, lineNumber - 1, 0);
        dummyTuple ??= MakeDummyTuple(location, documentLine);
        lineToElementMap[lineNumber] = dummyTuple;
        processingResult.CounterElements.Add((dummyTuple, counters));
      }
    }

    processingResult.SortSampledElements(); // Used for ordering.
    processingResult.FunctionCountersValue = result.FunctionCountersValue;
    document.ProfileProcessingResult = processingResult;
    return new SourceLineProfileResult(processingResult, result, dummyFunc, lineToElementMap);
  }

  public void ApplyColumnStyle(OptionalColumn column, IRDocumentColumnData columnData,
                               FunctionIR function, MarkedDocument document) {
    Trace.WriteLine($"Apply {column.ColumnName}, is main column: {column.IsMainColumn}");
    var elementColorPairs = new List<ValueTuple<IRElement, Color>>(function.TupleCount);

    foreach (var tuple in function.AllTuples) {
      var value = columnData.GetColumnValue(tuple, column);
      if (value == null)
        continue;

      int order = value.ValueOrder;
      double percentage = value.ValuePercentage;
      var color = settings_.PickBackColor(column, order, percentage);

      if (column.IsMainColumn && percentage >= settings_.ElementWeightCutoff) {
        elementColorPairs.Add(new ValueTuple<IRElement, Color>(tuple, color));
      }

      value.BackColor = color.AsBrush();
      value.TextColor = settings_.PickTextColor(column, order, percentage);
      value.TextWeight = settings_.PickTextWeight(column, order, percentage);

      value.Icon = settings_.PickIcon(column, value.ValueOrder, value.ValuePercentage).Icon;
      value.ShowPercentageBar = value.ShowPercentageBar && // Disabled per value
                                settings_.ShowPercentageBar(column, value.ValueOrder, value.ValuePercentage);
      value.PercentageBarBackColor = settings_.PickPercentageBarColor(column);
    }

    // Mark the elements themselves with a color.
    //? TODO: Check settings from UI
    //? Needs a tag so later a RemoveMarkedElements(tag) can be done
    if (column.IsMainColumn && document != null) {
      document.ClearInstructionMarkers();
      document.MarkElements(elementColorPairs);
    }
  }

  private class DummyFunctionProfileInfoProvider : IFunctionProfileInfoProvider {
    public List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node) {
      return new List<ProfileCallTreeNode>();
    }

    public List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node) {
      return new List<ProfileCallTreeNode>();
    }

    public List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node) {
      return new List<ModuleProfileInfo>();
    }
  }

  public void MarkCallSites(MarkedDocument document, FunctionIR function, IRTextFunction textFunction,
                            SourceLineProfileResult processingResult) {
    var metadataTag = function.GetTag<AssemblyMetadataTag>();
    bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

    if (hasInstrOffsetMetadata) {
      MarkCallSites(document, function, textFunction, metadataTag, processingResult);
    }
  }

  private void MarkCallSites(MarkedDocument document, FunctionIR function, IRTextFunction textFunction,
                             AssemblyMetadataTag metadataTag, SourceLineProfileResult processingResult = null) {
    // Mark indirect call sites and list the hottest call targets.
    // Useful especially for virtual function calls.
    var callTree = globalProfile_.CallTree;

    if (callTree == null) {
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
      bool isDirectCall = callTarget is {HasName: true};

      // When annotating a source file, map the instruction to the
      // fake tuple used the represent the source line.
      if (processingResult != null) {
        if (!instr.TryGetTag(out SourceLocationTag sourceTag) ||
            !processingResult.LineToElementMap.TryGetValue(sourceTag.Line, out element)) {
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
      }

      foreach (var target in callsite.SortedTargets) {
        var callsiteNode = new ProfileCallTreeGroupNode(target.Node, target.Weight);
        pair.List.Add(callsiteNode);
      }
    }

    // Add the overlays to the document.
    var indirectIcon = IconDrawing.FromIconResource("ExecuteIconColor");
    var directIcon = IconDrawing.FromIconResource("ExecuteIcon");

    foreach (var (element, pair) in overlayMap) {
      var color = App.Settings.DocumentSettings.BackgroundColor;

      if (element.ParentBlock != null && !element.ParentBlock.HasEvenIndexInFunction) {
        color = App.Settings.DocumentSettings.AlternateBackgroundColor;
      }

      var icon = pair.HasIndirectCalls ? indirectIcon : directIcon;
      var overlay = document.RegisterIconElementOverlay(element, icon, 16, 16);
      overlay.Background = color.AsBrush();
      overlay.IsLabelPinned = false;
      overlay.AllowLabelEditing = false;
      overlay.UseLabelBackground = true;
      overlay.ShowBackgroundOnMouseOverOnly = true;
      overlay.ShowBorderOnMouseOverOnly = true;
      overlay.AlignmentX = HorizontalAlignment.Left;
      overlay.MarginY = 1;

      if (element is InstructionIR instr) {
        // Place before the call opcode.
        int lineOffset = instr.OpcodeLocation.Offset - instr.TextLocation.Offset;
        overlay.MarginX = Utils.MeasureString(lineOffset, App.Settings.DocumentSettings.FontName,
                                              App.Settings.DocumentSettings.FontSize).Width - 20;
      }

      // Show a popup on hover with the list of call targets.
      SetupCallSiteHoverPreview(overlay, pair.List, document);
    }
  }

  private void SetupCallSiteHoverPreview(IconElementOverlay overlay, List<ProfileCallTreeNode> list,
                                         MarkedDocument document) {
    // The overlay hover preview is somewhat of a hack,
    // since the hover event is fired over the entire document,
    // but the popup should be shown only if mouse is over the overlay.
    //? TODO: Find a way to integrate hover login into overaly.OnHover
    var view = document as UIElement;
    CallTreeNodePopup popup = null;
    IElementOverlay hoveredOverlay = null;

    var preview = new PopupHoverPreview(
      view, HoverPreview.HoverDuration,
      (mousePoint, previewPoint) => {
        if (hoveredOverlay == null) {
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

        popup.ShowFunctions(list, irInfo_.NameProvider.FormatFunctionName);
        return popup;
      },
      (mousePoint, popup) => true,
      popup => {
        document.Session.RegisterDetachedPanel(popup);
      });

    overlay.OnHover += (sender, e) => {
      hoveredOverlay = sender as IElementOverlay;
    };

    overlay.OnHoverEnd += (sender, e) => {
      preview.HideDelayed();
      hoveredOverlay = null;
    };
  }

  private void MarkProfiledBlocks(List<(BlockIR, TimeSpan)> blockWeights, MarkedDocument document) {
    document.SuspendUpdate();
    double overlayHeight = document.DefaultLineHeight;
    var blockPen = ColorPens.GetPen(settings_.BlockOverlayBorderColor,
                                    settings_.BlockOverlayBorderThickness);

    for (int i = 0; i < blockWeights.Count; i++) {
      var block = blockWeights[i].Item1;
      var weight = blockWeights[i].Item2;
      double weightPercentage = profile_.ScaleWeight(weight);

      var icon = settings_.PickIconForOrder(i, weightPercentage);
      var color = settings_.PickBackColorForPercentage(weightPercentage);

      if (color == Colors.Transparent) {
        // Match the background color of the corresponding text line.
        color = block.HasEvenIndexInFunction ?
          App.Settings.DocumentSettings.BackgroundColor :
          App.Settings.DocumentSettings.AlternateBackgroundColor;
      }

      bool markOnFlowGraph = settings_.IsSignificantValue(i, weightPercentage);
      string label = $"{weightPercentage.AsTrimmedPercentageString()}";
      var overlay = document.RegisterIconElementOverlay(block, icon, 0, overlayHeight, label, "");
      overlay.Background = color.AsBrush();
      overlay.Border = blockPen;

      overlay.IsLabelPinned = true;
      overlay.AllowLabelEditing = false;
      overlay.UseLabelBackground = true;
      overlay.ShowBackgroundOnMouseOverOnly = false;
      overlay.ShowBorderOnMouseOverOnly = false;
      overlay.AlignmentX = HorizontalAlignment.Left;
      overlay.MarginX = -1;
      overlay.Padding = 2;

      if (markOnFlowGraph) {
        overlay.TextColor = settings_.HotBlockOverlayTextColor.AsBrush();
        overlay.TextWeight = FontWeights.Bold;
      }
      else {
        overlay.TextColor = settings_.BlockOverlayTextColor.AsBrush();
      }

      // Mark the block itself with a color.
      document.MarkBlock(block, color, markOnFlowGraph);

      if (weightPercentage > settings_.ElementWeightCutoff) {
        block.AddTag(GraphNodeTag.MakeColor(weightPercentage.AsTrimmedPercentageString(), color));
      }
    }
  }

  private async Task<IRDocumentColumnData>
    MarkProfiledElements(FunctionProcessingResult result,
                         FunctionIR function, MarkedDocument document) {
    var elements = result.SampledElements;

    // Add a time column.
    var columnData = new IRDocumentColumnData(function.InstructionCount);
    var percentageColumn = columnData.AddColumn(TimePerrcentageColumnTemplate());
    var timeColumn = columnData.AddColumn(TimeColumnTemplate());

    await Task.Run(() => {
      for (int i = 0; i < elements.Count; i++) {
        var element = elements[i].Item1;
        var weight = elements[i].Item2;
        double weightPercentage = profile_.ScaleWeight(weight);

        string label = weight.AsMillisecondsString();
        string percentageLabel = weightPercentage.AsTrimmedPercentageString();
        var columnValue = new ElementColumnValue(label, weight.Ticks, weightPercentage, i);
        var percentageColumnValue = new ElementColumnValue(percentageLabel, weight.Ticks, weightPercentage, i);
        columnValue.ToolTip = percentageLabel;
        percentageColumnValue.ToolTip = label;

        columnData.AddValue(percentageColumnValue, element, percentageColumn);
        var valueGroup = columnData.AddValue(columnValue, element, timeColumn);
        //valueGroup.BackColor = Brushes.Bisque;
      }
    });

    percentageColumn.IsMainColumn = true;
    ApplyColumnStyle(percentageColumn, columnData, function, document);
    ApplyColumnStyle(timeColumn, columnData, function, document);

    percentageColumn.HeaderClickHandler += ColumnHeaderClickHandler(document, function, columnData);
    timeColumn.HeaderClickHandler += ColumnHeaderClickHandler(document, function, columnData);

    var counterElements = result.CounterElements;

    if (counterElements.Count == 0) {
      return columnData;
    }

    //? TODO: Filter to hide counters
    //? TODO: Order of counters (custom sorting or fixed)
    //? TODO: Way to set a counter as a baseline, another diff to it in %
    //?    misspredictedBranches / totalBranches
    //?    takenBranches / total, etc JSON
    var perfCounters = globalProfile_.SortedPerformanceCounters;
    var colors = new Brush[] {Brushes.DarkSlateBlue, Brushes.DarkOliveGreen, Brushes.DarkSlateGray};
    var counterIcon = IconDrawing.FromIconResource("QueryIcon");
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

          //? Could have a config for all/per-counter to pick % or value as label
          //var label = $"{value * counter.Interval}";
          var columnValue = new ElementColumnValue(label, value, valuePercentage, i, tooltip);

          var color = colors[counter.Index % colors.Length];
          //? TODO: columnValue.TextColor = color;
          if (counter.IsMetric)
            columnValue.BackColor = Brushes.Beige;
          columnValue.ValuePercentage = valuePercentage;
          //? TODO: Show bar only if any value is much higher? Std dev
          columnValue.ShowPercentageBar = !isValueBasedMetric && valuePercentage >= 0.03;
          columnValue.PercentageBarBackColor = color;
          columnData.AddValue(columnValue, element, counterColumns[k]);

          var counterValueList = counterSortMap[k];
          counterValueList.Add(new CounterSortHelper(columnValue, value));
        }
      }
    });

    // Sort the counters from each column in decreasing order,
    // then assign the ValueOrder for each counter based on the sorting index.
    //? TODO: Sort lists in parallel
    for (int k = 0; k < perfCounters.Count; k++) {
      var counterValueList = counterSortMap[k];
      counterValueList.Sort((a, b) => -a.Value.CompareTo(b.Value));

      for (int i = 0; i < counterValueList.Count; i++) {
        counterValueList[i].ColumnValue.ValueOrder = i;
      }
    }

    foreach (var column in counterColumns) {
      ApplyColumnStyle(column, columnData, function, document);
    }

    return columnData;
  }

  private void CreatePerfCounterColumn(FunctionIR function, MarkedDocument document,
                                       IRDocumentColumnData columnData, List<PerformanceCounter> perfCounters,
                                       OptionalColumn[] counterColumns, int k) {
    var counterInfo = perfCounters[k];
    counterColumns[k] = CounterColumnTemplate(counterInfo, k);
    counterColumns[k].IsVisible = IsPerfCounterVisible(counterInfo);
    counterColumns[k].HeaderClickHandler += ColumnHeaderClickHandler(document, function, columnData);
    columnData.AddColumn(counterColumns[k]);
  }

  private OptionalColumnEventHandler ColumnHeaderClickHandler(MarkedDocument document, FunctionIR function,
                                                              IRDocumentColumnData columnData) {
    return column => {
      var currentMainColumn = columnData.MainColumn;

      if (column == currentMainColumn) {
        return;
      }

      if (currentMainColumn != null) {
        currentMainColumn.IsMainColumn = false;
        ApplyColumnStyle(currentMainColumn, columnData, function, document);
      }

      column.IsMainColumn = true;
      ApplyColumnStyle(column, columnData, function, document);
    };
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