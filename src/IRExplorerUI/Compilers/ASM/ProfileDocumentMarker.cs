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
        public Brush BlockOverlayTextColor { get; set; }
        public Brush ElementOverlayBackColor { get; set; }
        public Brush BlockOverlayBackColor { get; set; }
        public List<Color> ColorPalette { get; set; }

        public static ProfileDocumentMarkerOptions Default() {
            return new ProfileDocumentMarkerOptions() {
                VirtualColumnPosition = 450,
                ElementWeightCutoff = 0.001, // 0.1%
                LineWeightCutoff = 0.005, // 0.5%,
                ElementOverlayTextColor = Brushes.DarkRed,
                ElementOverlayBackColor = Brushes.Transparent,
                BlockOverlayTextColor = Brushes.DarkBlue,
                BlockOverlayBackColor = Brushes.LightGoldenrodYellow,
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
            int index = 0;

            foreach (var pair in blockWeights) {
                var element = pair.Item1;
                var weight = pair.Item2;
                double weightPercentage = profile.ScaleWeight(weight);
                document.MarkBlock(element, options_.PickColorForWeight(weightPercentage));

                //? TODO: Configurable
                IconDrawing icon = null;

                if (index == 0) {
                    icon = IconDrawing.FromIconResource("DotIconRed");
                }
                else if (index <= 2) {
                    icon = IconDrawing.FromIconResource("DotIconYellow");
                }
                else {
                    icon = IconDrawing.FromIconResource("DotIcon");
                }

                var tooltip = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(weight.TotalMilliseconds, 2)} ms)";
                blockOverlays.Add(new Tuple<IRElement, IconDrawing, string>(element, icon, tooltip));
                index++;
            }

            var blockOverlayList = document.AddIconElementOverlays(blockOverlays);

            foreach (var overlay in blockOverlayList) {
                overlay.IsToolTipPinned = true;
                overlay.TextColor = options_.BlockOverlayTextColor;
                overlay.Background = options_.BlockOverlayBackColor;
                overlay.UseToolTipBackground = true;
                overlay.ShowBackgroundOnMouseOverOnly = false;
                overlay.AlignmentX = HorizontalAlignment.Left;
                overlay.MarginX = 48;
            }

            document.ResumeUpdate();
        }

        private void MarkProfiledElements(List<Tuple<IRElement, TimeSpan>> elements,
                                          FunctionProfileData profile, IRDocument document) {
            var elementColorPairs = new List<Tuple<IRElement, Color>>(elements.Count);
            var elementOverlays = new List<Tuple<IRElement, IconDrawing, string>>(elements.Count);
            int index = 0;

            foreach (var pair in elements) {
                var element = pair.Item1;
                double weightPercentage = profile.ScaleWeight(pair.Item2);
                var color = options_.PickColorForWeight(weightPercentage);
                elementColorPairs.Add(new Tuple<IRElement, Color>(element, color));

                //? TODO: Configurable
                IconDrawing icon = null;

                if (index == 0) {
                    icon = IconDrawing.FromIconResource("DotIconRed");
                }
                else if (index <= 2) {
                    icon = IconDrawing.FromIconResource("DotIconYellow");
                }

                var tooltip = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(pair.Item2.TotalMilliseconds, 2)} ms)";
                elementOverlays.Add(new Tuple<IRElement, IconDrawing, string>(element, icon, tooltip));
                index++;
            }

            var elementOverlayList = document.AddIconElementOverlays(elementOverlays);

            foreach (var overlay in elementOverlayList) {
                overlay.IsToolTipPinned = true;
                overlay.TextColor = options_.ElementOverlayTextColor;
                overlay.Background ??= options_.ElementOverlayBackColor;
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
