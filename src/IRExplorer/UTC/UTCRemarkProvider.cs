// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CoreLib;
using CoreLib.Analysis;
using CoreLib.IR;
using CoreLib.UTC;

namespace Client {
    public class UTCRemarkParser {
        public static string ExtractVN(IRElement element) {
            var tag = element.GetTag<RemarkTag>();

            if (tag == null) {
                return null;
            }

            foreach (var remark in tag.Remarks) {
                if (remark.RemarkText.StartsWith("VN ")) {
                    var tokens = remark.RemarkText.Split(' ', ':');
                    string number = tokens[1];
                    return number;
                }
            }

            return null;
        }
    }

    // irx:remark parent_id, indent_level?

    public class UTCRemarkProvider : IRRemarkProvider {
        private List<RemarkCategory> categories_;
        private List<RemarkSectionBoundary> boundaries_;
        private Stack<RemarkContext> contextStack_;
        private RemarkCategory defaultCategory_;

        public UTCRemarkProvider() {
            contextStack_ = new Stack<RemarkContext>();
            LoadSettings();
        }

        public string SettingsFilePath => App.GetRemarksDefinitionFilePath("utc");
        
        public bool SaveSettings() {
            return false;
        }

        public bool LoadSettings() {
            var serializer = new RemarksDefinitionSerializer();
            var settingsPath = App.GetRemarksDefinitionFilePath("utc");

            if (settingsPath == null) {
                return false;
            }

            if (!serializer.Load(settingsPath, out categories_, out boundaries_)) {
                Trace.TraceError("Failed to load UTCRemarkProvider data");
                return false;
            }

            // Add a default category.
            defaultCategory_ = new RemarkCategory {
                Kind = RemarkKind.Default,
                Title = "",
                SearchedText = "",
                MarkColor = Colors.Transparent
            };

            categories_.Add(defaultCategory_);
            return true;
        }

        public List<RemarkCategory> LoadRemarkCategories() {
            if (LoadSettings()) {
                return categories_;
            }

            return null;
        }

        public List<RemarkSectionBoundary> LoadRemarkSectionBoundaries() {
            if (LoadSettings()) {
                return boundaries_;
            }

            return null;
        }

        public List<Remark> ExtractRemarks(string text, FunctionIR function, IRTextSection section) {
            var remarks = new List<Remark>();
            var lines = text.Split('\r', '\n');
            ExtractInstructionRemarks(text, lines, function, section, remarks);
            return remarks;
        }

        public OptimizationRemark GetOptimizationRemarkInfo(Remark remark) {
            return null;
        }

        private RemarkContext GetCurrentContext() {
            return contextStack_.Count > 0 ? contextStack_.Peek() : null;
        }

        private RemarkContext StartNewContext(string name, string id) {
            var currentContext = GetCurrentContext();
            var context = new RemarkContext(name, currentContext);

            if(currentContext != null) {
                currentContext.Children.Add(context);
            }
            else {
                //? TODO: Add to list of top-level contexts
            }

            contextStack_.Push(context);
            return context;
        }

        private void EndCurrentContext() {
            if(contextStack_.Count > 0) {
                contextStack_.Pop();
            }
        }

        private void ExtractInstructionRemarks(string text, string[] lines, FunctionIR function, 
                                               IRTextSection section, List<Remark> remarks) {
            var (fakeTuple, fakeBlock) = CreateFakeIRElements();

            var similarValueFinder = new SimilarValueFinder(function);
            var refFinder = new ReferenceFinder(function);

            int lineStartOffset = 0;
            int emptyLines = 0;

            //? TODO: For many lines, must be split in chunks and parallelized
            for (int i = 0; i < lines.Length; i++) {
                int index = 0;
                string line = lines[i];

                if (line.Length == 0) {
                    lineStartOffset++;
                    emptyLines++;
                    continue;
                }

                if (line.StartsWith("/// irx:")) {
                    if (HandleMetadata(line)) {
                        lineStartOffset += line.Length + 1;
                        continue;
                    }
                }

                while (index < line.Length) {
                    // Find next chunk delimited by whitespace.
                    if (index > 0) {
                        int next = line.IndexOf(' ', index);

                        if (next != -1) {
                            index = next + 1;
                        }
                    }

                    // Skip all whitespace.
                    while (index < line.Length && char.IsWhiteSpace(line[index])) {
                        index++;
                    }

                    if (index == line.Length) {
                        break;
                    }

                    //? TODO: This should use span to avoid allocations in Substring
                    string lineChunk = line.Substring(index);
                    var lineParser = new UTCParser(lineChunk, null, null);
                    var tuple = lineParser.ParseTuple(fakeBlock);

                    if (tuple is InstructionIR instr) {
                        var similarInstr = similarValueFinder.Find(instr);

                        if (similarInstr != null) {
                            var remarkLocation = new TextLocation(lineStartOffset, i - emptyLines, 0);

                            var location = new TextLocation(
                                instr.TextLocation.Offset + index + lineStartOffset, i - emptyLines, 0);

                            instr.TextLocation = location; // Set actual location in output text.

                            var remark =
                                new Remark(FindRemarkKind(line), section, line.Trim(), remarkLocation);

                            remark.ReferencedElements.Add(similarInstr);
                            remark.OutputElements.Add(instr);
                            remarks.Add(remark);
                            AttachToCurrentContext(remark);

                            index += instr.TextLength;
                            continue;
                        }
                    }

                    index++;
                }

                // Extract remarks mentioning only operands, not whole instructions.
                //? TODO: If an operand is part of an instruction that was already matched
                //? by a remark, don't include the operand anymore if it's the same remark text
                var parser = new UTCParser(line, null, null);

                while (!parser.IsDone()) {
                    var op = parser.ParseOperand(fakeTuple, false, false, true);

                    if (op != null) {
                        var value = refFinder.FindEquivalentValue(op, true);

                        if (value != null) {
                            //var parentInstr = value.ParentInstruction;

                            if (op.TextLocation.Line < lines.Length) {
                                var location = new TextLocation(op.TextLocation.Offset + lineStartOffset, i - emptyLines, 0);
                                op.TextLocation = location; // Set actual location in output text.

                                var remarkLocation = new TextLocation(lineStartOffset, i - emptyLines, 0);
                                var remark = new Remark(FindRemarkKind(line), section, line.Trim(), remarkLocation);

                                remark.ReferencedElements.Add(value);
                                remark.OutputElements.Add(op);
                                remarks.Add(remark);
                            }
                        }
                    }
                    else {
                        parser.SkipCurrentToken();
                    }
                }

                lineStartOffset += line.Length + 1;
            }
        }

        private void AttachToCurrentContext(Remark remark) {
            var context = GetCurrentContext();

            if (context != null) {
                context.Remarks.Add(remark);
                remark.Context = context;
            }

        }

        private bool HandleMetadata(string line) {
            var tokens = line.Split(new char[] { ' ', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length >= 2) {
                if (tokens[2] == "context_start" && tokens.Length >= 5) {
                    StartNewContext(tokens[3], tokens[4]);
                    return true;
                }
                else if (tokens[2] == "context_end") {
                    EndCurrentContext();
                    return true;
                }
            }

            return false;
        }

        public List<IRTextSection> GetSectionList(IRTextSection currentSection, int maxDepth, bool stopAtSectionBoundaries) {
            var list = new List<IRTextSection>();

            if (categories_ == null || boundaries_ == null) {
                return list; // Error loading settings.
            }

            var function = currentSection.ParentFunction;

            //? Example for SSAOpt sections
            //?  - consider only other SSaopt sections
            //?  - if in second pass, stop at section that separates first/second

            for (int i = currentSection.Number - 1, count = 0; i >= 0 && count < maxDepth; i--, count++) {
                var section = function.Sections[i];
                list.Add(section);

                if(stopAtSectionBoundaries && boundaries_.Count > 0) {
                    if (boundaries_.Find((boundary) => 
                            TextSearcher.Contains(section.Name, boundary.SearchedText, boundary.SearchKind)) != null) {
                        break; // Stop once section boundary reached.
                    }
                }
            }

            list.Reverse();
            return list;
        }

        public List<Remark> ExtractAllRemarks(List<IRTextSection> sections, FunctionIR function,
                                                  LoadedDocument document) {
            int maxConcurrency = Math.Min(sections.Count, Math.Min(8, Environment.ProcessorCount));
            var tasks = new Task<List<Remark>>[sections.Count];
            using var concurrencySemaphore = new SemaphoreSlim(maxConcurrency);
            int index = 0;

            //? TODO: Add per-function remark cache
            foreach (var section in sections) {
                concurrencySemaphore.Wait();

                tasks[index++] = Task.Run(() => {
                    try {
                        string sectionText = document.Loader.LoadSectionPassOutput(section.OutputBefore);
                        return ExtractRemarks(sectionText, function, section);
                    }
                    finally {
                        concurrencySemaphore.Release();
                    }
                });   
            }

            Task.WhenAll(tasks).Wait();
            
            // Combine all remarks into a single list.
            var remarks = new List<Remark>();

            for (int i = 0; i < sections.Count; i++) {
                remarks.AddRange(tasks[i].Result);
            }
            
            return remarks;
        }

        private (TupleIR, BlockIR) CreateFakeIRElements() {
            var func = new FunctionIR();
            var block = new BlockIR(IRElementId.FromLong(0), 0, func);
            var tuple = new TupleIR(IRElementId.FromLong(1), TupleKind.Other, block);
            return (tuple, block);
        }

        public RemarkCategory FindRemarkKind(string text) {
            if (categories_ == null) {
                return default; // Error loading settings.
            }

            text = text.Trim();

            foreach (var category in categories_) {
                if (TextSearcher.Contains(text, category.SearchedText, category.SearchKind)) {
                    return category;
                }
            }

            return defaultCategory_;
        }
    }
}
