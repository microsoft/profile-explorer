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

        public async Task<List<DocumentDiffResult>> ComputeSectionDiffs(
            List<Tuple<IRTextSection, IRTextSection>> comparedSections, IRTextSectionLoader leftDocLoader,
            IRTextSectionLoader rightDocLoader) {
            int maxConcurrency = Math.Min(16, Environment.ProcessorCount);
            var tasks = new Task<DocumentDiffResult>[comparedSections.Count];

            leftDocLoader.SuspendCaching();
            rightDocLoader.SuspendCaching();
            await Task.Run(() => ComputeSectionDiffsImpl(comparedSections, leftDocLoader, rightDocLoader,
                                                         tasks, maxConcurrency));

            var results = new List<DocumentDiffResult>(tasks.Length);

            foreach (var task in tasks) {
                results.Add(await task);
            }

            leftDocLoader.ResumeCaching();
            rightDocLoader.ResumeCaching();
            return results;
        }

        private async Task ComputeSectionDiffsImpl(
            List<Tuple<IRTextSection, IRTextSection>> comparedSections, IRTextSectionLoader leftDocLoader,
            IRTextSectionLoader rightDocLoader, Task<DocumentDiffResult>[] tasks, int maxConcurrency) {
            using var concurrencySemaphore = new SemaphoreSlim(maxConcurrency);
            int index = 0;

            foreach (var pair in comparedSections) {
                var leftSection = pair.Item1;
                var rightSection = pair.Item2;
                await concurrencySemaphore.WaitAsync();

                tasks[index++] = Task.Run(() => {
                    try {
                        //? TODO: Check SHA first, then compare text if no SHA
                        string leftText = leftDocLoader.GetSectionText(leftSection, false);
                        string rightText = rightDocLoader.GetSectionText(rightSection, false);
                        var diffs = ComputeInternalDiffs(leftText, rightText);
                        bool hasDiffs = HasDiffs(diffs);
                        return new DocumentDiffResult(leftSection, rightSection, diffs, hasDiffs);
                    }
                    finally {
                        concurrencySemaphore.Release();
                    }
                });
            }

            await Task.WhenAll(tasks);
        }
    }
}
