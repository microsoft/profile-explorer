// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using Core.IR;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace Client {
    //? TODO: Introduce interface
    public sealed class UTCFoldingStrategy {
        public FunctionIR Function { get; set; }

        public UTCFoldingStrategy(FunctionIR function) {
            Function = function;
        }

        public void UpdateFoldings(FoldingManager manager, TextDocument document) {
            int firstErrorOffset;
            IEnumerable<NewFolding> newFoldings = CreateNewFoldings(document, out firstErrorOffset);
            manager.UpdateFoldings(newFoldings, firstErrorOffset);
        }

        public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset) {
            firstErrorOffset = -1;
            return CreateNewFoldings(document);
        }

        public IEnumerable<NewFolding> CreateNewFoldings(ITextSource document) {
            List<NewFolding> newFoldings = new List<NewFolding>(Function.Blocks.Count);
            BlockIR lastBlock = null;
            int lastOffset = 0;
            bool sorted = true;

            foreach (var block in Function.Blocks) {
                int offset = block.TextLocation.Offset;
                int foldingLength = offset - lastOffset;

                if (lastBlock != null && foldingLength > 1 &&
                    lastBlock.Tuples.Count > 0) {
                    newFoldings.Add(new NewFolding(lastOffset, offset - 2));
                }

                if (offset < lastOffset) {
                    sorted = false;
                }

                lastOffset = offset;
                lastBlock = block;
            }

            if (!sorted) {
                newFoldings.Sort((a, b) => a.StartOffset - b.StartOffset);
            }

            return newFoldings;
        }
    }
}
