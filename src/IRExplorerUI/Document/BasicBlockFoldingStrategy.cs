// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public interface IBlockFoldingStrategy {
        FunctionIR Function { get; set; }
        public void UpdateFoldings(FoldingManager manager, TextDocument document);
    }

    public sealed class BasicBlockFoldingStrategy : IBlockFoldingStrategy {
        public BasicBlockFoldingStrategy(FunctionIR function, IRTextSection section) {
            Function = function;
            Section = section;
        }

        public FunctionIR Function { get; set; }
        public IRTextSection Section { get; set; }

        public void UpdateFoldings(FoldingManager manager, TextDocument document) {
            var newFoldings = CreateNewFoldings(document, out int firstErrorOffset);
            manager.UpdateFoldings(newFoldings, firstErrorOffset);
        }

        private IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset) {
            firstErrorOffset = -1;
            return CreateNewFoldings(document);
        }

        private IEnumerable<NewFolding> CreateNewFoldings(ITextSource document) {
            var foldings = new List<NewFolding>(Function.Blocks.Count);

            if (Section.ModuleOutput != null) {
                // With modules, collapse the text before/after the function.
                long offsetInDoc = Section.Output.DataStartOffset - Section.ModuleOutput.DataStartOffset;

                if (Section.Output.DataStartOffset > Section.ModuleOutput.DataStartOffset) {
                    foldings.Add(new NewFolding() {
                        StartOffset = 0,
                        EndOffset = (int)offsetInDoc,
                        DefaultClosed = true,
                    });
                }

                if (Section.Output.DataEndOffset < Section.ModuleOutput.DataEndOffset) {
                    foldings.Add(new NewFolding() {
                        StartOffset = (int)(offsetInDoc + Section.Output.Size),
                        EndOffset = (int)(Section.ModuleOutput.DataEndOffset - Section.ModuleOutput.DataStartOffset),
                        DefaultClosed = true,
                    });
                }
            }

            // Add foldings for regions.
            if (Function.RootRegion != null) {
                CreateRegionFoldings(Function.RootRegion, document, foldings);
            }

            // Add folding for each basic block.
            BlockIR lastBlock = null;
            int lastOffset = 0;
            int textLength = document.TextLength;
            bool sorted = true;

            foreach (var block in Function.Blocks) {
                int offset = block.TextLocation.Offset;
                int foldingLength = offset - lastOffset;

                if (lastBlock != null && foldingLength > 1 && lastBlock.Tuples.Count > 0) {
                    //? TODO: This seems to be a bug with diff mode
                    if (offset + foldingLength < textLength) {
                        foldings.Add(new NewFolding(lastOffset, offset - 2));
                    }
                }

                if (offset < lastOffset) {
                    sorted = false;
                }

                lastOffset = offset;
                lastBlock = block;
            }

            if (!sorted) {
                foldings.Sort((a, b) => a.StartOffset - b.StartOffset);
            }

            return foldings;
        }

        private void CreateRegionFoldings(RegionIR region, ITextSource document, List<NewFolding> foldings) {
            foldings.Add(new NewFolding((int)region.TextLocation.Offset,
                                        (int)(region.TextLocation.Offset + region.TextLength)));

            // foreach (var childRegion in region.ChildRegions) {
            //     CreateRegionFoldings(childRegion, document, foldings);
            // }
        }
    }
}