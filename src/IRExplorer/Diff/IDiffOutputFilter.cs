// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using DiffPlex.DiffBuilder.Model;

namespace IRExplorer.Diff {
    public interface IDiffOutputFilter {
        char[] IgnoredDiffLetters { get; }
        DiffKind EstimateModificationType(DiffPiece before, DiffPiece after);
    }
}
