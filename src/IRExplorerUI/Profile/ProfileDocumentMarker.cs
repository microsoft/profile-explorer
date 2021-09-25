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
using System.Windows;
using System.Windows.Media;
using IRExplorerUI.Profile;
using IRExplorerCore.IR.Tags;
using IRExplorerUI.Document;
using System.Windows.Documents;
using System.IO;

namespace IRExplorerUI.Compilers.ASM {
    //? TODO: Move to application settings
    public class ProfileDocumentMarkerOptions {
        public double VirtualColumnPosition { get; set; }
        public double ElementWeightCutoff { get; set; }
        public double LineWeightCutoff {  get; set; }
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
        public List<Color> ColorPalette { get; set; }

        private static ProfileDocumentMarkerOptions defaultInstance_;

        static ProfileDocumentMarkerOptions() {
            defaultInstance_ = new ProfileDocumentMarkerOptions() {
                VirtualColumnPosition = 450,
                ElementWeightCutoff = 0.003, // 0.3%
                LineWeightCutoff = 0.005, // 0.5%,
                ElementOverlayTextColor = Brushes.Black,
                HotElementOverlayTextColor = Brushes.DarkRed,
                ElementOverlayBackColor = Brushes.Transparent,
                HotElementOverlayBackColor = Brushes.AntiqueWhite,
                BlockOverlayTextColor = Brushes.DarkBlue,
                HotBlockOverlayTextColor = Brushes.DarkRed,
                BlockOverlayBackColor = Brushes.AliceBlue,
                HotBlockOverlayBackColor = Brushes.AntiqueWhite,
                InlineeOverlayTextColor = Brushes.DarkGreen,
                InlineeOverlayBackColor = Brushes.Transparent,
                ColorPalette = ColorUtils.MakeColorPallete(1, 1, 0.80f, 0.95f, 10) // 10 steps, red
            };
        }

        public static ProfileDocumentMarkerOptions Default => defaultInstance_;

        public Color PickColorForWeight(double weightPercentage) {
            int colorIndex = (int)Math.Floor(ColorPalette.Count * (1.0 - weightPercentage));
            colorIndex = Math.Clamp(colorIndex, 0, ColorPalette.Count - 1);
            return ColorPalette[colorIndex];
        }

        public Brush PickBrushForWeight(double weightPercentage) {
            return ColorBrushes.GetBrush(PickColorForWeight(weightPercentage));
        }
    }

    public class ProfileDocumentMarker {
        private FunctionProfileData profile_;
        private ProfileData globalProfile_;
        private ProfileDocumentMarkerOptions options_;
        private ICompilerIRInfo ir_;

        public double MaxVirtualColumn { get; set; }

        public ProfileDocumentMarker(FunctionProfileData profile, ProfileData globalProfile, ProfileDocumentMarkerOptions options, ICompilerIRInfo ir) {
            profile_ = profile;
            globalProfile_ = globalProfile;
            options_ = options;
            ir_ = ir;
        }

        public void Mark(IRDocument document, FunctionIR function) {
            document.SuspendUpdate();

            var metadataTag = function.GetTag<AssemblyMetadataTag>();
            bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

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

            for(int i = 0; i < blockWeights.Count; i++) {
                var element = blockWeights[i].Item1;
                var weight = blockWeights[i].Item2;
                double weightPercentage = profile_.ScaleWeight(weight);

                //? TODO: Configurable
                IconDrawing icon = null;
                bool markOnFlowGraph = false;

                if (i == 0) {
                    icon = IconDrawing.FromIconResource("DotIconRed");
                    markOnFlowGraph = true;
                }
                else if (i <= 2) {
                    icon = IconDrawing.FromIconResource("DotIconYellow");
                    markOnFlowGraph = true;
                }
                else {
                    icon = IconDrawing.FromIconResource("DotIcon");
                }

                var label = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(weight.TotalMilliseconds, 2):#,#} ms)";
                var overlay = document.RegisterIcomElementOverlay(element, icon, 16, 0, label);
                overlay.IsLabelPinned = true;
                overlay.UseLabelBackground = true;
                overlay.ShowBackgroundOnMouseOverOnly = false;
                overlay.AlignmentX = HorizontalAlignment.Left;
                overlay.MarginX = 48;

                if (i <= 2) {
                    overlay.TextColor = options_.HotBlockOverlayTextColor;
                    overlay.TextWeight = FontWeights.Bold;
                    overlay.Background = options_.HotBlockOverlayBackColor;
                }
                else {
                    overlay.TextColor = options_.BlockOverlayTextColor;
                    overlay.Background = options_.BlockOverlayBackColor;
                }

                // Mark the block itself with a color.
                document.MarkBlock(element, options_.PickColorForWeight(weightPercentage), markOnFlowGraph);

                if (weightPercentage > options_.ElementWeightCutoff) {
                    element.AddTag(GraphNodeTag.MakeLabel($"{Math.Round(weightPercentage * 100, 2)}%"));
                }
            }
        }

        private static readonly OptionalColumn TIME_COLUMN = OptionalColumn.Template("Values[TimeHeader]", "TimeColumnValueTemplate",
            "TimeHeader", "Time (ms)", "Instruction time");

        private static readonly OptionalColumn TIME_PERCENTAGE_COLUMN = OptionalColumn.Template("Values[TimePercentageHeader]", "TimePercentageColumnValueTemplate",
            "TimePercentageHeader", "Time (%)", "Instruction time percentage relative to function time");

        private void MarkProfiledElements(FunctionProfileData.ProcessingResult result, IRDocument document) {
            var elements = result.SampledElements;
            var elementColorPairs = new List<Tuple<IRElement, Color>>(elements.Count);
            double virtualColumnAdjustment = ir_.Mode == IRMode.x86_64 ? 100 : 0;

            // Add a time column.
            var columnData = document.ColumnData;
            columnData.Columns.Add(TIME_PERCENTAGE_COLUMN);
            columnData.Columns.Add(TIME_COLUMN);

            for (int i = 0; i < elements.Count; i++) {
                var element = elements[i].Item1;
                var weight = elements[i].Item2;
                double weightPercentage = profile_.ScaleWeight(weight);
                Color color;

                if (weightPercentage < options_.ElementWeightCutoff) {
                    color = Colors.Transparent;
                }
                else {
                    color = options_.PickColorForWeight(weightPercentage);
                }

                elementColorPairs.Add(new Tuple<IRElement, Color>(element, color));

                var label = $"{Math.Round(weight.TotalMilliseconds, 2):#,#} ms";
                var percentageLabel = $"{Math.Round(weightPercentage * 100, 2)}%";
                var columnValue = new ElementColumnValue(label);
                var percentageColumnValue = new ElementColumnValue(percentageLabel);

                //? TODO: Split into time % and time MS
                //? - time % has the bar, on the right, with % format as 00.00%
                //? - make color pallete lie uprof yellow/red

                if (i <= 8) {
                    IconDrawing icon = null;

                    if (i == 0) {
                        icon = IconDrawing.FromIconResource("DotIconRed");
                    }
                    else if (i <= 10) {
                        icon = IconDrawing.FromIconResource("DotIconYellow");
                    }

                    percentageColumnValue.TextColor = options_.ElementOverlayTextColor;
                    percentageColumnValue.BackColor = ColorBrushes.GetBrush(color);
                    //percentageColumnValue.BorderBrush = ColorBrushes.GetBrush(color);
                    //percentageColumnValue.BorderThickness = new Thickness(1);
                    percentageColumnValue.TextWeight = FontWeights.Bold;
                    percentageColumnValue.Icon = icon.Icon;
                    percentageColumnValue.Percentage = weightPercentage;
                    percentageColumnValue.PercentageBarBackColor = Brushes.Brown;
                    percentageColumnValue.ShowPercentageBar = true;
                    //percentageColumnValue.PrefixText = $"{i + 1}";

                    columnValue.TextColor = i <= 3 ? options_.HotElementOverlayTextColor : options_.ElementOverlayTextColor;
                    columnValue.TextWeight = FontWeights.Bold;
                }
                else {
                    percentageColumnValue.TextColor = options_.ElementOverlayTextColor;
                    percentageColumnValue.BackColor = options_.ElementOverlayBackColor;
                    percentageColumnValue.Percentage = weightPercentage;
                    percentageColumnValue.ShowPercentageBar = weightPercentage >= 0.02;
                    percentageColumnValue.PercentageBarBackColor = Brushes.Brown;
                    percentageColumnValue.PrefixText = " ";
                    //percentageColumnValue.Icon = IconDrawing.FromIconResource("DotIconTransparent").Icon;

                    columnValue.TextColor = options_.ElementOverlayTextColor;
                    columnValue.BackColor = options_.ElementOverlayBackColor;
                }

                columnData.AddValue(percentageColumnValue, element, TIME_PERCENTAGE_COLUMN);
                var valueGroup = columnData.AddValue(columnValue, element, TIME_COLUMN);
                valueGroup.BackColor = Brushes.Bisque;
            }

            // Mark the elements themselves with a color.
            document.MarkElements(elementColorPairs);

            var counterElements = result.CounterElements;

            if (counterElements.Count == 0) {
                return;
            }


            //? TODO: Filter to hide counters
            //? TODO: Order of counters (custom sorting or fixed)

            //? Max virt column must be for the entire document , not per line
            //? Way to set a counter as a baseline, another diff to it in %
            //?    misspredictedBranches / totalBranches
            //?    takenBranches / total, etc JSON

            var perfCounters = globalProfile_.SortedPerformanceCounters;
            var colors = new Brush[] { Brushes.DarkTurquoise, Brushes.DarkOliveGreen, Brushes.DarkViolet };
            var counterIcon = IconDrawing.FromIconResource("QueryIcon");

            // Add a column for each of the counters.

            for (int i = 0; i < counterElements.Count; i++) {
                var element = counterElements[i].Item1;
                var counterSet = counterElements[i].Item2;

                for (int k = 0; k < perfCounters.Count; k++) {
                    var counterInfo = perfCounters[k];
                    var value = counterSet.FindCounterSamples(counterInfo.Id);

                    if (value == 0) {
                        continue;
                    }

                    var label = $"{value * counterInfo.Frequency}";
                    var tooltip = counterInfo?.Name;
                    var overlay = document.RegisterIcomElementOverlay(element, counterIcon, 16, 0, label);

                    overlay.IsLabelPinned = true;
                    overlay.VirtualColumn = options_.VirtualColumnPosition + virtualColumnAdjustment;
                    MaxVirtualColumn = Math.Max(overlay.VirtualColumn, MaxVirtualColumn);

                    overlay.ToolTip = $"{counterInfo.Name} ({counterInfo.Id})";
                    overlay.VirtualColumn += k * 100;
                    overlay.TextColor = colors[counterInfo.Number % colors.Length];
                }

            }
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

                var color = options_.PickColorForWeight(weightPercentage);
                document.MarkElementsOnSourceLine(pair.Item1, color);
            }

            document.ResumeUpdate();
        }
    }
}
