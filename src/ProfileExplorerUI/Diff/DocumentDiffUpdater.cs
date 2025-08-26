// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DiffPlex.DiffBuilder.Model;
using ICSharpCode.AvalonEdit.Document;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Diff;
using ProfileExplorer.Core.Document.Renderers.Highlighters;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.Diff;

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

  public DocumentDiffUpdater(IDiffOutputFilter diffFilter, DiffSettings settings,
                             ICompilerInfoProvider compilerInfo) {
    diffFilter_ = diffFilter;
    settings_ = settings;
    compilerInfo_ = compilerInfo;
    ignoredDiffLetters_ = diffFilter_.IgnoredDiffLetters;
  }

  public DiffMarkingResult CreateNoDiffDocument(string text) {
    var document = new TextDocument(new StringTextSource(text));
    document.SetOwnerThread(Thread.CurrentThread);

    var result = new DiffMarkingResult(document);
    result.DiffText = text;

    document.SetOwnerThread(null);
    return result;
  }

  public DiffMarkingResult MarkDiffs(string text, string otherText,
                                     DiffPaneModel diff, DiffPaneModel otherDiff,
                                     bool isRightDoc, FilteredDiffInput filteredInput,
                                     DiffStatistics diffStats,
                                     bool markRightDocDeletion = false) {
    // Create a new text document and associate it with the task worker.
    var document = new TextDocument(new StringTextSource(text));
    document.SetOwnerThread(Thread.CurrentThread);

    var result = new DiffMarkingResult(document);
    int lineCount = diff.Lines.Count;
    int lineAdjustment = 0;

    for (int lineIndex = 0; lineIndex < lineCount; lineIndex++) {
      var line = diff.Lines[lineIndex];

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
          AppendInsertionChange(diffStats, result, line, offset);
          break;
        }
        case ChangeType.Deleted: {
          int actualLine = line.Position.Value + lineAdjustment;
          var docLine = document.GetLineByNumber(Math.Min(document.LineCount, actualLine));

          AppendDeletionChange(diffStats, result, docLine);
          break;
        }
        case ChangeType.Imaginary: {
          int docLineIndex = lineIndex + 1;

          if (isRightDoc) {
            // Mark the lines that have been removed on the right side.
            if (docLineIndex <= document.LineCount) {
              var docLine = document.GetLineByNumber(docLineIndex);

              if (markRightDocDeletion) {
                // Show the actual text that has been deleted.
                result.DiffSegments.Add(
                  new DiffTextSegment(DiffKind.Deletion, docLine.Offset, docLine.Length));
              }
              else {
                document.Replace(docLine.Offset, docLine.Length,
                                 new string(RemovedDiffLineChar, docLine.Length));
                result.DiffSegments.Add(new DiffTextSegment(DiffKind.Placeholder, docLine.Offset, docLine.Length));
              }
            }
          }
          else {
            // Add a placeholder in the left side to mark
            // the lines inserted on the right side.
            int offset = docLineIndex <= document.LineCount
              ? document.GetOffset(docLineIndex, 0)
              : document.TextLength;

            string imaginaryText = new(AddedDiffLineChar, otherDiff.Lines[lineIndex].Text.Length);

            document.Insert(offset, imaginaryText + Environment.NewLine);
            result.DiffSegments.Add(new DiffTextSegment(DiffKind.Placeholder, offset,
                                                        imaginaryText.Length));
          }

          lineAdjustment++;
          break;
        }
        case ChangeType.Modified: {
          MarkLineModificationDiffs(line, lineIndex, lineAdjustment,
                                    document, isRightDoc, otherDiff,
                                    result, diffStats);

          break;
        }
        default:
          throw new ArgumentOutOfRangeException();
      }

      // If the input text had parts replaced as a form of canonicalization,
      // replace those with the original text.
      if (filteredInput != null && line.Type != ChangeType.Imaginary) {
        int filteredLine = lineIndex - lineAdjustment;
        int docLineIndex = lineIndex + 1;

        if (filteredLine < filteredInput.LineReplacements.Count &&
            docLineIndex <= document.LineCount) {
          var replacements = filteredInput.LineReplacements[filteredLine];
          var docLine = document.GetLineByNumber(docLineIndex);

          foreach (var replacement in replacements) {
            int docLineOffset = docLine.Offset + replacement.Offset;

            if (docLineOffset + replacement.Length < document.TextLength) {
              document.Replace(docLineOffset, replacement.Length, replacement.Original);
            }
          }
        }
      }
    }

    result.DiffText = document.Text;
    document.SetOwnerThread(null);
    return result;
  }

  public async Task ReparseDiffedFunction(DiffMarkingResult diffResult,
                                          IRTextSection originalSection) {
    try {
      var errorHandler = compilerInfo_.IR.CreateParsingErrorHandler();
      var sectionParser = compilerInfo_.IR.CreateSectionParser(errorHandler);
      diffResult.DiffFunction = sectionParser.ParseSection(originalSection, diffResult.DiffText);

      if (diffResult.DiffFunction != null) {
        await compilerInfo_.AnalyzeLoadedFunction(diffResult.DiffFunction, originalSection);
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
      diffResult.DiffFunction = new FunctionIR();
    }
  }

  private void MarkLineModificationDiffs(DiffPiece line, int lineIndex, int lineAdjustment,
                                         TextDocument document, bool isRightDoc,
                                         DiffPaneModel otherDiff, DiffMarkingResult result,
                                         DiffStatistics diffStats) {
    int actualLine = line.Position.Value + lineAdjustment;
    int lineChanges = 0;
    int lineLength = 0;
    bool wholeLineReplaced = false;

    if (actualLine < document.LineCount) {
      // Use the modified line instead; below the segments
      // of the line that were changed (the sub-pieces) are marked.
      var docLine = document.GetLineByNumber(actualLine);
      document.Replace(docLine.Offset, docLine.Length, line.Text);
      wholeLineReplaced = true;
      lineLength = docLine.Length;
    }

    var modifiedSegments = new List<DiffTextSegment>();
    int column = 0;
    int otherColumn = 0;

    //? TODO: This is an ugly hack to get the two piece lists aligned
    //? for Beyond Compare in case there is a word inserted at the line start
    //? like in "r t100" vs "  t100" - the BC diff builder should match DiffPlex
    //? and insert a dummy whitespace diff corresponding to "r" instead two whitespaces in "  t100",
    //? which would create the same number of diffs on both sides
    var pieces = line.SubPieces;
    var otherPieces = otherDiff.Lines[lineIndex].SubPieces;
    int pieceIndexAdjustment = 0;

    if (pieces[0].Type != otherPieces[0].Type) {
      if (isRightDoc) {
        if (otherPieces.Count > 1 &&
            otherPieces[1].Type == pieces[0].Type &&
            pieces[0].Text.EndsWith(otherPieces[1].Text)) {
          otherColumn += otherPieces[0].Text.Length;
          pieceIndexAdjustment = 1;
        }
      }
      else {
        if (pieces.Count > 1 &&
            otherPieces[0].Type == pieces[1].Type &&
            otherPieces[0].Text.EndsWith(pieces[1].Text)) {
          pieceIndexAdjustment = -1;
        }
      }
    }

    foreach (var piece in line.SubPieces) {
      switch (piece.Type) {
        case ChangeType.Inserted: {
          Debug.Assert(isRightDoc);

          int offset = actualLine >= document.LineCount
            ? document.TextLength
            : document.GetOffset(actualLine, 0) + column;

          if (offset >= document.TextLength) {
            // Text inserted at the end of the line and document.
            if (!wholeLineReplaced) {
              document.Insert(document.TextLength, piece.Text);
            }

            if (IsSignifficantDiff(piece)) {
              modifiedSegments.Add(new DiffTextSegment(DiffKind.Modification,
                                                       offset, piece.Text.Length));
            }
          }
          else {
            // Check if this insertion has an equivalent deletion on the other side.
            // If it does, try to mark it as a modification.
            var diffKind = DiffKind.Insertion;
            var otherPiece = FindPieceInOtherDocument(otherDiff, lineIndex, piece, pieceIndexAdjustment);

            if (otherPiece != null && otherPiece.Type == ChangeType.Deleted) {
              if (!wholeLineReplaced) {
                document.Replace(offset, otherPiece.Text.Length, piece.Text);
              }

              if (wholeLineReplaced) {
                string diffLine = line.Text;
                string otherDiffLine = otherDiff.Lines[lineIndex].Text;
                int otherPieceOffset = otherColumn;
                int pieceOffset = column;
                diffKind = EstimateModificationType(piece, otherPiece,
                                                    pieceOffset, otherPieceOffset,
                                                    diffLine, otherDiffLine);
              }
            }
            else {
              // Try again to find a piece that matches the same offset.
              otherPiece = FindOverlappingPieceInOtherDocument(otherDiff, lineIndex, column);

              if (otherPiece != null && otherPiece.Type == ChangeType.Unchanged) {
                if (!wholeLineReplaced) {
                  document.Replace(offset, otherPiece.Text.Length, piece.Text);
                }

                string diffLine = line.Text;
                string otherDiffLine = otherDiff.Lines[lineIndex].Text;
                int otherPieceOffset = column;
                int pieceOffset = column;
                diffKind = EstimateModificationType(piece, otherPiece,
                                                    pieceOffset, otherPieceOffset,
                                                    diffLine, otherDiffLine);
              }
              else if (!wholeLineReplaced) {
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

          int offset = actualLine >= document.LineCount
            ? document.TextLength
            : document.GetOffset(actualLine, 0) + column;

          // Check if this deletion has an equivalent insertion on the other side.
          // If it does, try to mark it as a modification.
          var diffKind = DiffKind.Deletion;
          var otherPiece = FindPieceInOtherDocument(otherDiff, lineIndex, piece, pieceIndexAdjustment);

          if (otherPiece != null && otherPiece.Type == ChangeType.Inserted) {
            if (wholeLineReplaced) {
              string diffLine = line.Text;
              string otherDiffLine = otherDiff.Lines[lineIndex].Text;
              int otherPieceOffset = otherColumn;
              int pieceOffset = column;
              diffKind = EstimateModificationType(piece, otherPiece,
                                                  pieceOffset, otherPieceOffset,
                                                  diffLine, otherDiffLine);
            }
          }
          //else {
          //    otherPiece = FindOverlappingPieceInOtherDocument(otherDiff, lineIndex, column);

          //    if (otherPiece != null && otherPiece.Type == ChangeType.Unchanged) {
          //        var diffLine = line.Text;
          //        var otherDiffLine = otherDiff.Lines[lineIndex].Text;
          //        int otherPieceOffset = column;
          //        int pieceOffset = column;
          //        diffKind = EstimateModificationType(piece, otherPiece,
          //                                            pieceOffset, otherPieceOffset,
          //                                            diffLine, otherDiffLine);
          //    }
          //}

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
          if (!wholeLineReplaced && actualLine < document.LineCount) {
            int offset = document.GetOffset(actualLine, 0) + column;
            document.Replace(offset, piece.Text.Length, piece.Text);
          }

          var otherPiece = FindPieceInOtherDocument(otherDiff, lineIndex, piece, 0);

          if (otherPiece != null) {
            otherColumn += piece.Text.Length;
          }

          break;
        }
        default:
          throw new ArgumentOutOfRangeException("Unexpected change type!");
      }

      // Adjust the current column in the document.
      if (piece.Text != null) {
        column += piece.Text.Length;

        if (piece.Type != ChangeType.Unchanged) {
          lineChanges += piece.Text.Length;
        }
      }
    }

    // If most of the line changed, mark the entire line,
    // otherwise mark each sub-piece.
    if (settings_.ManyDiffsMarkWholeLine) {
      double percentChanged = 0;

      if (lineLength > 0) {
        percentChanged = lineChanges / (double)lineLength * 100;
      }

      if (percentChanged > settings_.ManyDiffsModificationPercentage) {
        if (actualLine < document.LineCount) {
          var docLine = document.GetLineByNumber(actualLine);
          var changeKind = DiffKind.Modification;

          // If even more of the line changed, consider it an insertion/deletion.
          if (percentChanged > settings_.ManyDiffsInsertionPercentage) {
            changeKind = isRightDoc ? DiffKind.Insertion : DiffKind.Deletion;
          }

          AppendChange(changeKind, docLine.Offset, docLine.Length, result);
          return;
        }
      }
    }

    foreach (var segment in modifiedSegments) {
      AppendChange(segment, result);
    }

    if (isRightDoc) {
      diffStats.LinesModified++;
    }
  }

  private void AppendChange(DiffKind kind, int offset, int length, DiffMarkingResult result) {
    AppendChange(new DiffTextSegment(kind, offset, length), result);
  }

  private void AppendChange(DiffTextSegment segment, DiffMarkingResult result) {
    bool accepted = false;

    switch (segment.Kind) {
      case DiffKind.Insertion: {
        accepted = settings_.ShowInsertions;
        break;
      }
      case DiffKind.Deletion: {
        accepted = settings_.ShowDeletions;
        break;
      }
      case DiffKind.Modification: {
        accepted = settings_.ShowModifications;
        break;
      }
      case DiffKind.MinorModification: {
        accepted = settings_.ShowMinorModifications;
        break;
      }
    }

    if (accepted) {
      result.DiffSegments.Add(segment);
    }
  }

  private void AppendInsertionChange(DiffStatistics diffStats, DiffMarkingResult result,
                                     DiffPiece line, int offset) {
    AppendChange(DiffKind.Insertion, offset, line.Text.Length, result);
    diffStats.LinesAdded++;
  }

  private void AppendDeletionChange(DiffStatistics diffStats, DiffMarkingResult result,
                                    DocumentLine docLine) {
    AppendChange(DiffKind.Deletion, docLine.Offset, docLine.Length, result);
    diffStats.LinesDeleted++;
  }

  private void AppendModificationChange(List<DiffTextSegment> modifiedSegments,
                                        DiffKind diffKind, AdjustedDiffPiece filteredPiece) {
    // With modifications that are expanded, it's possible to have two diffs
    // be expanded to the same text range - in that case keep the initial segment.
    if (modifiedSegments.Count > 0) {
      var lastSegment = modifiedSegments[^1];

      if (lastSegment.StartOffset == filteredPiece.Offset &&
          lastSegment.Length == filteredPiece.Length) {
        return;
      }
    }

    modifiedSegments.Add(new DiffTextSegment(diffKind, filteredPiece.Offset,
                                             filteredPiece.Length));
  }

  private DiffPiece FindPieceInOtherDocument(DiffPaneModel otherDiff, int lineIndex,
                                             DiffPiece piece, int piecePossitionOffset) {
    if (lineIndex < otherDiff.Lines.Count) {
      var otherLine = otherDiff.Lines[lineIndex];
      int position = piece.Position.Value + piecePossitionOffset;

      if (position > 0 && position <= otherLine.SubPieces.Count) {
        var result = otherLine.SubPieces[position - 1];
        return !string.IsNullOrEmpty(result.Text) ? result : null;
      }
    }

    return null;
  }

  private DiffPiece FindOverlappingPieceInOtherDocument(DiffPaneModel otherDiff,
                                                        int lineIndex, int offset) {
    if (lineIndex < otherDiff.Lines.Count) {
      var otherLine = otherDiff.Lines[lineIndex];
      int otherOffset = 0;

      foreach (var piece in otherLine.SubPieces) {
        if (!string.IsNullOrEmpty(piece.Text)) {
          if (otherOffset <= offset &&
              otherOffset + piece.Text.Length >= offset) {
            return piece;
          }

          otherOffset += otherLine.Text.Length;
        }
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

    foreach (char letter in piece.Text) {
      if (!char.IsWhiteSpace(letter) && Array.IndexOf(ignoredDiffLetters_, letter) == -1) {
        return true;
      }
    }

    return false;
  }

  private DiffKind EstimateModificationType(DiffPiece before, DiffPiece after,
                                            int beforeOffset, int afterOffset,
                                            string beforeDocumentText,
                                            string afterDocumentText) {
    if (!settings_.IdentifyMinorDiffs) {
      return DiffKind.Modification;
    }

    return diffFilter_.EstimateModificationType(before, after, beforeOffset, afterOffset,
                                                beforeDocumentText, afterDocumentText);
  }
}