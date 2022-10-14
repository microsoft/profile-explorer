// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System.Collections.Generic;
using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.ASM;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using IRExplorerUI.Profile;
using IRExplorerCore.IR.Tags;
using IRExplorerUI.Document;
using System.Windows.Documents;
using System.IO;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using System.Text;

namespace IRExplorerUI.Compilers.ASM {
    // Used as the interface for both the full IR document and lightweight version (source file).
    public interface MarkedDocument {
        double DefaultLineHeight { get; }
        public void SuspendUpdate();
        public void ResumeUpdate();
        public void ClearInstructionMarkers();
        public void MarkElements(ICollection<ValueTuple<IRElement, Color>> elementColorPairs);
        public void MarkBlock(IRElement element, Color selectedColor, bool raiseEvent = true);
        public IconElementOverlay RegisterIconElementOverlay(IRElement element, IconDrawing icon,
            double width, double height,
            string label, string tooltip);
    }

    //? TODO: Move to application settings
    public class ProfileDocumentMarkerOptions {
        public enum ValueUnitKind {
            Nanosecond,
            Millisecond,
            Second,
            Percent,
            Value
        }

        public double VirtualColumnPosition { get; set; }
        public double ElementWeightCutoff { get; set; }
        public double LineWeightCutoff {  get; set; }
        public int TopOrderCutoff { get; set; }
        public double IconBarWeightCutoff { get; set; }
        
        public Brush ColumnTextColor { get; set; }
        public Brush ElementOverlayTextColor { get; set; }
        public Brush HotElementOverlayTextColor { get; set; }
        public Brush InlineeOverlayTextColor { get; set; }
        public Brush BlockOverlayTextColor { get; set; }
        public Brush HotBlockOverlayTextColor { get; set; }
        public Brush InlineeOverlayBackColor { get; set; }
        public Brush ElementOverlayBackColor { get; set; }
        public Brush HotElementOverlayBackColor { get; set; }
        public Brush BlockOverlayBackColor { get; set; }
        public Brush HotBlockOverlayBackColor { get; set; }
        public Brush BlockOverlayBorderColor { get; set; }
        public double BlockOverlayBorderThickness { get; set; }
        public Brush PercentageBarBackColor { get; set; }
        public int MaxPercentageBarWidth { get; set; }
        public bool DisplayPercentageBar { get; set; }
        public bool DisplayIcons { get; set; }
        public bool RemoveEmptyColumns { get; set; }
        public ValueUnitKind ValueUnit { get; set; }

        private static ProfileDocumentMarkerOptions defaultInstance_;
        private static ColorPalette defaultBackColorPalette_ = ColorPalette.Profile;

        static ProfileDocumentMarkerOptions() {
            defaultInstance_ = new ProfileDocumentMarkerOptions() {
                VirtualColumnPosition = 350,
                ElementWeightCutoff = 0.003, // 0.3%
                LineWeightCutoff = 0.005, // 0.5%,
                TopOrderCutoff = 10,
                IconBarWeightCutoff = 0.03,
                MaxPercentageBarWidth = 50,
                DisplayIcons = true,
                RemoveEmptyColumns = true,
                DisplayPercentageBar = true,
                ColumnTextColor = Brushes.Black,
                ElementOverlayTextColor = Brushes.DimGray,
                HotElementOverlayTextColor = Brushes.DarkRed,
                ElementOverlayBackColor = Brushes.Transparent,
                HotElementOverlayBackColor = Brushes.AntiqueWhite,
                BlockOverlayTextColor = Brushes.DarkBlue,
                HotBlockOverlayTextColor = Brushes.DarkRed,
                BlockOverlayBackColor = Brushes.AliceBlue,
                BlockOverlayBorderColor = Brushes.DimGray,
                BlockOverlayBorderThickness = 1,
                HotBlockOverlayBackColor = Brushes.AntiqueWhite,
                InlineeOverlayTextColor = Brushes.Green,
                InlineeOverlayBackColor = Brushes.Transparent,
                PercentageBarBackColor = Utils.ColorFromString("#Aa4343").AsBrush()
            };
        }

        public static ProfileDocumentMarkerOptions Default => defaultInstance_;

        public Color PickBackColor(OptionalColumn column, int colorIndex, double percentage) {
            if (!ShouldUseBackColor(column)) {
                return Colors.Transparent;
            }

            //? TODO: ShouldUsePalette, ColorPalette in Appearance
            return column.Appearance.PickColorForPercentage
                ? PickBackColorForPercentage(column, percentage)
                : PickBackColorForOrder(column, colorIndex, percentage, InvertColorPalette(column));
        }

        private ColorPalette PickColorPalette(OptionalColumn column) {
            return column?.Appearance?.BackColorPalette ?? defaultBackColorPalette_;
        }

        private bool InvertColorPalette(OptionalColumn column) {
            return column != null && column.Appearance.InvertColorPalette;
        }

        public Color PickBackColorForPercentage(OptionalColumn column, double percentage) {
            if (percentage < ElementWeightCutoff) {
                return Colors.Transparent;
            }

            var palette = PickColorPalette(column);
            return palette.PickColorForPercentage(percentage, InvertColorPalette(column));
        }

        public Color PickBackColorForOrder(OptionalColumn column, int order, double percentage, bool inverted) {
            if (!IsSignificantValue(order, percentage)) {
                return Colors.Transparent;
            }

            var palette = PickColorPalette(column);
            return palette.PickColor(order, inverted);
        }

        public bool IsSignificantValue(int order, double percentage) {
            return order < TopOrderCutoff && percentage >= IconBarWeightCutoff;
        }

        public Color PickBackColorForOrder(int order, double percentage, bool inverted) {
            return PickBackColorForOrder(null, order, percentage, inverted);
        }

        public Brush PickTextColor(OptionalColumn column, int order, double percentage) {
            return column.Appearance.TextColor ?? ColumnTextColor;
        }

        public FontWeight PickTextWeight(OptionalColumn column, int order, double percentage) {
            if (column.Appearance.PickColorForPercentage) {
                return percentage switch {
                    >= 0.9 => FontWeights.Bold,
                    >= 0.75 => FontWeights.Medium,
                    _ => FontWeights.Normal
                };
            }

            return order switch {
                0 => FontWeights.ExtraBold,
                1 => FontWeights.Bold,
                _ => IsSignificantValue(order, percentage)
                    ? FontWeights.Medium 
                    : FontWeights.Normal
            };
        }

        public FontWeight PickTextWeight(double percentage) {
            return percentage switch {
                >= 0.9 => FontWeights.Bold,
                >= 0.75 => FontWeights.Medium,
                _ => FontWeights.Normal
            };
        }

        public Brush PickBrushForPercentage(double weightPercentage) {
            return PickBackColorForPercentage(null, weightPercentage).AsBrush();
        }

        //? TODO: Cache IconDrawing between calls
        public IconDrawing PickIcon(OptionalColumn column, int order, double percentage) {
            if (!ShouldShowIcon(column)) {
                return IconDrawing.Empty;
            }
            else if (column.Appearance.PickColorForPercentage) {
                return PickIconForPercentage(percentage);
            }

            return PickIconForOrder(order, percentage);
        }

        public IconDrawing PickIconForOrder(int order, double percentage) {
            return order switch {
                0 => IconDrawing.FromIconResource("HotFlameIcon1"),
                1 => IconDrawing.FromIconResource("HotFlameIcon2"),
                // Even if instr is the n-th hottest one, don't use an icon
                // if the percentage is small.
                _ => (IsSignificantValue(order, percentage)) ?
                    IconDrawing.FromIconResource("HotFlameIcon3") :
                    IconDrawing.FromIconResource("HotFlameIconTransparent")
            };
        }

        public IconDrawing PickIconForPercentage(double percentage) {
            return percentage switch {
                >= 0.9 => IconDrawing.FromIconResource("HotFlameIcon1"),
                >= 0.75 => IconDrawing.FromIconResource("HotFlameIcon2"),
                >= 0.5 => IconDrawing.FromIconResource("HotFlameIcon3"),
                _ => IconDrawing.FromIconResource("HotFlameIconTransparent")
            };
        }

        public bool ShowPercentageBar(OptionalColumn column, int order, double percentage) {
            if (!ShouldShowPercentageBar(column)) {
                return false;
            }

            // Don't use a bar if it ends up only a few pixels.
            return percentage >= IconBarWeightCutoff;
        }

        public bool ShowPercentageBar(double percentage) {
            if (!DisplayPercentageBar) {
                return false;
            }

            // Don't use a bar if it ends up only a few pixels.
            return percentage >= IconBarWeightCutoff;
        }

        public Brush PickPercentageBarColor(OptionalColumn column) {
            return column.Appearance.PercentageBarBackColor ?? PercentageBarBackColor;
        }

        public bool ShouldShowPercentageBar(OptionalColumn column) =>
            DisplayPercentageBar &&
            (column.Appearance.ShowPercentageBar ||
            (column.IsMainColumn && column.Appearance. ShowMainColumnPercentageBar));
        public bool ShouldShowIcon(OptionalColumn column) =>
            DisplayIcons && (column.Appearance.ShowIcon ||
            (column.IsMainColumn && column.Appearance.ShowMainColumnIcon));
        public bool ShouldUseBackColor(OptionalColumn column) =>
            column.Appearance.UseBackColor ||
            (column.IsMainColumn && column.Appearance.UseMainColumnBackColor);

        public Color PickColorForPercentage(double percentage) {
            return PickBackColorForPercentage(null, percentage);
        }
    }

    public class ProfileDocumentMarker {
        private FunctionProfileData profile_;
        private ProfileData globalProfile_;
        private ProfileDocumentMarkerOptions options_;
        private ICompilerIRInfo ir_;

        public ProfileDocumentMarker(FunctionProfileData profile, ProfileData globalProfile, 
                                     ProfileDocumentMarkerOptions options, ICompilerIRInfo ir) {
            profile_ = profile;
            globalProfile_ = globalProfile;
            options_ = options;
            ir_ = ir;
        }

        public async Task<IRDocumentColumnData> Mark(MarkedDocument document, FunctionIR function, IRTextFunction textFunction) {
            document.SuspendUpdate();
            IRDocumentColumnData columnData = null;
            var metadataTag = function.GetTag<AssemblyMetadataTag>();
            bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

            //? TODO: Make async
            if (hasInstrOffsetMetadata) {
                var result = profile_.Process(function, ir_);
                columnData = MarkProfiledElements(result, function, document);
                MarkProfiledBlocks(result.BlockSampledElements, document);
                MarkCallSites(document, function, textFunction, metadataTag);
            }

            document.ResumeUpdate();
            return columnData;
        }

        private void MarkCallSites(MarkedDocument document, FunctionIR function, IRTextFunction textFunction, AssemblyMetadataTag metadataTag) {
            var callTree = globalProfile_.CallTree;

            if (callTree == null) {
                return;
            }

            var icon = IconDrawing.FromIconResource("ExecuteIconColor");
            var node = callTree.GetCombinedCallTreeNode(textFunction);

            if (node == null || !node.HasCallSites) {
                return;
            }

            foreach (var callsite in node.CallSites.Values) {
                if (FunctionProfileData.TryFindElementForOffset(metadataTag, callsite.RVA- profile_.FunctionDebugInfo.RVA, ir_, out var element)) {
                    //Trace.WriteLine($"Found CS for elem at RVA {callsite.RVA}, weight {callsite.Weight}: {element}");
                    var instr = element as InstructionIR;
                    if (instr == null || !ir_.IsCallInstruction(instr)) continue;

                    // Skip over direct calls.
                    var callTarget = ir_.GetCallTarget(instr);

                    if (callTarget != null && callTarget.HasName) {
                        continue;
                    }

                    var sb = new StringBuilder();
                    int index = 0;

                    foreach (var target in callsite.SortedTargets) {
                        if (++index > 1) {
                            sb.AppendLine();
                        }

                        var targetNode = callTree.FindNode(target.NodeId);
                        double weightPercentage = callsite.ScaleWeight(target.Weight);
                        sb.Append($"{weightPercentage.AsPercentageString().PadLeft(6)} | {target.Weight.AsMillisecondsString()} | {targetNode.FunctionName}");
                    }

                    var label = $"Indirect call targets:\n{sb}";
                    var overlay = document.RegisterIconElementOverlay(element, icon, 16, 16, label, "");

                    Color color = App.Settings.DocumentSettings.BackgroundColor;

                    if (instr.ParentBlock != null && !instr.ParentBlock.HasEvenIndexInFunction) {
                        color = App.Settings.DocumentSettings.AlternateBackgroundColor;
                    }

                    overlay.Background = color.AsBrush();
                    //overlay.Border = blockPen;
                    overlay.IsLabelPinned = false;
                    overlay.UseLabelBackground = true;
                    overlay.ShowBackgroundOnMouseOverOnly = true;
                    overlay.ShowBorderOnMouseOverOnly = true;
                    overlay.AlignmentX = HorizontalAlignment.Left;

                    // Place before the opcode.
                    int lineOffset = lineOffset = instr.OpcodeLocation.Offset - instr.TextLocation.Offset;
                    overlay.MarginX = Utils.MeasureString(lineOffset, App.Settings.DocumentSettings.FontName,
                                                          App.Settings.DocumentSettings.FontSize).Width - 16 - 4;
                    overlay.MarginY = 1;
                }
            }
        }

        public async Task<IRDocumentColumnData> MarkSourceLines(MarkedDocument document, FunctionIR function, 
                                                                FunctionProfileData.ProcessingResult result) {
            return MarkProfiledElements(result, function, document);
        }

        private void MarkProfiledBlocks(List<Tuple<BlockIR, TimeSpan>> blockWeights, MarkedDocument document) {
            document.SuspendUpdate();
            var overlayHeight = document.DefaultLineHeight;
            var blockPen = ColorPens.GetPen(options_.BlockOverlayBorderColor,
                                            options_.BlockOverlayBorderThickness);

            for(int i = 0; i < blockWeights.Count; i++) {
                var block = blockWeights[i].Item1;
                var weight = blockWeights[i].Item2;
                double weightPercentage = profile_.ScaleWeight(weight);

                var icon = options_.PickIconForOrder(i, weightPercentage);
                var color = options_.PickBackColorForOrder(i, weightPercentage, true);

                if (color == Colors.Transparent) {
                    // 
                    color = block.HasEvenIndexInFunction ?
                        App.Settings.DocumentSettings.BackgroundColor :
                        App.Settings.DocumentSettings.AlternateBackgroundColor;
                }

                bool markOnFlowGraph = options_.IsSignificantValue(i, weightPercentage);
                var label = $"{weightPercentage.AsTrimmedPercentageString()}";
                var overlay = document.RegisterIconElementOverlay(block, icon, 0, overlayHeight, label, "");
                overlay.Background = color.AsBrush();
                overlay.Border = blockPen;

                overlay.IsLabelPinned = true;
                overlay.UseLabelBackground = true;
                overlay.ShowBackgroundOnMouseOverOnly = false;
                overlay.ShowBorderOnMouseOverOnly = false;
                overlay.AlignmentX = HorizontalAlignment.Left;
                overlay.MarginX = -1;
                overlay.Padding = 2;

                if (markOnFlowGraph) {
                    overlay.TextColor = options_.HotBlockOverlayTextColor;
                    overlay.TextWeight = FontWeights.Bold;
                }
                else {
                    overlay.TextColor = options_.BlockOverlayTextColor;
                }

                // Mark the block itself with a color.
                document.MarkBlock(block, color, markOnFlowGraph);

                if (weightPercentage > options_.ElementWeightCutoff) {
                    block.AddTag(GraphNodeTag.MakeColor(weightPercentage.AsTrimmedPercentageString(), color));
                }
            }
        }

        private static readonly OptionalColumn TIME_COLUMN = 
            OptionalColumn.Template("[TimeHeader]", "TimePercentageColumnValueTemplate",
                                    "TimeHeader", "Time (ms)", "Instruction time", null, 50.0, "TimeColumnHeaderTemplate",
            new OptionalColumnAppearance() {
                ShowPercentageBar = false,
                ShowMainColumnPercentageBar = false,
                UseBackColor = true,
                UseMainColumnBackColor = true,
                ShowMainColumnIcon = true,
                BackColorPalette = ColorPalette.Profile,
                InvertColorPalette = true
            });

        private static readonly OptionalColumn TIME_PERCENTAGE_COLUMN = 
            OptionalColumn.Template("[TimePercentageHeader]", "TimePercentageColumnValueTemplate",
            "TimePercentageHeader", "Time (%)", "Instruction time percentage relative to function time", null, 50.0, "TimeColumnHeaderTemplate",
            new OptionalColumnAppearance() {
                ShowPercentageBar = true,
                ShowMainColumnPercentageBar = true, 
                UseBackColor = true,
                UseMainColumnBackColor = true,
                ShowIcon = true,
                BackColorPalette = ColorPalette.Profile,
                InvertColorPalette = true
            });
        
        public void ApplyColumnStyle(OptionalColumn column, IRDocumentColumnData columnData,
                                     FunctionIR function, MarkedDocument document) {
            Trace.WriteLine($"Apply {column.ColumnName}, main {column.IsMainColumn}");

            var style = column.Appearance;
            var elementColorPairs = new List<ValueTuple<IRElement, Color>>(function.TupleCount);

            foreach (var tuple in function.AllTuples) {
                var value = columnData.GetColumnValue(tuple, column);
                if(value == null) continue;

                int order = value.ValueOrder;
                double percentage = value.ValuePercentage;
                var color = options_.PickBackColor(column, order, percentage);

                if (column.IsMainColumn) {
                    elementColorPairs.Add(new ValueTuple<IRElement, Color>(tuple, color));
                }

                value.BackColor = color.AsBrush();
                value.TextColor = options_.PickTextColor(column, order, percentage);
                value.TextWeight = options_.PickTextWeight(column, order, percentage);

                value.Icon = options_.PickIcon(column, value.ValueOrder, value.ValuePercentage).Icon;
                value.ShowPercentageBar = value.ShowPercentageBar && // Disabled per value
                                          options_.ShowPercentageBar(column, value.ValueOrder, value.ValuePercentage);
                value.PercentageBarBackColor = options_.PickPercentageBarColor(column);
            }

            // Mark the elements themselves with a color.
            //? option in appearance
            //? Needs a tag so later a RemoveMarkedElements(tag) can be done
            if (column.IsMainColumn && document != null) {
                document.ClearInstructionMarkers();
                document.MarkElements(elementColorPairs);
            }
        }

        struct CounterSortHelper {
            public ElementColumnValue ColumnValue;
            public long Value;

            public CounterSortHelper(ElementColumnValue columnValue, long value) {
                ColumnValue = columnValue;
                Value = value;
            }
        }

        private IRDocumentColumnData 
            MarkProfiledElements(FunctionProfileData.ProcessingResult result, 
                                 FunctionIR function, MarkedDocument document) {
            var elements = result.SampledElements;

            // Add a time column.
            var columnData = new IRDocumentColumnData(function.InstructionCount);
            var percentageColumn = columnData.AddColumn(TIME_PERCENTAGE_COLUMN);
            var timeColumn = columnData.AddColumn(TIME_COLUMN);

            for (int i = 0; i < elements.Count; i++) {
                var element = elements[i].Item1;
                var weight = elements[i].Item2;
                double weightPercentage = profile_.ScaleWeight(weight);

                var label = weight.AsMillisecondsString();
                var percentageLabel = weightPercentage.AsTrimmedPercentageString();
                var columnValue = new ElementColumnValue(label, weight.Ticks, weightPercentage, i);
                var percentageColumnValue = new ElementColumnValue(percentageLabel, weight.Ticks, weightPercentage, i);
                
                columnData.AddValue(percentageColumnValue, element, percentageColumn);
                var valueGroup = columnData.AddValue(columnValue, element, timeColumn);
                //valueGroup.BackColor = Brushes.Bisque;
            }

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

            //? Way to set a counter as a baseline, another diff to it in %
            //?    misspredictedBranches / totalBranches
            //?    takenBranches / total, etc JSON

            var perfCounters = globalProfile_.SortedPerformanceCounters;
            var colors = new Brush[] { Brushes.DarkSlateBlue, Brushes.DarkOliveGreen, Brushes.DarkSlateGray };
            var counterIcon = IconDrawing.FromIconResource("QueryIcon");
            var counterColumns = new OptionalColumn[perfCounters.Count];

            // Add a column for each counter.
            for (int k = 0; k < perfCounters.Count; k++) {
                var counterInfo = perfCounters[k];
                counterColumns[k] = OptionalColumn.Template($"[CounterHeader{counterInfo.Id}]", "TimePercentageColumnValueTemplate",
                    $"CounterHeader{counterInfo.Id}", $"{ShortenPerfCounterName(counterInfo.Name)}",
                    counterInfo?.Config?.Description != null ? $"{counterInfo.Config.Description}" : $"{counterInfo.Name}",
                    null, 50, "TimeColumnHeaderTemplate",
                    new OptionalColumnAppearance() {
                        ShowPercentageBar = true,
                        ShowMainColumnPercentageBar = true,
                        UseBackColor = counterInfo.IsMetric,
                        UseMainColumnBackColor = true,
                        PickColorForPercentage = false,
                        ShowIcon = false,
                        ShowMainColumnIcon = true,
                        BackColorPalette = ColorPalette.Profile,
                        InvertColorPalette = true,
                        TextColor = ColorPalette.DarkHue.PickBrush(k),
                        PercentageBarBackColor = ColorPalette.DarkHue.PickBrush(k)
                    });

                counterColumns[k].IsVisible = IsPerfCounterVisible(counterInfo);
                counterColumns[k].HeaderClickHandler += ColumnHeaderClickHandler(document, function, columnData);
                columnData.AddColumn(counterColumns[k]);
            }

            // build lists
            // sort lists by value (parallel)
            // go over lists and assign ValueOrder

            var counterSortMap = new List<List<CounterSortHelper>>();

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
                        var metric = counter as PerformanceMetricInfo;
                        valuePercentage = metric.ComputeMetric(counterSet, out var baseValue, out var relativeValue);

                        // Don't show metrics for counters with few hits,
                        // they tend to be the ones the most inaccurate.
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
                    //columnValue.TextColor = color;
                    if (counter.IsMetric) columnValue.BackColor = Brushes.Beige;
                    columnValue.ValuePercentage = valuePercentage;
                    //? TODO: SHow bar only if any value is much higher? Std dev
                    columnValue.ShowPercentageBar = !isValueBasedMetric && valuePercentage >= 0.03;
                    columnValue.PercentageBarBackColor = color;
                    columnData.AddValue(columnValue, element, counterColumns[k]);

                    var counterValueList = counterSortMap[k];
                    counterValueList.Add(new CounterSortHelper(columnValue, value));
                }
            }

            // Sort the counters from each column in decreasing order,
            // then assign the ValueOrder for each counter based on the sorting index.
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

        public static bool IsPerfCounterVisible(PerformanceCounterInfo counterInfo) {
            //? TODO: Use a filter list from options
            return counterInfo.Name != "Timer";
        }

        public static string FormatPerformanceMetric(double value, PerformanceMetricInfo metric) {
            if (value == 0) {
                return "";
            }

            return metric.Config.IsPercentage ? value.AsPercentageString() : $"{value:F2}";
        }

        public static string FormatPerformanceCounter(long value, PerformanceCounterInfo counter) {
            if (counter.Frequency > 1000) {
                double valueK = (double)(value * counter.Frequency) / 1000;
                return $"{valueK:##}K";
            }
            else {
                return $"{value * counter.Frequency}";
            }
        }

        static readonly (string,string)[] PerfCounterNameReplacements = new (string, string)[] {
            ("Instruction", "Instr"),
            ("Misprediction", "Mispred"),
        };

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
    }
}