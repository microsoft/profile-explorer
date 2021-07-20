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

        public ISession Session => session_;

        public string CompilerIRName => "ASM";

        public string CompilerDisplayName => "ASM " + ir_.Mode.ToString();

        public string OpenFileFilter => "Asm Files|*.asm;*.txt;*.log|All Files|*.*";

        public string DefaultSyntaxHighlightingFile => "ASM";

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

                    var backColor = block.Number % 2 == 0 ?
                        App.Settings.DocumentSettings.BackgroundColor :
                        App.Settings.DocumentSettings.AlternateBackgroundColor;
                    overlay.Background = ColorBrushes.GetBrush(backColor);

                    overlay.ShowBackgroundOnMouseOverOnly = false;
                    overlay.UseToolTipBackground = true;
                    overlay.Padding = 2;
                }
            }

            // Check if there is profile info.
            var profile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

            if(profile != null) {
                var markerOptions = ProfileDocumentMarkerOptions.Default();
                var profileMarker = new ProfileDocumentMarker(markerOptions, ir_);
                profileMarker.Mark(document, profile, function);
            }

            return true;
        }

        public void ReloadSettings() {

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
