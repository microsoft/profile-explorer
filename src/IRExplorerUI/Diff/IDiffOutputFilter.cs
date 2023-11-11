﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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

    public class FilteredDiffInput {
        public struct Replacement {
            public Replacement(int offset, string replaced, string original) {
                Offset = offset;
                Replaced = replaced;
                Original = original;
            }

            public int Offset { get; set; }
            public string Replaced { get; set; }
            public string Original { get; set; }

            public int Length => Replaced.Length;
        }
        
        public string Text { get; set; }
        public List<List<Replacement>> LineReplacements { get; set; }

        public static List<Replacement> NoReplacements = new List<Replacement>(0);

        public FilteredDiffInput(int capacity) {
            Text = string.Empty;
            LineReplacements = new List<List<Replacement>>(capacity);
        }

        public FilteredDiffInput(string text) {
            Text = text;
            LineReplacements = null;
        }
    }

    public interface IDiffInputFilter {
        void Initialize(DiffSettings settings, ICompilerIRInfo ifInfo);
        FilteredDiffInput FilterInputText(string text);
        string FilterInputLine(string line);
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
