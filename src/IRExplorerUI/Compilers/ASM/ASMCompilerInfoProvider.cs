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
using System.IO;

namespace IRExplorerUI.Compilers.ASM {
    public class ASMCompilerInfoProvider : ICompilerInfoProvider {
        private readonly ISession session_;
        //? TODO: Make custom to fix <UNTITLED> section names
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

        public string OpenFileFilter => "ASM and Binary Files|*.asm;*.txt;*.log;*.exe;*.dll;*.sys|All Files|*.*";
        public string OpenDebugFileFilter => "Debug Files|*.pdb|All Files|*.*";

        public string DefaultSyntaxHighlightingFile => "ASM";

        public ICompilerIRInfo IR => ir_;

        public INameProvider NameProvider => names_;

        public ISectionStyleProvider SectionStyleProvider => styles_;

        public IRRemarkProvider RemarkProvider => remarks_;

        public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>();

        public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>();

        public List<FunctionTaskDefinition> ScriptFunctionTasks => new List<FunctionTaskDefinition>();

        public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
            // Annotate the instructions with debug info (line numbers, source files)
            // if the debug file is specified and available.
            var loadedDoc = Session.SessionState.FindLoadedDocument(section);
            var debugFile = loadedDoc.DebugInfoFilePath;

            if (!string.IsNullOrEmpty(debugFile) &&
                File.Exists(debugFile)) {

                using var debugInfo = new PDBDebugInfoProvider();

                if (debugInfo.LoadDebugInfo(debugFile)) {
                    debugInfo.AnnotateSourceLocations(function, section.ParentFunction);
                }
            }

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
                    var label = $"B{block.Number}";
                    var overlay = document.AddIconElementOverlay(firstTuple, null, 0, overlayHeight, label, null,
                                                                 HorizontalAlignment.Left, VerticalAlignment.Center, -6, -1);
                    overlay.ShowOnMarkerBar = false;
                    overlay.IsLabelPinned = true;
                    overlay.DefaultOpacity = 1;
                    overlay.TextWeight = FontWeights.DemiBold;
                    overlay.TextColor = Brushes.DarkBlue;

                    var backColor = block.Number % 2 == 0 ?
                        App.Settings.DocumentSettings.BackgroundColor :
                        App.Settings.DocumentSettings.AlternateBackgroundColor;
                    overlay.Background = ColorBrushes.GetBrush(backColor);

                    overlay.ShowBackgroundOnMouseOverOnly = false;
                    overlay.UseLabelBackground = true;
                    overlay.Padding = 2;
                }
            }

            // Check if there is profile info and annotate the instrs. with timing info.
            var profile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

            if(profile != null) {
                var profileOptions = ProfileDocumentMarkerOptions.Default;
                var profileMarker = new ProfileDocumentMarker(profile, Session.ProfileData,
                                                              profileOptions, ir_);
                profileMarker.Mark(document, function);
            }

            // Annotate instrs. with source line numbers if debug info is available.
            var markerOptions = ProfileDocumentMarkerOptions.Default;
            var sourceMarker = new SourceDocumentMarker(markerOptions, ir_);
            sourceMarker.Mark(document, function);
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

        public (string, List<string> linePrefixes) FilterInputText(string text) {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var builder = new StringBuilder(lines.Length);
            var linePrefixes = new List<string>();

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

                        linePrefixes.Add(line.Substring(0, index));
                        newLine = line.Substring(index).PadLeft(line.Length, ' ');
                    }
                }
                
                builder.AppendLine(newLine);
            }

            return (builder.ToString(), linePrefixes);
        }

        public void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo) {
            
        }
    }
}
