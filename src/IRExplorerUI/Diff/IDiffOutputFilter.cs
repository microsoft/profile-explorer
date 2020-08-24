// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using DiffPlex.DiffBuilder.Model;
using IRExplorerCore;

namespace IRExplorerUI.Diff {
    public class AdjustedDiffPiece {
        public AdjustedDiffPiece(int offset, int length) {
            Offset = offset;
            Length = length;
        }

        public int Offset { get; set; }
        public int Length { get; set; }
    }

    public interface IDiffOutputFilter {
        char[] IgnoredDiffLetters { get; }

        void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo);
        DiffKind EstimateModificationType(DiffPiece before, DiffPiece after, int beforeOffset, int afterOffset,
                                          string beforeDocumentText, string afterDocumentText);
        AdjustedDiffPiece AdjustChange(DiffPiece change, int offset, int lineOffset, string lineText);
    }
}
