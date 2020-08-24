// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit.Document;
using IRExplorerCore;
using IRExplorerCore.UTC;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IRExplorerUI.Diff {
    public class DiffStatistics {
        public int LinesAdded { get; set; }
        public int LinesDeleted { get; set; }
        public int LinesModified { get; set; }

        public override string ToString() {
            if (LinesAdded == 0 && LinesDeleted == 0 && LinesModified == 0) {
                return "0 diffs";
            }

            return $"A {LinesAdded}, D {LinesDeleted}, M {LinesModified}";
        }
    }

    public class DocumentDiffUpdater {
        private const char RemovedDiffLineChar = ' ';
        private const char AddedDiffLineChar = ' ';

        private ICompilerInfoProvider compilerInfo_;
        private DiffSettings settings_;
        private IDiffOutputFilter diffFilter_;
        private char[] ignoredDiffLetters_;

        public DocumentDiffUpdater(IDiffOutputFilter diffFilter, DiffSettings settings, ICompilerInfoProvider compilerInfo) {
            diffFilter_ = diffFilter;
            settings_ = settings;
            compilerInfo_ = compilerInfo;
            ignoredDiffLetters_ = diffFilter_.IgnoredDiffLetters;
        }

        //? TODO: Split huge method
        public Task<DiffMarkingResult> MarkDiffs(string text, string otherText,
                                                 DiffPaneModel diff, DiffPaneModel otherDiff,
                                                 IRDocument textEditor, bool isRightDoc,
                                                 DiffStatistics diffStats) {
            textEditor.StartDiffSegmentAdding();
            textEditor.TextArea.IsEnabled = false;
            var section = textEditor.Section;

            return Task.Run(() => {
                // Create a new text document and associate it with the task worker.
                var document = new TextDocument(new StringTextSource(text));
                document.SetOwnerThread(Thread.CurrentThread);
                var result = new DiffMarkingResult(document);
                var modifiedSegments = new List<DiffTextSegment>(64);
                int lineCount = diff.Lines.Count;
                int lineAdjustment = 0;

                for (int i = 0; i < lineCount; i++) {
                    var line = diff.Lines[i];

                    switch (line.Type) {
                        case ChangeType.Unchanged: {
                                break; // Ignore.
                            }
                        case ChangeType.Inserted: {
                                int actualLine = line.Position.Value + lineAdjustment;
                                int offset;

                                if (actualLine >= document.LineCount) {
                                    offset = document.TextLength;
                                }
                                else {
                                    offset = document.GetOffset(actualLine, 0);
                                }

                                document.Insert(offset, line.Text + Environment.NewLine);

                                result.DiffSegments.Add(
                                    new DiffTextSegment(DiffKind.Insertion, offset, line.Text.Length));

                                diffStats.LinesAdded++;
                                break;
                            }
                        case ChangeType.Deleted: {
                                int actualLine = line.Position.Value + lineAdjustment;
                                var docLine = document.GetLineByNumber(Math.Min(document.LineCount, actualLine));

                                result.DiffSegments.Add(
                                    new DiffTextSegment(DiffKind.Deletion, docLine.Offset,
                                                        docLine.Length));

                                diffStats.LinesDeleted++;
                                break;
                            }
                        case ChangeType.Imaginary: {
                                int actualLine = i + 1;

                                if (isRightDoc) {
                                    if (actualLine <= document.LineCount) {
                                        var docLine = document.GetLineByNumber(actualLine);
                                        int offset = docLine.Offset;
                                        int length = docLine.Length;
                                        document.Replace(offset, length, new string(RemovedDiffLineChar, length));

                                        result.DiffSegments.Add(
                                            new DiffTextSegment(DiffKind.Placeholder, offset, length));
                                    }
                                }
                                else {
                                    int offset = actualLine <= document.LineCount
                                        ? document.GetOffset(actualLine, 0)
                                        : document.TextLength;

                                    string imaginaryText =
                                        new string(AddedDiffLineChar, otherDiff.Lines[i].Text.Length);

                                    document.Insert(offset, imaginaryText + Environment.NewLine);

                                    result.DiffSegments.Add(
                                        new DiffTextSegment(DiffKind.Placeholder, offset,
                                                            imaginaryText.Length));
                                }

                                lineAdjustment++;
                                break;
                            }
                        case ChangeType.Modified: {
                                int actualLine = line.Position.Value + lineAdjustment;
                                int lineChanges = 0;
                                int lineLength = 0;
                                bool wholeLineReplaced = false;

                                if (actualLine < document.LineCount) {
                                    var docLine = document.GetLineByNumber(actualLine);
                                    document.Replace(docLine.Offset, docLine.Length, line.Text);
                                    wholeLineReplaced = true;
                                    lineLength = docLine.Length;
                                }

                                modifiedSegments.Clear();
                                int column = 0;
                                int otherColumn = 0;

                                foreach (var piece in line.SubPieces) {
                                    switch (piece.Type) {
                                        case ChangeType.Inserted: {
                                                Debug.Assert(isRightDoc);

                                                int offset = actualLine >= document.LineCount
                                                    ? document.TextLength
                                                    : document.GetOffset(actualLine, 0) + column;

                                                if (offset >= document.TextLength) {
                                                    if (!wholeLineReplaced) {
                                                        document.Insert(document.TextLength, piece.Text);
                                                    }

                                                    if (IsSignifficantDiff(piece)) {
                                                        modifiedSegments.Add(new DiffTextSegment(DiffKind.Modification, offset, piece.Text.Length));
                                                    }
                                                }
                                                else {
                                                    var diffKind = DiffKind.Insertion;
                                                    var otherPiece = FindPieceInOtherDocument(otherDiff, i, piece, 0);

                                                    if (otherPiece != null && otherPiece.Type == ChangeType.Deleted) {
                                                        if (!wholeLineReplaced) {
                                                            document.Replace(offset, otherPiece.Text.Length, piece.Text);
                                                        }

                                                        if (wholeLineReplaced) {
                                                            var diffLine = line.Text;
                                                            var otherDiffLine = otherDiff.Lines[i].Text;
                                                            int otherPieceOffset = otherColumn;
                                                            int pieceOffset = column;
                                                            diffKind = EstimateModificationType(piece, otherPiece, pieceOffset, otherPieceOffset, diffLine, otherDiffLine);
                                                        }
                                                    }
                                                    else {
                                                        if (!wholeLineReplaced) {
                                                            document.Insert(offset, piece.Text);
                                                        }
                                                    }

                                                    if (otherPiece != null) {
                                                        otherColumn += otherPiece.Text.Length;
                                                    }

                                                    if (IsSignifficantDiff(piece)) {
                                                        var filteredPiece = diffFilter_.AdjustChange(piece, offset, column, line.Text);
                                                        AppendModificationChange(modifiedSegments, diffKind, filteredPiece);
                                                    }

                                                }
                                                break;
                                            }
                                        case ChangeType.Deleted: {
                                                Debug.Assert(!isRightDoc);
                                                int offset = document.GetOffset(actualLine, 0) + column;

                                                if (offset >= document.TextLength) {
                                                    offset = document.TextLength;
                                                }

                                                var diffKind = DiffKind.Deletion;
                                                var otherPiece = FindPieceInOtherDocument(otherDiff, i, piece, 0);

                                                if (otherPiece != null && otherPiece.Type == ChangeType.Inserted) {
                                                    if (wholeLineReplaced) {
                                                        var diffLine = line.Text;
                                                        var otherDiffLine = otherDiff.Lines[i].Text;
                                                        int otherPieceOffset = otherColumn;
                                                        int pieceOffset = column;
                                                        diffKind = EstimateModificationType(piece, otherPiece, pieceOffset, otherPieceOffset, diffLine, otherDiffLine);
                                                    }
                                                }

                                                if (IsSignifficantDiff(piece)) {
                                                    var filteredPiece = diffFilter_.AdjustChange(piece, offset, column, line.Text);
                                                    AppendModificationChange(modifiedSegments, diffKind, filteredPiece);
                                                }

                                                if (otherPiece != null) {
                                                    otherColumn += otherPiece.Text.Length;
                                                }
                                                break;
                                            }
                                        case ChangeType.Modified:
                                        case ChangeType.Imaginary: {
                                                break; // Nothing to do here.
                                            }
                                        case ChangeType.Unchanged: {
                                                if (!wholeLineReplaced) {
                                                    int offset = document.GetOffset(actualLine, 0) + column;
                                                    document.Replace(offset, piece.Text.Length, piece.Text);
                                                }

                                                var otherPiece = FindPieceInOtherDocument(otherDiff, i, piece, 0);

                                                if (otherPiece != null) {
                                                    otherColumn += piece.Text.Length;
                                                }

                                                break;
                                            }
                                        default:
                                            throw new ArgumentOutOfRangeException("Unexpected change type!");
                                    }

                                    if (piece.Text != null) {
                                        column += piece.Text.Length;

                                        if (piece.Type != ChangeType.Unchanged) {
                                            lineChanges += piece.Text.Length;
                                        }
                                    }
                                }

                                // If most of the line changed, mark the entire line,
                                // otherwise mark each sub-piece.
                                bool handled = false;

                                if (settings_.ManyDiffsMarkWholeLine) {
                                    double percentChanged = 0;

                                    if (lineLength > 0) {
                                        percentChanged = ((double)lineChanges / (double)lineLength) * 100;
                                    }

                                    if (percentChanged > settings_.ManyDiffsModificationPercentage) {
                                        if (actualLine < document.LineCount) {
                                            var docLine = document.GetLineByNumber(actualLine);
                                            var changeKind = DiffKind.Modification;

                                            // If even more of the line changed, consider it an insertion/deletion.
                                            if (percentChanged > settings_.ManyDiffsInsertionPercentage) {
                                                changeKind = isRightDoc ? DiffKind.Insertion : DiffKind.Deletion;
                                            }

                                            result.DiffSegments.Add(new DiffTextSegment(changeKind, docLine.Offset, docLine.Length));
                                            handled = true;
                                        }
                                    }
                                }

                                if (!handled) {
                                    foreach (var segment in modifiedSegments) {
                                        result.DiffSegments.Add(segment);
                                    }
                                }

                                if (isRightDoc) {
                                    diffStats.LinesModified++;
                                }

                                break;
                            }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                result.DiffText = document.Text;
                document.SetOwnerThread(null);
                ReparseDiffedFunction(result, section);
                return result;
            });
        }

        private static void AppendModificationChange(List<DiffTextSegment> modifiedSegments, DiffKind diffKind, AdjustedDiffPiece filteredPiece) {
            if (modifiedSegments.Count > 0) {
                var lastSegment = modifiedSegments[^1];
                if (lastSegment.StartOffset == filteredPiece.Offset &&
                    lastSegment.Length == filteredPiece.Length) {
                    return;
                }
            }

            modifiedSegments.Add(new DiffTextSegment(diffKind, filteredPiece.Offset, filteredPiece.Length));
        }

        private DiffPiece FindPieceInOtherDocument(DiffPaneModel otherDiff, int lineIndex,
                                                   DiffPiece piece, int piecePossitionOffset) {
            if (lineIndex < otherDiff.Lines.Count) {
                var otherLine = otherDiff.Lines[lineIndex];
                int position = piece.Position.Value + piecePossitionOffset;

                if (position < otherLine.SubPieces.Count) {
                    return otherLine.SubPieces.Find(item => item.Position.HasValue &&
                                                            item.Position.Value == position);
                }
            }

            return null;
        }

        private bool IsSignifficantDiff(DiffPiece piece, DiffPiece otherPiece = null) {
            if (!settings_.FilterInsignificantDiffs) {
                return true;
            }

            if (piece.Text == null) {
                return false;
            }

            bool signifficant = false;

            foreach (char letter in piece.Text) {
                if (!char.IsWhiteSpace(letter) && Array.IndexOf(ignoredDiffLetters_, letter) == -1) {
                    signifficant = true;
                    break;
                }
            }

            return signifficant;
        }

        private DiffKind EstimateModificationType(DiffPiece before, DiffPiece after,
                                                  int beforeOffset, int afterOffset,
                                                  string beforeDocumentText, string afterDocumentText) {
            if (!settings_.IdentifyMinorDiffs) {
                return DiffKind.Modification;
            }

            return diffFilter_.EstimateModificationType(before, after, beforeOffset, afterOffset,
                                                        beforeDocumentText, afterDocumentText);
        }

        private void ReparseDiffedFunction(DiffMarkingResult diffResult, IRTextSection originalSection) {
            try {
                var errorHandler = compilerInfo_.IR.CreateParsingErrorHandler();
                var sectionParser = compilerInfo_.IR.CreateSectionParser(errorHandler);

                diffResult.DiffFunction = sectionParser.ParseSection(originalSection, diffResult.DiffText);

                if (diffResult.DiffFunction != null) {
                    compilerInfo_.AnalyzeLoadedFunction(diffResult.DiffFunction);
                }
                else {
                    Trace.TraceWarning("Failed re-parsing diffed section\n");
                }

                if (errorHandler.HadParsingErrors) {
                    Trace.TraceWarning("Errors while re-parsing diffed section:\n");

                    if (errorHandler.ParsingErrors != null) {
                        foreach (var error in errorHandler.ParsingErrors) {
                            Trace.TraceWarning($"  - {error}");
                        }
                    }
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Crashed while re-parsing diffed section: {ex}");
            }
        }
    }
}
