// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public interface IBlockFoldingStrategy {
        FunctionIR Function { get; set; }
        public void UpdateFoldings(FoldingManager manager, TextDocument document);
    }

    public sealed class BaseBlockFoldingStrategy : IBlockFoldingStrategy {
        public BaseBlockFoldingStrategy(FunctionIR function) {
            Function = function;
        }

        public FunctionIR Function { get; set; }

        public void UpdateFoldings(FoldingManager manager, TextDocument document) {
            var newFoldings = CreateNewFoldings(document, out int firstErrorOffset);
            manager.UpdateFoldings(newFoldings, firstErrorOffset);
        }

        private IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset) {
            firstErrorOffset = -1;
            return CreateNewFoldings(document);
        }

        private IEnumerable<NewFolding> CreateNewFoldings(ITextSource document) {
            var newFoldings = new List<NewFolding>(Function.Blocks.Count);
            BlockIR lastBlock = null;
            int lastOffset = 0;
            bool sorted = true;

            foreach (var block in Function.Blocks) {
                int offset = block.TextLocation.Offset;
                int foldingLength = offset - lastOffset;

                if (lastBlock != null && foldingLength > 1 && lastBlock.Tuples.Count > 0) {
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
