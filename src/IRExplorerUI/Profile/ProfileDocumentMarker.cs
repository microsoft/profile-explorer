﻿// Copyright (c) Microsoft Corporation. All rights reserved.
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

namespace IRExplorerUI.Compilers.ASM {
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
        public double IcongBarWeightCutoff { get; set; }
        
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
                IcongBarWeightCutoff = 0.03,
                MaxPercentageBarWidth = 100,
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
            return order < TopOrderCutoff && percentage >= IcongBarWeightCutoff;
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
                    >= 0.75 => FontWeights.DemiBold,
                    _ => FontWeights.Normal
                };
            }

            return order switch {
                0 => FontWeights.ExtraBold,
                1 => FontWeights.Bold,
                _ => IsSignificantValue(order, percentage)
                    ? FontWeights.DemiBold 
                    : FontWeights.Normal
            };
        }

        public FontWeight PickTextWeight(double percentage) {
            return percentage switch {
                >= 0.9 => FontWeights.Bold,
                >= 0.75 => FontWeights.DemiBold,
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
            return percentage >= IcongBarWeightCutoff;
        }

        public bool ShowPercentageBar(double percentage) {
            if (!DisplayPercentageBar) {
                return false;
            }

            // Don't use a bar if it ends up only a few pixels.
            return percentage >= IcongBarWeightCutoff;
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

        public async Task Mark(IRDocument document, FunctionIR function) {
            document.SuspendUpdate();

            var metadataTag = function.GetTag<AssemblyMetadataTag>();
            bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

            //? TODO: Make async
            if (hasInstrOffsetMetadata) {
                var result = profile_.Process(function, ir_);
                MarkProfiledElements(result, document);
                MarkProfiledBlocks(result.BlockSampledElements, document);
            }

            // Without precise instruction info, try to derive it
            // from line number info if that's available.
            if (!hasInstrOffsetMetadata) {
                MarkProfiledLines(document);
            }

            document.ResumeUpdate();
        }

        private void MarkProfiledBlocks(List<Tuple<BlockIR, TimeSpan>> blockWeights, IRDocument document) {
            document.SuspendUpdate();
            var overlayHeight = document.TextArea.TextView.DefaultLineHeight;
            var blockPen = ColorPens.GetPen(options_.BlockOverlayBorderColor,
                                               options_.BlockOverlayBorderThickness);

            for(int i = 0; i < blockWeights.Count; i++) {
                var block = blockWeights[i].Item1;
                var weight = blockWeights[i].Item2;
                double weightPercentage = profile_.ScaleWeight(weight);

                var icon = options_.PickIconForOrder(i, weightPercentage);
                var color = options_.PickBackColorForOrder(i, weightPercentage, true);

                if (color == Colors.Transparent) {
                    color = block.HasEvenIndexInFunction ?
                        App.Settings.DocumentSettings.BackgroundColor :
                        App.Settings.DocumentSettings.AlternateBackgroundColor;
                }


                bool markOnFlowGraph = options_.IsSignificantValue(i, weightPercentage);
                var label = $"{weightPercentage.AsPercentageString()} ({weight.AsMillisecondsString()})";
                var overlay = document.RegisterIconElementOverlay(block, icon, 0, overlayHeight, label);
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
                    var graphLabel = $"{weightPercentage.AsPercentageString()} ({weight.AsMillisecondsString()})";
                    block.AddTag(GraphNodeTag.MakeLabel(weightPercentage.AsPercentageString()));
                }
            }
        }

        private static readonly OptionalColumn TIME_COLUMN = 
            OptionalColumn.Template("[TimeHeader]", "TimePercentageColumnValueTemplate",
                                    "TimeHeader", "Time (ms)", "Instruction time", null, 100.0, "TimeColumnHeaderTemplate",
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
            "TimePercentageHeader", "Time (%)", "Instruction time percentage relative to function time", null, 100.0, "TimeColumnHeaderTemplate",
            new OptionalColumnAppearance() {
                ShowPercentageBar = true,
                ShowMainColumnPercentageBar = true,
                UseBackColor = true,
                UseMainColumnBackColor = true,
                ShowIcon = true,
                BackColorPalette = ColorPalette.Profile,
                InvertColorPalette = true
            });
        
        public void ApplyColumnStyle(OptionalColumn column,
            IRDocumentColumnData columnData,
            IRDocument document) {
            Trace.WriteLine($"Apply {column.ColumnName}, main {column.IsMainColumn}");

            var function = document.Function;
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
                //value.BorderBrush = ColorBrushes.GetBrush(color);
                //value.BorderThickness = new Thickness(1);

                value.ShowPercentageBar = options_.ShowPercentageBar(column, value.ValueOrder, value.ValuePercentage);
                value.PercentageBarBackColor = options_.PickPercentageBarColor(column);
            }

            // Mark the elements themselves with a color.
            //? option in appearance
            //? Needs a tag so later a RemoveMarkedElements(tag) can be done
            if (column.IsMainColumn) {
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

        private void MarkProfiledElements(FunctionProfileData.ProcessingResult result, IRDocument document) {
            var elements = result.SampledElements;

            // Add a time column.
            var columnData = document.ColumnData;
            var percentageColumn = columnData.AddColumn(TIME_PERCENTAGE_COLUMN);
            var timeColumn = columnData.AddColumn(TIME_COLUMN);


            for (int i = 0; i < elements.Count; i++) {
                var element = elements[i].Item1;
                var weight = elements[i].Item2;
                double weightPercentage = profile_.ScaleWeight(weight);

                var label = weight.AsMillisecondsString();
                var percentageLabel = weightPercentage.AsPercentageString();
                var columnValue = new ElementColumnValue(label, weight.Ticks, weightPercentage, i);
                var percentageColumnValue = new ElementColumnValue(percentageLabel, weight.Ticks, weightPercentage, i);
                
                columnData.AddValue(percentageColumnValue, element, percentageColumn);
                var valueGroup = columnData.AddValue(columnValue, element, timeColumn);
                //valueGroup.BackColor = Brushes.Bisque;
            }

            percentageColumn.IsMainColumn = true;
            ApplyColumnStyle(percentageColumn, columnData, document);
            ApplyColumnStyle(timeColumn, columnData, document);

            percentageColumn.HeaderClickHandler += ColumnHeaderClickHandler(document, columnData);
            timeColumn.HeaderClickHandler += ColumnHeaderClickHandler(document, columnData);

            var counterElements = result.CounterElements;

            if (counterElements.Count == 0) {
                return;
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
                    counterInfo.Description != null ? $"{counterInfo.Description}" : $"{counterInfo.Name}",
                    null, 100.0, "TimeColumnHeaderTemplate",
                    new OptionalColumnAppearance() {
                        ShowPercentageBar = false,
                        ShowMainColumnPercentageBar = true,
                        UseBackColor = false,
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
                counterColumns[k].HeaderClickHandler += ColumnHeaderClickHandler(document, columnData);
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
                    var value = counterSet.FindCounterValue(counter);

                    if (value == 0) {
                        continue;
                    }

                    //? Could have a config for all/per-counter to pick % or value as label
                    double valuePercentage = result.ScaleCounterValue(value, counter);
                    var label = valuePercentage.AsPercentageString();
                    var tooltip = $"{value * counter.Frequency}";
                    var columnValue = new ElementColumnValue(label, value, valuePercentage, i, tooltip);

                    var color = colors[counter.Number % colors.Length];
                    columnValue.TextColor = color;
                    columnValue.TextWeight = FontWeights.DemiBold;
                    columnValue.ValuePercentage = valuePercentage;
                    columnValue.ShowPercentageBar = valuePercentage >= 0.03;
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
                ApplyColumnStyle(column, columnData, document);
            }
        }

        public static bool IsPerfCounterVisible(PerformanceCounterInfo counterInfo) {
            //? TODO: Use a filter list from options
            return counterInfo.Name != "Timer";
        }

        static readonly (string,string)[] PerfCounterNameReplacements = new (string, string)[] {
            ("Instructions", "Instrs"),
            ("Mispredictions", "Mispred"),
        };

        public static string ShortenPerfCounterName(string name) {
            foreach (var replacement in PerfCounterNameReplacements) {
                int index = name.LastIndexOf(replacement.Item1);

                if (index != -1) {
                    return name.Substring(0, index) + replacement.Item2;
                }
            }

            return name;
        }

        private OptionalColumnEventHandler ColumnHeaderClickHandler(IRDocument document, IRDocumentColumnData columnData) {
            return column => {
                var currentMainColumn = columnData.MainColumn;

                if (column == currentMainColumn) {
                    return;
                }

                if (currentMainColumn != null) {
                    currentMainColumn.IsMainColumn = false;
                    ApplyColumnStyle(currentMainColumn, columnData, document);
                }

                column.IsMainColumn = true;
                ApplyColumnStyle(column, columnData, document);
            };
        }

        private void MarkProfiledLines(IRDocument document) {
            var lines = new List<Tuple<int, TimeSpan>>(profile_.SourceLineWeight.Count);

            foreach (var pair in profile_.SourceLineWeight) {
                lines.Add(new Tuple<int, TimeSpan>(pair.Key, pair.Value));
            }

            lines.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            document.SuspendUpdate();

            foreach (var pair in lines) {
                double weightPercentage = profile_.ScaleWeight(pair.Item2);

                if (weightPercentage < options_.LineWeightCutoff) {
                    continue;
                }

                var color = options_.PickColorForPercentage(weightPercentage);
                document.MarkElementsOnSourceLine(pair.Item1, color);
            }

            document.ResumeUpdate();
        }
    }
}
