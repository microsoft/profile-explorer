// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using DiffPlex.DiffBuilder.Model;
using ProfileExplorerCore2;
using ProfileExplorer.UI.Diff;

namespace ProfileExplorer.UI.Compilers.Default;

public sealed class DefaultDiffOutputFilter : IDiffOutputFilter {
  private DiffSettings settings_;
  private ICompilerIRInfo compilerInfo_;
  private char[] ExpansionStopLetters => new[] {
    '(', ')', '<', '>', ',', '.', ';', ':', '|', '[', ']', '{', '}', '!', ' ', '\t'
  };
  public char[] IgnoredDiffLetters => new[] {
    '(', ')', ',', '.', ';', ':', '|', '{', '}', '!', ' ', '\t'
  };

  public void Initialize(DiffSettings settings, ICompilerIRInfo compilerInfo) {
    settings_ = settings;
    compilerInfo_ = compilerInfo;
  }

  public DiffKind EstimateModificationType(DiffPiece before, DiffPiece after, int beforeOffset, int afterOffset,
                                           string beforeLineText, string afterLineText) {
    string beforeText = ExpandDiff(before.Text, beforeOffset, beforeLineText,
                                   out int beforeLeftStopIndex, out int beforeRightStopIndex);
    string afterText = ExpandDiff(after.Text, afterOffset, afterLineText,
                                  out int afterLeftStopIndex, out int afterRightStopIndex);

    if (IsTemporaryVariable(beforeText, out int beforeNumber) &&
        IsTemporaryVariable(afterText, out int afterNumber)) {
      if (beforeNumber == afterNumber) {
        return DiffKind.MinorModification;
      }

      if (settings_.FilterTempVariableNames) {
        return DiffKind.MinorModification;
      }
    }
    else if (IsSSANumber(beforeText, beforeLeftStopIndex, beforeRightStopIndex, beforeLineText) &&
             IsSSANumber(afterText, afterLeftStopIndex, afterRightStopIndex, afterLineText)) {
      if (settings_.FilterSSADefNumbers) {
        return DiffKind.MinorModification;
      }
    }
    else if (IsCommentText(beforeText, beforeLeftStopIndex, beforeRightStopIndex, beforeLineText) ||
             IsCommentText(afterText, afterLeftStopIndex, afterRightStopIndex, afterLineText)) {
      return DiffKind.MinorModification;
    }

    return DiffKind.Modification;
  }

  public AdjustedDiffPiece AdjustChange(DiffPiece change, int documentOffset,
                                        int lineOffset, string lineText) {
    string text = ExpandDiff(change.Text, lineOffset, lineText,
                             out int leftStopIndex, out int rightStopIndex);

    if (IsTemporaryVariable(text, out int _)) {
      // Enlarge diff marking to cover entire variable/opcode.
      int lineStartOffset = documentOffset - lineOffset;
      return new AdjustedDiffPiece(lineStartOffset + leftStopIndex, text.Length);
    }

    if (IsSSANumber(text, leftStopIndex, rightStopIndex, lineText)) {
      // Enlarge diff to include entire SSA number <*123> instead of just some digits.
      if (lineText[leftStopIndex] != '<' && leftStopIndex > 0) {
        leftStopIndex--;
      }

      if (lineText[rightStopIndex] != '>' && rightStopIndex < lineText.Length - 1) {
        rightStopIndex++;
      }

      int lineStartOffset = documentOffset - lineOffset;
      return new AdjustedDiffPiece(lineStartOffset + leftStopIndex, rightStopIndex - leftStopIndex + 1);
    }

    return new AdjustedDiffPiece(documentOffset, change.Text.Length);
  }

  private static bool IsSSANumberEnd(int rightStopIndex, string lineText) {
    return lineText[rightStopIndex] == '>' ||
           lineText[rightStopIndex] == 'l' ||
           lineText[rightStopIndex] == 'r';
  }

  private static bool IsSSANumberStart(int leftStopIndex, string lineText) {
    return lineText[leftStopIndex] == '<' ||
           lineText[leftStopIndex] == '*' ||
           char.IsDigit(lineText[leftStopIndex]);
  }

  private bool IsTemporaryVariable(string text, out int tempNumber) {
    tempNumber = 0;
    var name = text.AsSpan();
    int prefixLength = 0;

    //? TODO: Should query ICompilerInfo instead of hardcoding this.
    if (name.StartsWith("t".AsSpan())) {
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
    bool hasSSANumberStart = IsSSANumberStart(leftStopIndex, lineText);
    bool hasSSANumberEnd = IsSSANumberEnd(rightStopIndex, lineText);
    bool isCandidate = hasSSANumberStart && hasSSANumberEnd;

    if (!isCandidate) {
      // Sometimes the < > letters are not part of the diff, check the
      // previous/next letters too.
      if (!hasSSANumberStart && leftStopIndex > 0 &&
          IsSSANumberStart(leftStopIndex - 1, lineText)) {
        leftStopIndex--;
        hasSSANumberStart = true;
      }

      if (!hasSSANumberEnd && rightStopIndex < lineText.Length - 1 &&
          IsSSANumberEnd(rightStopIndex + 1, lineText)) {
        rightStopIndex++;
        hasSSANumberEnd = true;
      }

      isCandidate = hasSSANumberStart && hasSSANumberEnd;
    }

    if (isCandidate) {
      leftStopIndex += !char.IsDigit(lineText[leftStopIndex]) ? 1 : 0;
      rightStopIndex -= !char.IsDigit(lineText[rightStopIndex]) ? 1 : 0;
      string defNumber = lineText.Substring(leftStopIndex, rightStopIndex - leftStopIndex + 1);
      return int.TryParse(defNumber, out int _);
    }

    return false;
  }

  private bool IsCommentText(string text, int leftStopIndex, int rightStopIndex, string lineText) {
    // Everything following # is debug info and line numbers.
    return lineText.LastIndexOf('#', leftStopIndex) != -1;
  }

  private string ExpandDiff(string diffText, int lineOffset, string lineText,
                            out int leftStopIndex, out int rightStopIndex) {
    if (diffText.Length == 0 || lineOffset >= lineText.Length) {
      leftStopIndex = rightStopIndex = 0;
      return diffText;
    }

    // Sometimes the diff starts with one of the stop letters, which should not
    // be included in the diff, just skip over it.
    while (lineOffset < lineText.Length - 1 &&
           Array.IndexOf(ExpansionStopLetters, lineText[lineOffset]) != -1) {
      lineOffset++;
    }

    // Expand left/right as long no end marker letters are found.
    int left = lineOffset;
    int right = lineOffset;

    while (left > 0) {
      if (Array.IndexOf(ExpansionStopLetters, lineText[left - 1]) != -1) {
        break;
      }

      left--;
    }

    while (right < lineText.Length - 1) {
      if (Array.IndexOf(ExpansionStopLetters, lineText[right + 1]) != -1) {
        break;
      }

      right++;
    }

    leftStopIndex = left;
    rightStopIndex = right;
    return lineText.Substring(left, right - left + 1);
  }
}