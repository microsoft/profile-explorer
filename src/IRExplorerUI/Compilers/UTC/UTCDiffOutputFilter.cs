// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerUI.Diff;
using DiffPlex.DiffBuilder.Model;
using System;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.UTC;

namespace IRExplorerUI.UTC {
    public class UTCDiffOutputFilter : IDiffOutputFilter {
        public char[] IgnoredDiffLetters => new char[] {
            '(', ')', ',', '.', ';', ':', '|', '{', '}', '!', ' ', '\t'
        };

        private char[] ExpansionStopLetters => new char[] {
            '(', ')', '<', '>', ',', '.', ';', ':', '|', '[', ']', '{', '}', '!', ' ', '\t'
        };

        private DiffSettings settings_;
        private ICompilerIRInfo compilerInfo_;

        public void Initialize(DiffSettings settings, ICompilerIRInfo compilerInfo) {
            settings_ = settings;
            compilerInfo_ = compilerInfo;
        }

        public DiffKind EstimateModificationType(DiffPiece before, DiffPiece after, int beforeOffset, int afterOffset,
                                                 string beforeDocumentText, string afterDocumentText) {
            string beforeText = ExpandDiff(before.Text, beforeOffset, beforeDocumentText,
                                           out int beforeLeftStopIndex, out int beforeRightStopIndex);
            string afterText = ExpandDiff(after.Text, afterOffset, afterDocumentText,
                                          out int afterLeftStopIndex, out int afterRightStopIndex);

            if (IsTemporaryVariable(beforeText, out int beforeNumber) &&
                IsTemporaryVariable(afterText, out int afterNumber)) {

                if (beforeNumber == afterNumber) {
                    return DiffKind.MinorModification;
                }
                else
                    return DiffKind.MinorModification;
            }
            else if (IsSSANumber(beforeText, beforeLeftStopIndex, beforeRightStopIndex, beforeDocumentText) &&
                     IsSSANumber(afterText, afterLeftStopIndex, afterRightStopIndex, afterDocumentText)) {
                return DiffKind.MinorModification;
            }
            else if (IsEquivSymbolNumber(beforeText, beforeLeftStopIndex, beforeRightStopIndex, beforeDocumentText) &&
                     IsEquivSymbolNumber(afterText, afterLeftStopIndex, afterRightStopIndex, afterDocumentText)) {
                return DiffKind.MinorModification;
            }
            else if (IsEHRegionAnnotation(beforeText, beforeLeftStopIndex, beforeRightStopIndex, beforeDocumentText) &&
                     IsEHRegionAnnotation(afterText, afterLeftStopIndex, afterRightStopIndex, afterDocumentText)) {
                return DiffKind.MinorModification;
            }
            //? TODO: Doesn't always mark line numbers, and for calls it can mark
            //? diffs after the opcode as comments for OPCALL(#ID) ...
            //else if (IsCommentText(beforeText, beforeLeftStopIndex, beforeRightStopIndex, beforeDocumentText) ||
            //         IsCommentText(afterText, afterLeftStopIndex, afterRightStopIndex, afterDocumentText)) {
            //    return DiffKind.MinorModification;
            //}

            return DiffKind.Modification;
        }

        public AdjustedDiffPiece AdjustChange(DiffPiece change, int offset,
                                              int lineOffset, string lineText) {
            string text = ExpandDiff(change.Text, lineOffset, lineText,
                                     out int leftStopIndex, out int rightStopIndex);

            if (IsTemporaryVariable(text, out var _) ||
                UTCOpcodes.IsOpcode(text)) {
                // Enlarge diff marking to cover entire variable/opcode.
                int lineStartOffset = offset - lineOffset;
                return new AdjustedDiffPiece(lineStartOffset + leftStopIndex, text.Length);
            }
            else if (IsSSANumber(text, leftStopIndex, rightStopIndex, lineText)) {
                // Enlarge diff to include entire SSA number <*123> instead of just some digits.
                if (lineText[leftStopIndex] != '<' && leftStopIndex > 0) {
                    leftStopIndex--;
                }

                if (lineText[rightStopIndex] != '>' && rightStopIndex < lineText.Length - 1) {
                    rightStopIndex++;
                }

                int lineStartOffset = offset - lineOffset;
                return new AdjustedDiffPiece(lineStartOffset + leftStopIndex, rightStopIndex - leftStopIndex + 1);
            }

            return new AdjustedDiffPiece(offset, change.Text.Length);
        }

        private bool IsTemporaryVariable(string text, out int tempNumber) {
            tempNumber = 0;
            var name = text.AsSpan();
            int prefixLength = 0;

            if (name.StartsWith("tv".AsSpan()) || name.StartsWith("hv".AsSpan())) {
                prefixLength = 2;
            }
            else if (name.StartsWith("t".AsSpan())) {
                prefixLength = 1;
            }
            else {
                return false;
            }

            var remainingName = name.Slice(prefixLength);
            int index = 0;

            while (index < remainingName.Length &&
                   char.IsDigit(remainingName[index])) {
                index++;
            }

            if (index < remainingName.Length) {
                if (Array.IndexOf(ExpansionStopLetters, remainingName[index]) != -1) {
                    remainingName = remainingName.Slice(0, index);
                }
                else {
                    return false;
                }
            }

            return int.TryParse(remainingName, out tempNumber);
        }

        private bool IsSSANumber(string text, int leftStopIndex, int rightStopIndex, string lineText) {
            if (rightStopIndex < 0 || rightStopIndex >= lineText.Length) {
                text = text;
            }

            bool hasSSANumberStart = lineText[leftStopIndex] == '<' ||
                                     lineText[leftStopIndex] == '*' ||
                                     char.IsDigit(lineText[leftStopIndex]);
            bool hasSSANumberEnd = lineText[rightStopIndex] == '>' ||
                                   lineText[rightStopIndex] == 'l' ||
                                   lineText[rightStopIndex] == 'r';
            if (hasSSANumberStart && hasSSANumberEnd) {
                leftStopIndex += !char.IsDigit(lineText[leftStopIndex]) ? 1 : 0;
                var defNumber = lineText.Substring(leftStopIndex, rightStopIndex - leftStopIndex - 1);
                return int.TryParse(defNumber, out var _);
            }

            return false;
        }

        private bool IsEquivSymbolNumber(string text, int leftStopIndex, int rightStopIndex, string lineText) {
            if (!IsNumber(text)) {
                return false;
            }

            if (leftStopIndex > 0) {
                return lineText[leftStopIndex - 1] == ':';
            }

            return true;
        }

        private bool IsEHRegionAnnotation(string text, int leftStopIndex, int rightStopIndex, string lineText) {
            if (leftStopIndex != 0) {
                return false;
            }

            switch (text) {
                case "":
                case "r": {
                        return true;
                    }
                default:
                    break;
            }

            return false;
        }

        private bool IsCommentText(string text, int leftStopIndex, int rightStopIndex, string lineText) {
            // Everything following # is debug info and line numbers.
            if (lineText.LastIndexOf('#', leftStopIndex) != -1) {
                text = text;
            }
            return lineText.LastIndexOf('#', leftStopIndex) != -1;
        }


        private bool IsNumber(string text) {
            foreach (char letter in text) {
                if (!char.IsDigit(letter)) {
                    return false;
                }
            }

            return true;
        }

        private string ExpandDiff(string diffText, int offset, string text,
                                  out int leftStopIndex, out int rightStopIndex) {
            if (diffText.Length == 0 ||
                offset + diffText.Length >= text.Length) {
                leftStopIndex = rightStopIndex = 0;
                return diffText;
            }

            // Expand left/right as long no end marker letters are found.
            int left = offset;
            int right = offset + diffText.Length - 1;

            while (left > 0) {
                if (Array.IndexOf(ExpansionStopLetters, text[left - 1]) != -1) {
                    break;
                }

                left--;
            }

            while (right < text.Length - 1) {
                if (Array.IndexOf(ExpansionStopLetters, text[right + 1]) != -1) {
                    break;
                }

                right++;
            }

            leftStopIndex = left;
            rightStopIndex = right;
            return text.Substring(left, right - left + 1);
        }
    }
}
