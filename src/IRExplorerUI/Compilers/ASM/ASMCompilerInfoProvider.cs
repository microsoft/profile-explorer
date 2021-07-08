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
    public class ASMCompilerInfoProvider : ICompilerInfoProvider {
        private readonly ISession session_;
        private readonly UTCNameProvider names_ = new UTCNameProvider();
        private readonly UTCSectionStyleProvider styles_ = new UTCSectionStyleProvider();
        private readonly UTCRemarkProvider remarks_;
        private readonly ASMCompilerIRInfo ir_;

        public ASMCompilerInfoProvider(IRMode mode, ISession session) {
            session_ = session;
            remarks_ = new UTCRemarkProvider(this);
            ir_ = new ASMCompilerIRInfo(mode);
        }

        public string CompilerIRName => "ASM";

        public string OpenFileFilter => "Asm Files|*.asm;*.txt;*.log|All Files|*.*";

        public string DefaultSyntaxHighlightingFile => "ASM";

        public ISession Session => session_;

        public ICompilerIRInfo IR => ir_;

        public INameProvider NameProvider => names_;

        public ISectionStyleProvider SectionStyleProvider => styles_;

        public IRRemarkProvider RemarkProvider => remarks_;

        public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>();

        public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>();

        public List<FunctionTaskDefinition> ScriptFunctionTasks => new List<FunctionTaskDefinition>();

        public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
            return true;
        }

        public IDiffInputFilter CreateDiffInputFilter() {
            return new ASMDiffInputFilter();
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            return new BasicDiffOutputFilter();
        }

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            return new BasicBlockFoldingStrategy(function);
        }

        public bool HandleLoadedDocument(IRDocument document, FunctionIR function, IRTextSection section) {
            // Since the ASM blocks don't have a number in the text,
            // attach an overlay label next to the first instr. in the block.
            var overlayHeight = document.TextArea.TextView.DefaultLineHeight - 2;

            foreach (var block in function.Blocks) {
                if (block.Tuples.Count > 0) {
                    var firstTuple = block.Tuples[0];
                    var tooltip = $"B{block.Number}";
                    var overlay = document.AddIconElementOverlay(firstTuple, null, 0, overlayHeight, tooltip,
                                                                 HorizontalAlignment.Left, VerticalAlignment.Center, -6, -1);
                    overlay.ShowOnMarkerBar = false;
                    overlay.IsToolTipPinned = true;
                    overlay.DefaultOpacity = 1;
                    overlay.TextWeight = FontWeights.DemiBold;
                    overlay.TextColor = Brushes.DarkBlue;
                    overlay.ShowBackgroundOnMouseOverOnly = false;
                    overlay.UseToolTipBackground = true;
                    overlay.Padding = 2;
                }
            }

            // Check if there is profile info.
            var profile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

            if(profile != null) {
                AnnotateProfilerData(profile, function, section, document);
            }

            return true;
        }

        private bool TryFindElementForOffset(AssemblyMetadataTag metadataTag, long offset, out IRElement element) {
            int multiplier = 1;
            var offsetData = Session.CompilerInfo.IR.InstrOffsetData;

            do {
                if (metadataTag.OffsetToElementMap.TryGetValue(offset - multiplier * offsetData.OffsetAdjustIncrement, out element)) {
                    return true;
                }
                ++multiplier;
            } while (multiplier * offsetData.OffsetAdjustIncrement < offsetData.MaxOffsetAdjust);

            return false;
        }

        //? TODO: Should be extracted to share with other compilers
        private void AnnotateProfilerData(FunctionProfileData profile, FunctionIR function, 
                                          IRTextSection section, IRDocument document) {
            var summary = section.ParentFunction.ParentSummary;

            foreach (var pair in profile.ChildrenWeights) {
                var child = summary.GetFunctionWithId(pair.Key);
            }

            double weightCutoff = 0.003;
            int lightSteps = 10; // 1,1,0.5 is red
            var colors = ColorUtils.MakeColorPallete(1, 1, 0.85f, 0.95f, lightSteps);
            var nextElementId = new IRElementId();

            var metadataTag = function.GetTag<AssemblyMetadataTag>();
            bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;
            bool markedInstrs = false;

            var blockWeights = new Dictionary<BlockIR, TimeSpan>();

            if (hasInstrOffsetMetadata) {
                var elements = new List<Tuple<IRElement, TimeSpan>>(profile.InstructionWeight.Count);

                foreach (var pair in profile.InstructionWeight) {
                    if (TryFindElementForOffset(metadataTag, pair.Key, out var element)) {
                        elements.Add(new Tuple<IRElement, TimeSpan>(element, pair.Value));

                        if (blockWeights.TryGetValue(element.ParentBlock, out var currentWeight)) {
                            blockWeights[element.ParentBlock] = currentWeight + pair.Value;
                        }
                        else {
                            blockWeights[element.ParentBlock] = pair.Value;
                        }
                    }
                }

                elements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
                int index = 0;

                foreach (var pair in elements) {
                    var element = pair.Item1;
                    double weightPercentage = profile.ScaleWeight(pair.Item2);

                    if (weightPercentage < weightCutoff) {
                        continue;
                    }

                    int colorIndex = (int)Math.Floor(lightSteps * (1.0 - weightPercentage));

                    if (colorIndex < 0) {
                        colorIndex = 0;
                    }

                    var color = colors[colorIndex];

                    var tooltip = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(pair.Item2.TotalMilliseconds, 2)} ms)";
                    Session.CurrentDocument.MarkElement(element, color);

                    IconDrawing icon = null;
                    bool isPinned = false;
                    Brush textColor;

                    if (index == 0) {
                        icon = IconDrawing.FromIconResource("StarIconRed");
                        textColor = Brushes.DarkRed;
                        isPinned = true;
                    }
                    else if (index <= 3) {
                        icon = IconDrawing.FromIconResource("StarIconYellow");
                        textColor = Brushes.DarkRed;
                        isPinned = true;
                    }
                    else {
                        icon = IconDrawing.FromIconResource("DotIcon");
                        textColor = Brushes.DarkRed;
                        isPinned = true;
                    }

                    var overlay = Session.CurrentDocument.AddIconElementOverlay(element, icon, 16, 16, tooltip);
                    overlay.IsToolTipPinned = isPinned;
                    overlay.TextColor = textColor;
                    overlay.DefaultOpacity = 1;
                    markedInstrs = true;
                    index++;
                }

                foreach (var pair in blockWeights) {
                    var element = pair.Key;
                    double weightPercentage = profile.ScaleWeight(pair.Value);

                    if (weightPercentage < weightCutoff) {
                        continue;
                    }

                    var tooltip = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(pair.Value.TotalMilliseconds, 2)} ms)";
                    var icon = IconDrawing.FromIconResource("DotIconYellow");
                    var overlay = Session.CurrentDocument.AddIconElementOverlay(element, icon, 16, 16, tooltip);
                    overlay.IsToolTipPinned = true;
                    overlay.TextColor = Brushes.DarkRed;
                }
            }

            var lines = new List<Tuple<int, TimeSpan>>(profile.SourceLineWeight.Count);

            foreach (var pair in profile.SourceLineWeight) {
                lines.Add(new Tuple<int, TimeSpan>(pair.Key, pair.Value));
            }

            lines.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            int lineIndex = 0;

            foreach (var pair in lines) {
                int sourceLine = pair.Item1;
                double weightPercentage = profile.ScaleWeight(pair.Item2);

                // if (weightPercentage < weightCutoff) {
                //     continue;
                // }

                int colorIndex = (int)Math.Floor(lightSteps * (1.0 - weightPercentage));

                if (colorIndex < 0) {
                    colorIndex = 0;
                }

                var color = colors[colorIndex];
                
                if (!hasInstrOffsetMetadata || !markedInstrs) {
                    document.MarkElementsOnSourceLine(sourceLine, color);
                }
            }
        }

        public void ReloadSettings() {
            //? TODO: Not needed, set in constructor
            // IRModeUtilities.SetIRModeFromSettings(ir_);
        }
    }

    public class ASMDiffInputFilter : IDiffInputFilter {
        public char[] AcceptedLetters => new char[] {
            'A', 'B', 'C', 'D', 'E', 'F',
            'a', 'b', 'c', 'd', 'e', 'f'
        };

        public string FilterInputText(string text) {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var builder = new StringBuilder(lines.Length);

            foreach (var line in lines) {
                var newLine = line;
                int index = line.IndexOf(':');

                if (index != -1) {
                    bool isAddress = true;

                    for (int i = 0; i < index; i++) {
                        var letter = line[i];
                        if (!(Char.IsWhiteSpace(letter) || Char.IsDigit(letter) ||
                              Array.IndexOf(AcceptedLetters, letter) != -1)) {
                            isAddress = false;
                            break;
                        }
                    }

                    if (isAddress) {
                        // Skip over the bytecodes found before the opcode.
                        int startIndex = index;

                        for (index = index + 1; index < line.Length; index++) {
                            var letter = line[index];
                            if (!(Char.IsWhiteSpace(letter) || Char.IsDigit(letter) ||
                                  Array.IndexOf(AcceptedLetters, letter) != -1)) {
                                break;
                            }
                        }

                        // Move back before the opcode starts.
                        while(index > startIndex && !Char.IsWhiteSpace(line[index - 1])) {
                            index--;
                        }

                        newLine = line.Substring(index).PadLeft(line.Length, '#');
                    }
                }
                
                builder.AppendLine(newLine);
            }

            return builder.ToString();
        }

        public void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo) {
            
        }
    }
}
