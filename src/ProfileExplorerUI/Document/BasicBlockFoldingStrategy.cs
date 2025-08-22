// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ProfileExplorerCore2.IR;

namespace ProfileExplorer.UI;

public interface IBlockFoldingStrategy {
  public void UpdateFoldings(FoldingManager manager, TextDocument document);
}

public sealed class BasicBlockFoldingStrategy : IBlockFoldingStrategy {
  private FunctionIR function_;

  public BasicBlockFoldingStrategy(FunctionIR function) {
    function_ = function;
  }

  public void UpdateFoldings(FoldingManager manager, TextDocument document) {
    var newFoldings = CreateNewFoldings(document, out int firstErrorOffset);
    manager.UpdateFoldings(newFoldings, firstErrorOffset);
  }

  private IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset) {
    firstErrorOffset = -1;
    return CreateNewFoldings(document);
  }

  private IEnumerable<NewFolding> CreateNewFoldings(ITextSource document) {
    var newFoldings = new List<NewFolding>(function_.Blocks.Count);

    if (function_.Blocks.Count == 0) {
      return newFoldings;
    }

    BlockIR lastBlock = null;
    int lastOffset = 0;
    int textLength = document.TextLength;

    foreach (var block in function_.Blocks) {
      int offset = block.TextLocation.Offset;
      int foldingLength = offset - lastOffset;

      if (lastBlock != null && foldingLength > 1) {
        //? TODO: This seems to be a bug with diff mode
        int endOffset = Math.Min(offset, textLength - 1);

        if (endOffset > lastOffset) {
          newFoldings.Add(new NewFolding(lastOffset, endOffset - 2));
        }
      }

      lastOffset = offset;
      lastBlock = block;
    }

    // Handle the last block.
    if (lastOffset < textLength - 1) {
      int endOffset = Math.Min(lastOffset + function_.Blocks[^1].TextLength, textLength - 1);

      if (endOffset > lastOffset) {
        newFoldings.Add(new NewFolding(lastOffset, endOffset - 2));
      }
    }

    return newFoldings;
  }
}