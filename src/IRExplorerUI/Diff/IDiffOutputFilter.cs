// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using DiffPlex.DiffBuilder.Model;
using IRExplorerCore;

namespace IRExplorerUI.Diff {
    public struct AdjustedDiffPiece {
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

    public interface IDiffInputFilter {
        void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo);
        (string, List<string> linePrefixes) FilterInputText(string text);
    }

    public class BasicDiffOutputFilter : IDiffOutputFilter {
        public char[] IgnoredDiffLetters => new char[] {
            '(', ')', ',', '.', ';', ':', '|', '{', '}', '!', ' ', '\t'
        };

        public AdjustedDiffPiece AdjustChange(DiffPiece change, int documentOffset, int lineOffset, string lineText) {
            return new AdjustedDiffPiece(documentOffset, change.Text.Length);
        }

        public DiffKind EstimateModificationType(DiffPiece before, DiffPiece after, int beforeOffset, int afterOffset, string beforeDocumentText, string afterDocumentText) {
            return DiffKind.Modification;
        }

        public void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo) {
            
        }
    }
}
