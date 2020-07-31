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
using IRExplorer.Diff;

namespace IRExplorer {
    public class HasDiffResult {
        public HasDiffResult(IRTextSection leftSection, IRTextSection rightSection, SideBySideDiffModel model,
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

    public static class DocumentDiff {
        private static readonly char[] IgnoredDiffLetters = {
            '(', ')', ',', '.', ';', ':', '|', '{', '}', '!', ' ', '\t', '\r', '\n'
        };

        public static SideBySideDiffModel ComputeDiffs(string leftText, string rightText) {
            //? TODO: Check first if text is identical

            if (BeyondCompareDiffBuilder.HasBeyondCompare()) {
                return BeyondCompareDiffBuilder.ComputeDiffs(leftText, rightText);
            }

            var diffBuilder = new SideBySideDiffBuilder(new Differ(), IgnoredDiffLetters);
            var diff = diffBuilder.BuildDiffModel(leftText, rightText);
            return diff;
        }

        public static bool HasDiffs(SideBySideDiffModel diffModel) {
            foreach (var line in diffModel.OldText.Lines) {
                if (line.Type != ChangeType.Unchanged && line.Type != ChangeType.Imaginary) {
                    return true;
                }
            }

            return false;
        }

        public static async Task<List<HasDiffResult>> ComputeSectionDiffs(
            List<Tuple<IRTextSection, IRTextSection>> comparedSections, SectionLoader leftDocLoader,
            SectionLoader rightDocLoader) {
            int maxConcurrency = Math.Min(16, Environment.ProcessorCount);
            var tasks = new Task<HasDiffResult>[comparedSections.Count];

            await Task.Run(() => ComputeSectionDiffsImpl(comparedSections, leftDocLoader, rightDocLoader,
                                                         tasks, maxConcurrency));

            var results = new List<HasDiffResult>(tasks.Length);

            foreach (var task in tasks) {
                results.Add(task.Result);
            }

            return results;
        }

        private static async Task ComputeSectionDiffsImpl(
            List<Tuple<IRTextSection, IRTextSection>> comparedSections, SectionLoader leftDocLoader,
            SectionLoader rightDocLoader, Task<HasDiffResult>[] tasks, int maxConcurrency) {
            using var concurrencySemaphore = new SemaphoreSlim(maxConcurrency);
            int index = 0;

            foreach (var pair in comparedSections) {
                var leftSection = pair.Item1;
                var rightSection = pair.Item2;
                await concurrencySemaphore.WaitAsync();

                tasks[index++] = Task.Run(() => {
                    try {
                        string leftText = leftDocLoader.LoadSectionText(leftSection, false);
                        string rightText = rightDocLoader.LoadSectionText(rightSection, false);
                        var diffs = ComputeDiffs(leftText, rightText);
                        bool hasDiffs = HasDiffs(diffs);
                        return new HasDiffResult(leftSection, rightSection, diffs, hasDiffs);
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
