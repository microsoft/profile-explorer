﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerCore.UTC;

namespace IRExplorerUI.UTC {
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

    public class UTCRemarkProvider : IRRemarkProvider {
        private class RemarkContextState {
            private Stack<RemarkContext> contextStack_;
            private List<RemarkContext> rootContexts_;

            public List<RemarkContext> RootContexts => rootContexts_;

            public RemarkContextState() {
                contextStack_ = new Stack<RemarkContext>();
                rootContexts_ = new List<RemarkContext>();
            }

            public void AttachToCurrentContext(Remark remark) {
                var context = GetCurrentContext();

                if (context != null) {
                    context.Remarks.Add(remark);
                    remark.Context = context;
                }
            }

            public RemarkContext GetCurrentContext() {
                return contextStack_.Count > 0 ? contextStack_.Peek() : null;
            }

            public RemarkContext StartNewContext(string id, string name, int lineNumber) {
                var currentContext = GetCurrentContext();
                var context = new RemarkContext(id, name, currentContext);

                if (currentContext != null) {
                    currentContext.Children.Add(context);
                }
                else {
                    rootContexts_.Add(context);
                }

                context.StartLine = lineNumber;
                contextStack_.Push(context);
                return context;
            }

            public void EndCurrentContext(int lineNumber) {
                if (contextStack_.Count > 0) {
                    var context = contextStack_.Pop();
                    context.EndLine = lineNumber;
                }
            }
        }

        private const string MetadataStartString = "/// irx:";
        private const string RemarkContextStartString = "context_start";
        private const string RemarkContextEndString = "context_end";

        private ICompilerInfoProvider compilerInfo_;
        private List<RemarkCategory> categories_;
        private List<RemarkSectionBoundary> boundaries_;
        private List<RemarkTextHighlighting> highlighting_;
        private RemarkCategory defaultCategory_;
        private bool settingsLoaded_;

        public UTCRemarkProvider(ICompilerInfoProvider compilerInfo) {
            compilerInfo_ = compilerInfo;
            categories_ = new List<RemarkCategory>();
            boundaries_ = new List<RemarkSectionBoundary>();
            highlighting_ = new List<RemarkTextHighlighting>();
            settingsLoaded_ = LoadSettings();
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

            if (!serializer.Load(settingsPath, 
                                 out categories_, 
                                 out boundaries_,
                                 out highlighting_)) {
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

        public List<RemarkCategory> RemarkCategories {
            get {
                if (LoadSettings()) {
                    return categories_;
                }

                return null;
            }
        }

        public List<RemarkSectionBoundary> RemarkSectionBoundaries {
            get {
                if (LoadSettings()) {
                    return boundaries_;
                }

                return null;
            }
        }

        public List<RemarkTextHighlighting> RemarkTextHighlighting {
            get {
                if (LoadSettings()) {
                    return highlighting_;
                }

                return null;
            }
        }

        public List<Remark> ExtractRemarks(string text, FunctionIR function, IRTextSection section,
                                           RemarkProviderOptions options) {
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            return ExtractRemarks(new List<string>(lines), function, section, options);
        }

        public List<Remark> ExtractRemarks(List<string> textLines, FunctionIR function, IRTextSection section,
                                           RemarkProviderOptions options) {
            if (!settingsLoaded_) {
                return new List<Remark>(); // Failed to load settings, bail out.
            }

            // The RemarkContextState allows multiple threads to use the provider
            // by not having any global state visible to all threads.
            var remarks = new List<Remark>();
            var state = new RemarkContextState();

            ExtractInstructionRemarks(textLines, function, section, remarks, options, state);
            return remarks;
        }

        public OptimizationRemark GetOptimizationRemarkInfo(Remark remark) {
            return null;
        }

        private void ExtractInstructionRemarks(List<string> lines, FunctionIR function,
                                               IRTextSection section, List<Remark> remarks,
                                               RemarkProviderOptions options,
                                               RemarkContextState state) {
            var (fakeTuple, fakeBlock) = CreateFakeIRElements();

            var similarValueFinder = new SimilarValueFinder(function);
            var refFinder = new ReferenceFinder(function, compilerInfo_.IR);

            //? TODO: Extract "block N" as a block reference

            // The split lines don't include the endline, but considering
            // the \r \n is needed to get the proper document offset.
            int newLineLength = Environment.NewLine.Length;
            int lineStartOffset = 0;

            //? TODO: For many lines, must be split in chunks and parallelized
            for (int i = 0; i < lines.Count; i++) {
                int index = 0;
                string line = lines[i];

                if (line.StartsWith(MetadataStartString, StringComparison.Ordinal)) {
                    if (HandleMetadata(line, i, state)) {
                        lineStartOffset += line.Length + newLineLength;
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
                            var remarkLocation = new TextLocation(lineStartOffset, i, 0);

                            var location = new TextLocation(
                                instr.TextLocation.Offset + index + lineStartOffset, i, 0);

                            instr.TextLocation = location; // Set actual location in output text.

                            var remarkKind = FindRemarkKind(line, isInstructionElement: true);
                            var remark = new Remark(remarkKind, section, 
                                                    line.Trim(), line, remarkLocation);
                            remark.ReferencedElements.Add(similarInstr);
                            remark.OutputElements.Add(instr);
                            remarks.Add(remark);
                            state.AttachToCurrentContext(remark);

                            index += instr.TextLength;
                            continue;
                        }
                    }

                    index++;
                }

                // Extract remarks mentioning only operands, not whole instructions.
                //? TODO: If an operand is part of an instruction that was already matched
                //? by a remark, don't include the operand anymore if it's the same remark text
                if (!options.FindOperandRemarks) {
                    lineStartOffset += line.Length + newLineLength;
                    continue;
                }

                var parser = new UTCParser(line, null, null);

                while (!parser.IsDone()) {
                    var op = parser.ParseOperand(fakeTuple, false, false, true);

                    if (op != null) {
                        var value = refFinder.FindEquivalentValue(op, true);

                        if (value != null) {
                            //var parentInstr = value.ParentInstruction;

                            if (op.TextLocation.Line < lines.Count) {
                                var location = new TextLocation(op.TextLocation.Offset + lineStartOffset, i, 0);
                                op.TextLocation = location; // Set actual location in output text.

                                var remarkLocation = new TextLocation(lineStartOffset, i, 0);
                                var remarkKind = FindRemarkKind(line, isInstructionElement: false);
                                var remark = new Remark(remarkKind, section, 
                                                        line.Trim(), line, remarkLocation);

                                remark.ReferencedElements.Add(value);
                                remark.OutputElements.Add(op);
                                remarks.Add(remark);
                                state.AttachToCurrentContext(remark);
                            }
                        }
                    }
                    else {
                        parser.SkipCurrentToken();
                    }
                }

                lineStartOffset += line.Length + newLineLength;
            }
        }

        private bool HandleMetadata(string line, int lineNumber, RemarkContextState state) {
            var tokens = line.Split(new char[] { ' ', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length >= 2) {
                if (tokens[2].StartsWith(RemarkContextStartString) && tokens.Length >= 5) {
                    state.StartNewContext(tokens[3], tokens[4], lineNumber);
                    return true;
                }
                else if (tokens[2].StartsWith(RemarkContextEndString)) {
                    state.EndCurrentContext(lineNumber);
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

            for (int i = currentSection.Number - 1, count = 0; i >= 0 && count < maxDepth; i--, count++) {
                var section = function.Sections[i];
                list.Add(section);

                if (stopAtSectionBoundaries && boundaries_.Count > 0) {
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
                                              LoadedDocument document, RemarkProviderOptions options) {
            if (!settingsLoaded_) {
                return new List<Remark>(); // Failed to load settings, bail out.
            }

            int maxConcurrency = Math.Min(sections.Count, Math.Min(8, Environment.ProcessorCount));
            var tasks = new Task<List<Remark>>[sections.Count];
            using var concurrencySemaphore = new SemaphoreSlim(maxConcurrency);
            int index = 0;

            //? TODO: Add per-function remark cache
            foreach (var section in sections) {
                concurrencySemaphore.Wait();

                tasks[index++] = Task.Run(() => {
                    try {
                        var sectionTextLines = document.Loader.GetSectionOutputTextLines(section.OutputBefore);
                        return ExtractRemarks(sectionTextLines, function, section, options);
                    }
                    finally {
                        concurrencySemaphore.Release();
                    }
                });
            }

            Task.WaitAll(tasks);

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

        public RemarkCategory FindRemarkKind(string text, bool isInstructionElement) {
            if (categories_ == null) {
                return default; // Error loading settings.
            }

            text = text.Trim();

            foreach (var category in categories_) {
                // Ignore remarks that expect an entire instruction reference
                // if the IR is not an instruction.
                if (category.ExpectInstructionIR && !isInstructionElement) {
                    continue;
                }

                if (TextSearcher.Contains(text, category.SearchedText, category.SearchKind)) {
                    return category;
                }
            }

            return defaultCategory_;
        }
    }
}
