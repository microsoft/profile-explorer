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
using System.Text;
using System.Windows;
using System.Windows.Media;
using IRExplorerUI.Profile;
using IRExplorerCore.IR.Tags;

namespace IRExplorerUI.Compilers.ASM {
    public class ProfileDocumentMarkerOptions {
        public double VirtualColumnPosition { get; set; }
        public double ElementWeightCutoff { get; set; }
        public double LineWeightCutoff {  get; set; }
        public Brush ElementOverlayTextColor { get; set; }
        public Brush HotElementOverlayTextColor { get; set; }
        public Brush BlockOverlayTextColor { get; set; }
        public Brush HotBlockOverlayTextColor { get; set; }
        public Brush ElementOverlayBackColor { get; set; }
        public Brush HotElementOverlayBackColor { get; set; }
        public Brush BlockOverlayBackColor { get; set; }
        public Brush HotBlockOverlayBackColor { get; set; }
        public List<Color> ColorPalette { get; set; }

        public static ProfileDocumentMarkerOptions Default() {
            return new ProfileDocumentMarkerOptions() {
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
                ColorPalette = ColorUtils.MakeColorPallete(1, 1, 0.80f, 0.95f, 10) // 10 steps, red
            };
        }

        public Color PickColorForWeight(double weightPercentage) {
            int colorIndex = (int)Math.Floor(ColorPalette.Count * (1.0 - weightPercentage));
            colorIndex = Math.Clamp(colorIndex, 0, ColorPalette.Count - 1);
            return ColorPalette[colorIndex];
        }
    }

    public class ProfileDocumentMarker {
        private ProfileDocumentMarkerOptions options_;
        private ICompilerIRInfo ir_;
        
        public ProfileDocumentMarker(ProfileDocumentMarkerOptions options, ICompilerIRInfo ir) {
            options_ = options;
            ir_ = ir;
        }

        public void Mark(IRDocument document, FunctionProfileData profile, FunctionIR function) {
            var metadataTag = function.GetTag<AssemblyMetadataTag>();
            bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

            if (hasInstrOffsetMetadata) {
                var (elementWeights, blockWeights) = CollectProfiledElements(profile, metadataTag);

                MarkProfiledElements(elementWeights, profile, document);
                MarkProfiledBlocks(blockWeights, profile, document);
            }

            if (!hasInstrOffsetMetadata) {
                MarkProfiledLines(profile, document);
            }
        }

        private (List<Tuple<IRElement, TimeSpan>>,
                 List<Tuple<BlockIR, TimeSpan>>)
            CollectProfiledElements(FunctionProfileData profile, AssemblyMetadataTag metadataTag) {
            var elements = new List<Tuple<IRElement, TimeSpan>>(profile.InstructionWeight.Count);
            var blockWeightMap = new Dictionary<BlockIR, TimeSpan>();

            foreach (var pair in profile.InstructionWeight) {
                if (TryFindElementForOffset(metadataTag, pair.Key, out var element)) {
                    elements.Add(new Tuple<IRElement, TimeSpan>(element, pair.Value));

                    if (blockWeightMap.TryGetValue(element.ParentBlock, out var currentWeight)) {
                        blockWeightMap[element.ParentBlock] = currentWeight + pair.Value;
                    }
                    else {
                        blockWeightMap[element.ParentBlock] = pair.Value;
                    }
                }
            }

            var blockWeights = blockWeightMap.ToList();
            blockWeights.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            elements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return (elements, blockWeights);
        }

        private void MarkProfiledBlocks(List<Tuple<BlockIR, TimeSpan>> blockWeights, 
                                        FunctionProfileData profile, IRDocument document) {
            document.SuspendUpdate();
            var blockOverlays = new List<Tuple<IRElement, IconDrawing, string>>(blockWeights.Count);

            for(int i = 0; i < blockWeights.Count; i++) {
                var element = blockWeights[i].Item1;
                var weight = blockWeights[i].Item2;
                double weightPercentage = profile.ScaleWeight(weight);

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

                var tooltip = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(weight.TotalMilliseconds, 2)} ms)";
                blockOverlays.Add(new Tuple<IRElement, IconDrawing, string>(element, icon, tooltip));
                document.MarkBlock(element, options_.PickColorForWeight(weightPercentage), markOnFlowGraph);

                if (weightPercentage > options_.ElementWeightCutoff) {
                    element.AddTag(GraphNodeTag.MakeLabel($"{Math.Round(weightPercentage * 100, 2)}%"));
                }
            }

            var blockOverlayList = document.AddIconElementOverlays(blockOverlays);

            for(int i = 0; i < blockOverlayList.Count; i++) {
                var overlay = blockOverlayList[i];
                overlay.IsToolTipPinned = true;
                overlay.UseToolTipBackground = true;
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
            }

            document.ResumeUpdate();
        }

        private void MarkProfiledElements(List<Tuple<IRElement, TimeSpan>> elements,
                                          FunctionProfileData profile, IRDocument document) {
            var elementColorPairs = new List<Tuple<IRElement, Color>>(elements.Count);
            var elementOverlays = new List<Tuple<IRElement, IconDrawing, string>>(elements.Count);

            for(int i = 0; i < elements.Count; i++) {
                var element = elements[i].Item1;
                var weight = elements[i].Item2;
                double weightPercentage = profile.ScaleWeight(weight);
                Color color;

                if (weightPercentage < options_.ElementWeightCutoff) {
                    color = Colors.Transparent;
                }
                else {
                    color = options_.PickColorForWeight(weightPercentage);
                }

                elementColorPairs.Add(new Tuple<IRElement, Color>(element, color));

                //? TODO: Configurable
                IconDrawing icon = null;

                if (i == 0) {
                    icon = IconDrawing.FromIconResource("DotIconRed");
                }
                else if (i <= 2) {
                    icon = IconDrawing.FromIconResource("DotIconYellow");
                }

                var tooltip = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(weight.TotalMilliseconds, 2)} ms)";
                elementOverlays.Add(new Tuple<IRElement, IconDrawing, string>(element, icon, tooltip));
            }

            var elementOverlayList = document.AddIconElementOverlays(elementOverlays);

            for(int i = 0; i < elementOverlayList.Count; i++) {
                var overlay = elementOverlayList[i];
                overlay.IsToolTipPinned = true;

                if (i <= 2) {
                    overlay.TextColor = options_.HotElementOverlayTextColor;
                    overlay.UseToolTipBackground = true;
                    overlay.Background = options_.HotElementOverlayBackColor;
                    overlay.TextWeight = FontWeights.Bold;
                }
                else {
                    overlay.TextColor = options_.ElementOverlayTextColor;
                    overlay.Background = options_.ElementOverlayBackColor;
                }

                overlay.VirtualColumn = options_.VirtualColumnPosition;
            }

            document.MarkElements(elementColorPairs);
        }

        private void MarkProfiledLines(FunctionProfileData profile, IRDocument document) {
            var lines = new List<Tuple<int, TimeSpan>>(profile.SourceLineWeight.Count);

            foreach (var pair in profile.SourceLineWeight) {
                lines.Add(new Tuple<int, TimeSpan>(pair.Key, pair.Value));
            }

            lines.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            document.SuspendUpdate();

            foreach (var pair in lines) {
                double weightPercentage = profile.ScaleWeight(pair.Item2);

                if (weightPercentage < options_.LineWeightCutoff) {
                    continue;
                }

                var color = options_.PickColorForWeight(weightPercentage);
                document.MarkElementsOnSourceLine(pair.Item1, color);
            }

            document.ResumeUpdate();
        }

        private bool TryFindElementForOffset(AssemblyMetadataTag metadataTag, long offset, out IRElement element) {
            int multiplier = 1;
            var offsetData = ir_.InstructionOffsetData;

            do {
                if (metadataTag.OffsetToElementMap.TryGetValue(offset - multiplier * offsetData.OffsetAdjustIncrement, out element)) {
                    return true;
                }
                ++multiplier;
            } while (multiplier * offsetData.OffsetAdjustIncrement < offsetData.MaxOffsetAdjust);

            return false;
        }
    }
}
