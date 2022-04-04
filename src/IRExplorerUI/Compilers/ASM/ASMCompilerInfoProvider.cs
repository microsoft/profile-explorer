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
using System.Text;
using System.Windows;
using System.Windows.Media;
using IRExplorerUI.Profile;
using IRExplorerCore.IR.Tags;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using IRExplorerCore.Utilities;
using static IRExplorerUI.ModuleReportPanel;

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

        public virtual string CompilerIRName => "ASM";

        public virtual string CompilerDisplayName => "ASM " + ir_.Mode.ToString();

        public virtual string OpenFileFilter => "ASM and Binary Files|*.asm;*.txt;*.log;*.exe;*.dll;*.sys|All Files|*.*";
        public virtual string OpenDebugFileFilter => "Debug Files|*.pdb|All Files|*.*";

        public virtual string DefaultSyntaxHighlightingFile => (ir_.Mode == IRMode.ARM64 ?  "ARM64" : "x86") + " ASM IR";

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

            if (!string.IsNullOrEmpty(debugFile) && File.Exists(debugFile)) {
                using var debugInfo = CreateDebugInfoProvider(loadedDoc.BinaryFilePath);

                if (debugInfo.LoadDebugInfo(debugFile)) {
                    debugInfo.AnnotateSourceLocations(function, section.ParentFunction);
                }
            }

            return true;
        }

        public IDiffInputFilter CreateDiffInputFilter() {
            var filter = new ASMDiffInputFilter();
            filter.Initialize(App.Settings.DiffSettings, IR);
            return filter;
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            return new BasicDiffOutputFilter();
        }

        //? TODO: Debug/Binary related functs should not be part of CompilerInfoProvider
        public IDebugInfoProvider CreateDebugInfoProvider(string imagePath) {
            using var info = new PEBinaryInfoProvider(imagePath);

            if (!info.Initialize()) {
                return new JsonDebugInfoProvider();
                return null;
            }

            switch (info.BinaryFileInfo.FileKind) {
                case BinaryFileKind.Native: {
                    return new PDBDebugInfoProvider(App.Settings.SymbolOptions);
                }
                case BinaryFileKind.DotNetR2R:
                case BinaryFileKind.DotNet: {
                    return new JsonDebugInfoProvider();
                }
                default: {
                    throw new InvalidOperationException();
                }
            }
        }

        public async Task<string> FindDebugInfoFile(string imagePath, SymbolFileSourceOptions options = null, string disasmOutputPath = null) {
            using var info = new PEBinaryInfoProvider(imagePath);

            if (!info.Initialize()) {
                return Utils.LocateDebugInfoFile(imagePath, ".json");
            }

            switch (info.BinaryFileInfo.FileKind) {
                case BinaryFileKind.Native: {
                    if (options == null) {
                        // Make sure the binary directory is also included in the symbol search.
                        options = (SymbolFileSourceOptions)App.Settings.SymbolOptions.Clone();
                        options.InsertSymbolPath(imagePath);
                    }

                    var result = await PDBDebugInfoProvider.LocateDebugInfoFile(info.SymbolFileInfo, options);

                    if (File.Exists(result)) {
                        return result;
                    }

                    //? TODO: Shouldn't be needed anymore
                    // Do a simple search otherwise.
                    return Utils.LocateDebugInfoFile(imagePath, ".pdb");
                }
                case BinaryFileKind.DotNetR2R: {
                    if (!string.IsNullOrEmpty(disasmOutputPath)) {
                        try {
                            // When using the external disassembler, the output file
                            // will be a random temp file, not based on image name.
                            var path = Path.GetDirectoryName(disasmOutputPath);
                            return Path.Combine(path, Path.GetFileNameWithoutExtension(disasmOutputPath)) + ".json";
                        }
                        catch (Exception ex) {
                            Trace.TraceError($"Failed to get .NET R2R debug file path for {imagePath}: {ex}");
                        }
                    }

                    return Utils.LocateDebugInfoFile(imagePath, ".json");
                }
                default: {
                    throw new InvalidOperationException();
                }
            }
            
        }

        public async Task<string> FindBinaryFile(BinaryFileDescription binaryFile, SymbolFileSourceOptions options = null) {
            if (options == null) {
                // Make sure the binary directory is also included in the symbol search.
                options = (SymbolFileSourceOptions)App.Settings.SymbolOptions.Clone();
                options.InsertSymbolPath(binaryFile.ImagePath);
            }

            return await PEBinaryInfoProvider.LocateBinaryFile(binaryFile, options);
        }

        public IDisassembler CreateDisassembler(string modulePath) {
            var info = PEBinaryInfoProvider.GetBinaryFileInfo(modulePath);

            if (info != null) {
                return new ExternalDisassembler(App.Settings.GetExternalDisassemblerOptions(info.FileKind));
            }

            // Assume it's a native image.
            return new ExternalDisassembler(App.Settings.GetExternalDisassemblerOptions(BinaryFileKind.Native));
        }
        //? TODO: << Debug/Binary related functs should not be part of CompilerInfoProvider

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            return new BasicBlockFoldingStrategy(function);
        }

        public virtual Task HandleLoadedDocument(LoadedDocument document, string modulePath) {
            return Task.CompletedTask;
        }

        public async Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section) {
            // Since the ASM blocks don't have a number in the text,
            // attach an overlay label next to the first instr. in the block.
            document.ColumnData = new IRDocumentColumnData(function.InstructionCount);
            var overlayHeight = document.TextArea.TextView.DefaultLineHeight;
            var options = ProfileDocumentMarkerOptions.Default; //? TODO: App.Settings...
            var blockPen = ColorPens.GetPen(options.BlockOverlayBorderColor,
                                            options.BlockOverlayBorderThickness);

            foreach (var block in function.Blocks) {
                if (block.Tuples.Count <= 0) {
                    continue;
                }

                var label = $"B{block.Number}";
                var overlay = document.AddIconElementOverlay(block, null, 0, overlayHeight, label, null,
                                                             HorizontalAlignment.Left, VerticalAlignment.Center);
                overlay.MarginX = -8;
                overlay.Padding = 4;
                overlay.ShowOnMarkerBar = false;
                overlay.IsLabelPinned = true;
                overlay.TextWeight = FontWeights.Bold;
                overlay.TextColor = options.BlockOverlayTextColor;

                var backColor = block.HasEvenIndexInFunction ?
                    App.Settings.DocumentSettings.BackgroundColor :
                    App.Settings.DocumentSettings.AlternateBackgroundColor;
                overlay.Background = ColorBrushes.GetBrush(backColor);
                overlay.Border = blockPen;

                overlay.ShowBackgroundOnMouseOverOnly = false;
                overlay.ShowBorderOnMouseOverOnly = false;
                overlay.UseLabelBackground = true;
            }

            // Check if there is profile info and annotate the instrs. with timing info.
            var profile = Session.ProfileData?.GetFunctionProfile(section.ParentFunction);

            if(profile != null) {
                var profileOptions = ProfileDocumentMarkerOptions.Default;
                var profileMarker = new ProfileDocumentMarker(profile, Session.ProfileData,
                                                              profileOptions, ir_);
                await profileMarker.Mark(document, function);
            }

            // Annotate instrs. with source line numbers if debug info is available.
            var markerOptions = ProfileDocumentMarkerOptions.Default;
            var sourceMarker = new SourceDocumentMarker(markerOptions, ir_);
            await sourceMarker.Mark(document, function);
        }

        public void ReloadSettings() {

        }
    }

    public class ASMDiffInputFilter : IDiffInputFilter {
        public char[] AcceptedLetters => new char[] {
            'A', 'B', 'C', 'D', 'E', 'F',
            'a', 'b', 'c', 'd', 'e', 'f'
        };

        private Dictionary<string, int> addressMap_;
        private int nextAddressId_;

        public void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo) {
            addressMap_ = new Dictionary<string, int>();
            nextAddressId_ = 1;
        }

        public FilteredDiffInput FilterInputText(string text) {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var result = new FilteredDiffInput(lines.Length);

            var builder = new StringBuilder(text.Length);
            var linePrefixes = new List<string>(lines.Length);

            foreach (var line in lines) {
                var (newLine, replacements) = FilterInputLineImpl(line);
                builder.AppendLine(newLine);
                result.LineReplacements.Add(replacements);
            }

            result.Text = builder.ToString();
            return result;
        }

        public string FilterInputLine(string line) {
            var (result, _) = FilterInputLineImpl(line);
            return result;
        }

        public (string, List<FilteredDiffInput.Replacement>) FilterInputLineImpl(string line) {
            var newLine = line;
            int index = line.IndexOf(':');

            if (index == -1) {
                return (newLine, FilteredDiffInput.NoReplacements);
            }

            var (address, _) = ParseAddress(line, 0);

            if (address == null) {
                return (newLine, FilteredDiffInput.NoReplacements);
            }

            var replacements = new List<FilteredDiffInput.Replacement>(1);

            // Create a canonical form for the address so that diffing considers
            // the addresses equal if the target blocks are the same in two functs.
            // where the function start is not the same anymore.
            if (addressMap_.GetOrAddValue(address, nextAddressId_) == nextAddressId_) {
                nextAddressId_++;
            }

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
            int afterIndex = index;

            while (index > startIndex && !Char.IsWhiteSpace(line[index - 1])) {
                index--;
            }

            // Check if there is any address found after the opcode
            // and replace it with the canonical form.
            while (afterIndex < line.Length && !Char.IsWhiteSpace(line[afterIndex])) {
                afterIndex++;
            }

            //? TODO: There could be multiple addresses as operands?
            if (afterIndex < line.Length) {
                var (afterAddress, offset) = ParseAddress(line, afterIndex);

                if (afterAddress != null && afterAddress.Length >= 4) {
                    int id = addressMap_.GetOrAddValue(afterAddress, nextAddressId_);
                    if (id == nextAddressId_) {
                        nextAddressId_++;
                    }

                    var replacementAddress = id.ToString().PadLeft(afterAddress.Length, 'A');
                    newLine = newLine.Replace(afterAddress, replacementAddress); //? TODO: In-place replace?
                    replacements.Add(new FilteredDiffInput.Replacement(offset, replacementAddress, afterAddress));
                }
            }

            var newLinePrefix = newLine.Substring(0, index);
            var newLinePrefixReplacement = new string(' ', index);
            newLine = newLinePrefixReplacement + newLine.Substring(index);  //? TODO: In-place replace?
            replacements.Add(new FilteredDiffInput.Replacement(0, newLinePrefixReplacement, newLinePrefix));
            return (newLine, replacements);
        }
        
        private (string, int) ParseAddress(string line, int startOffset) {
            int addressStartOffset = startOffset;
            int offset;

            for (offset = startOffset; offset < line.Length; offset++) {
                var letter = line[offset];

                if (Char.IsWhiteSpace(letter)) {
                    addressStartOffset = offset + 1;
                    continue;
                }

                if (!(Char.IsDigit(letter) || Array.IndexOf(AcceptedLetters, letter) != -1)) {
                    break;
                }
            }

            if (offset > addressStartOffset) {
                return (line.Substring(addressStartOffset, offset - addressStartOffset), addressStartOffset);
            }

            return (null, 0);
        }
    }
}
