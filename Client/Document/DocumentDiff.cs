// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace Client {
    public class HasDiffResult {
        public HasDiffResult(IRTextSection leftSection, IRTextSection rightSection,
                             SideBySideDiffModel model, bool hasDiffs) {
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
        public static async Task<SideBySideDiffModel>
            ComputeDiffs(string leftText, string rightText) {
            //? TODO: Check first if text is identical

            var diffBuilder = new SideBySideDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(leftText, rightText,
                                                  ignoreWhitespace: true);
            return diff;
        }

        public static bool HasDiffs(SideBySideDiffModel diffModel) {
            for (int i = 0; i < diffModel.OldText.Lines.Count; i++) {
                var line = diffModel.OldText.Lines[i];

                if (line.Type != ChangeType.Unchanged &&
                    line.Type != ChangeType.Imaginary) {
                    return true;
                }
            }

            return false;
        }

        public static async Task<List<HasDiffResult>>
        ComputeSectionDiffs(List<Tuple<IRTextSection, IRTextSection>> comparedSections,
                            DocumentSectionLoader leftDocLoader,
                            DocumentSectionLoader rightDocLoader) {
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

        private static async Task
        ComputeSectionDiffsImpl(List<Tuple<IRTextSection, IRTextSection>> comparedSections,
                                DocumentSectionLoader leftDocLoader,
                                DocumentSectionLoader rightDocLoader,
                                Task<HasDiffResult>[] tasks, int maxConcurrency) {
            using (SemaphoreSlim concurrencySemaphore = new SemaphoreSlim(maxConcurrency)) {
                int index = 0;

                foreach (var pair in comparedSections) {
                    var leftSection = pair.Item1;
                    var rightSection = pair.Item2;
                    concurrencySemaphore.Wait();

                    tasks[index++] = Task.Run<HasDiffResult>(() => {
                        try {
                            var leftText = leftDocLoader.LoadSectionText(leftSection, useCache: false);
                            var rightText = rightDocLoader.LoadSectionText(rightSection, useCache: false);
                            var diffs = ComputeDiffs(leftText, rightText).Result;
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
}
