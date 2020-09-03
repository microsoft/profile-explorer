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
        private List<RemarkCategory> categories_;
        private List<RemarkSectionBoundary> boundaries_;
        private RemarkCategory defaultCategory_;
        private bool settingsLoaded_;

        public UTCRemarkProvider() {
            categories_ = new List<RemarkCategory>();
            boundaries_ = new List<RemarkSectionBoundary>();
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

        public List<Remark> ExtractRemarks(string text, FunctionIR function, IRTextSection section,
                                           RemarkProviderOptions options) {
            if (!settingsLoaded_) {
                return new List<Remark>(); // Failed to load settings, bail out.
            }

            //? TODO: Could use an API that doesn't need splitting into lines again
            var remarks = new List<Remark>();
            var lines = text.Split('\r', '\n');

            // The RemarkContextState allows multiple threads to use the provider
            // by not having any global state visible to all threads.
            var state = new RemarkContextState();
            ExtractInstructionRemarks(text, lines, function, section, remarks, options, state);

            foreach (var context in state.RootContexts) {
                Trace.TraceInformation("\n------------------------------------------------------");
                Trace.TraceInformation("\n" + context.ToString());
            }

            return remarks;
        }

        public OptimizationRemark GetOptimizationRemarkInfo(Remark remark) {
            return null;
        }



        private void ExtractInstructionRemarks(string text, string[] lines, FunctionIR function,
                                               IRTextSection section, List<Remark> remarks,
                                               RemarkProviderOptions options,
                                               RemarkContextState state) {
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

                if (line.StartsWith(MetadataStartString, StringComparison.Ordinal)) {
                    if (HandleMetadata(line, i - emptyLines, state)) {
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

                            var remark = new Remark(FindRemarkKind(line), section, line.Trim(), remarkLocation);
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
                    lineStartOffset += line.Length + 1;
                    continue;
                }

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


        private bool HandleMetadata(string line, int lineNumber, RemarkContextState state) {
            var tokens = line.Split(new char[] { ' ', ':', ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length >= 2) {
                if (tokens[2] == RemarkContextStartString && tokens.Length >= 5) {
                    state.StartNewContext(tokens[3], tokens[4], lineNumber);
                    return true;
                }
                else if (tokens[2] == RemarkContextEndString) {
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
                        string sectionText = document.Loader.GetSectionPassOutput(section.OutputBefore);
                        return ExtractRemarks(sectionText, function, section, options);
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
