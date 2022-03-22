// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using IRExplorerCore;
using IRExplorerUI.Diff;

namespace IRExplorerUI {
    public enum DiffImplementationKind {
        Internal,
        External
    }

    public class DocumentDiffResult {
        public DocumentDiffResult(IRTextSection leftSection, IRTextSection rightSection, SideBySideDiffModel model,
                             bool hasDiffs) {
            LeftSection = leftSection;
            RightSection = rightSection;
            Model = model;
            HasDiffs = hasDiffs;
        }

        public IRTextSection LeftSection { get; set; }
        public IRTextSection RightSection { get; set; }
        public SideBySideDiffModel Model { get; set; }
        public bool HasDiffs { get; set; }
    }

    public class DocumentDiffBuilder {
        private static readonly char[] IgnoredDiffLetters = {
            '(', ')', ',', '.', ';', ':', '|', '{', '}', '!', ' ', '\t', '\r', '\n'
        };

        private DiffSettings settings_;

        public DocumentDiffBuilder(DiffSettings settings) {
            settings_ = settings;
        }

        public SideBySideDiffModel ComputeDiffs(string leftText, string rightText) {
            if (settings_.DiffImplementation == DiffImplementationKind.External) {
                if (!string.IsNullOrEmpty(settings_.ExternalDiffAppPath)) {
                    var result = BeyondCompareDiffBuilder.ComputeDiffs(leftText, rightText, settings_.ExternalDiffAppPath);

                    if (result != null) {
                        return result;
                    }
                }

                // Fall back to the internal diff engine if the external one failed.
            }

            return ComputeInternalDiffs(leftText, rightText);
        }

        public SideBySideDiffModel ComputeInternalDiffs(string leftText, string rightText) {
            //? TODO: Check first if text is identical
            //? - Could use a per-section hash
            var diffBuilder = new SideBySideDiffBuilder(new Differ(), IgnoredDiffLetters);
            var diff = diffBuilder.BuildDiffModel(leftText, rightText);
            return diff;
        }

        public bool HasDiffs(SideBySideDiffModel diffModel) {
            foreach (var line in diffModel.OldText.Lines) {
                if (line.Type != ChangeType.Unchanged && line.Type != ChangeType.Imaginary) {
                    return true;
                }
            }

            return false;
        }

        public async Task<List<DocumentDiffResult>> AreSectionsDifferent(
            List<(IRTextSection, IRTextSection)> comparedSections, IRTextSectionLoader leftDocLoader,
            IRTextSectionLoader rightDocLoader, ICompilerInfoProvider irInfo, bool quickMode, CancelableTask cancelableTask) {
            //? TODO: Use a "max threads" setting
            int maxConcurrency = Math.Min(16, Environment.ProcessorCount);
            var tasks = new Task<DocumentDiffResult>[comparedSections.Count];

            await Task.Run(() => AreSectionsDifferentImpl(comparedSections, leftDocLoader, rightDocLoader,
                                                         tasks, irInfo, quickMode, cancelableTask, maxConcurrency));
            var results = new List<DocumentDiffResult>(tasks.Length);

            foreach (var task in tasks) {
                results.Add(await task);
            }
            
            return results;
        }

        private async Task AreSectionsDifferentImpl(
            List<(IRTextSection, IRTextSection)> comparedSections, 
            IRTextSectionLoader leftDocLoader,
            IRTextSectionLoader rightDocLoader, 
            Task<DocumentDiffResult>[] tasks, ICompilerInfoProvider irInfo, bool quickMode,
            CancelableTask cancelableTask, int maxConcurrency) {
            var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, maxConcurrency);
            var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);
            int index = 0;

            foreach (var pair in comparedSections) {
                var leftSection = pair.Item1;
                var rightSection = pair.Item2;

                tasks[index++] = (taskFactory.StartNew(() => {
                    if (quickMode) {
                        if (leftDocLoader.SectionSignaturesComputed &&
                            rightDocLoader.SectionSignaturesComputed) {
                            if (!leftSection.IsSectionTextDifferent(rightSection)) {
                                return new DocumentDiffResult(leftSection, rightSection, null, false);
                            }
                        }

                        string leftText = leftDocLoader.GetSectionText(leftSection, false);
                        string rightText = rightDocLoader.GetSectionText(rightSection, false);
                        
                        if (leftText.Equals(rightText, StringComparison.Ordinal)) {
                            return new DocumentDiffResult(leftSection, rightSection, null, false);
                        }

                        var leftInputFilter = irInfo.CreateDiffInputFilter();
                        var rightInputFilter = irInfo.CreateDiffInputFilter();

                        if (leftInputFilter == null || rightInputFilter == null) {
                            return new DocumentDiffResult(leftSection, rightSection, null, true);
                        }

                        var leftLines = leftText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                        var rightLines = rightText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                        if (leftLines.Length != rightLines.Length) {
                            return new DocumentDiffResult(leftSection, rightSection, null, true);
                        }

                        for (int i = 0; i < leftLines.Length; i++) {
                            var leftLine = leftLines[i];
                            var rightLine = rightLines[i];

                            var leftResult = leftInputFilter.FilterInputLine(leftLine);
                            var rightResult = rightInputFilter.FilterInputLine(rightLine);

                            if (!leftResult.Equals(rightResult, StringComparison.Ordinal)) {
                                return new DocumentDiffResult(leftSection, rightSection, null, true);
                            }
                        }

                        return new DocumentDiffResult(leftSection, rightSection, null, false);
                    }
                    else {
                        string leftText = leftDocLoader.GetSectionText(leftSection, false);
                        string rightText = rightDocLoader.GetSectionText(rightSection, false);

                        var leftInputFilter = irInfo.CreateDiffInputFilter();
                        var rightInputFilter = irInfo.CreateDiffInputFilter();

                        if (leftInputFilter != null && rightInputFilter != null) {
                            var leftResult = leftInputFilter.FilterInputText(leftText);
                            var rightResult = rightInputFilter.FilterInputText(rightText);
                            leftText = leftResult.Text;
                            rightText = rightResult.Text;
                        }

                        var diffs = ComputeInternalDiffs(leftText, rightText);
                        bool hasDiffs = HasDiffs(diffs);
                        return new DocumentDiffResult(leftSection, rightSection, null, hasDiffs);
                    }
                }, cancelableTask.Token));
            }

            await Task.WhenAll(tasks);
        }
    }
}
