// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using IRExplorerCore;

namespace IRExplorerUI;

[Flags]
public enum TextSearchKind {
  Default = 1 << 0, // Case sensitive
  CaseInsensitive = 1 << 1,
  Regex = 1 << 2,
  WholeWord = 1 << 3
}

public struct TextSearchResult {
  public int Offset;
  public int Length;

  public TextSearchResult(int offset, int length) {
    Offset = offset;
    Length = length;
  }

  public override bool Equals(object obj) {
    return obj is TextSearchResult result && Offset == result.Offset && Length == result.Length;
  }

  public override int GetHashCode() {
    return HashCode.Combine(Offset, Length);
  }
}

public class TextSearcher {
  public static bool IsWholeWord(ReadOnlySpan<char> matchedText, ReadOnlySpan<char> text, int startOffset) {
    if (startOffset > 0) {
      if (IsWordLetter(text[startOffset - 1])) {
        return false;
      }
    }

    if (startOffset + matchedText.Length < text.Length) {
      if (IsWordLetter(text[startOffset + matchedText.Length])) {
        return false;
      }
    }

    return true;
  }

  public static List<TextSearchResult> AllIndexesOf(ReadOnlyMemory<char> text,
                                                    ReadOnlyMemory<char> searchedText,
                                                    int startOffset = 0,
                                                    TextSearchKind searchKind = TextSearchKind.Default,
                                                    CancelableTask cancelableTask = null) {
    if (text.Length == 0 || searchedText.Length == 0) {
      return new List<TextSearchResult>();
    }

    var regex = searchKind.HasFlag(TextSearchKind.Regex) ?
      CreateRegex(searchedText.ToString(), searchKind) : null;

    var offsetList = new List<TextSearchResult>();
    (int offset, int length) = IndexOf(text, searchedText, startOffset, searchKind, regex);

    while (offset != -1 && offset + length < text.Length) {
      offsetList.Add(new TextSearchResult(offset, length));
      offset += length;

      if (cancelableTask != null && cancelableTask.IsCanceled) {
        break;
      }

      (offset, length) = IndexOf(text, searchedText, offset, searchKind, regex);
    }

    return offsetList;
  }

  public static TextSearchResult? FirstIndexOf(ReadOnlyMemory<char> text,
                                               ReadOnlyMemory<char> searchedText,
                                               int startOffset = 0,
                                               TextSearchKind searchKind = TextSearchKind.Default) {
    (int offset, int length) = IndexOf(text, searchedText, startOffset, searchKind);

    if (offset != -1) {
      return new TextSearchResult(offset, length);
    }

    return null;
  }

  public static bool Contains(ReadOnlyMemory<char> text,
                              ReadOnlyMemory<char> searchedText,
                              TextSearchKind searchKind = TextSearchKind.Default) {
    (int offset, _) = IndexOf(text, searchedText, 0, searchKind);
    return offset != -1;
  }

  public static List<TextSearchResult> AllIndexesOf(string text, string searchedText,
                                                    int startOffset = 0,
                                                    TextSearchKind searchKind = TextSearchKind.Default,
                                                    CancelableTask cancelableTask = null) {
    return AllIndexesOf(text.AsMemory(), searchedText.AsMemory(),
                        startOffset, searchKind, cancelableTask);
  }

  public static TextSearchResult? FirstIndexOf(string text, string searchedText,
                                               int startOffset = 0,
                                               TextSearchKind searchKind = TextSearchKind.Default) {
    return FirstIndexOf(text.AsMemory(), searchedText.AsMemory(),
                        startOffset, searchKind);
  }

  public static bool Contains(string text, string searchedText,
                              TextSearchKind searchKind = TextSearchKind.Default) {
    return Contains(text.AsMemory(), searchedText.AsMemory(), searchKind);
  }

  public static List<TextSearchResult> AllIndexesOf(ReadOnlyMemory<char> text, string searchedText,
                                                    int startOffset = 0,
                                                    TextSearchKind searchKind = TextSearchKind.Default,
                                                    CancelableTask cancelableTask = null) {
    return AllIndexesOf(text, searchedText.AsMemory(),
                        startOffset, searchKind, cancelableTask);
  }

  public static TextSearchResult? FirstIndexOf(ReadOnlyMemory<char> text, string searchedText,
                                               int startOffset = 0,
                                               TextSearchKind searchKind = TextSearchKind.Default) {
    return FirstIndexOf(text, searchedText.AsMemory(),
                        startOffset, searchKind);
  }

  public static bool Contains(ReadOnlyMemory<char> text, string searchedText,
                              TextSearchKind searchKind = TextSearchKind.Default) {
    return Contains(text, searchedText.AsMemory());
  }

  private static bool IsWordLetter(char letter) {
    return char.IsLetter(letter) || char.IsDigit(letter) || letter == '_';
  }

  private static Regex CreateRegex(string pattern, TextSearchKind searchKind) {
    try {
      var options = RegexOptions.Compiled;

      if (searchKind.HasFlag(TextSearchKind.CaseInsensitive)) {
        options |= RegexOptions.IgnoreCase;
      }

      return new Regex(pattern, options);
    }
    catch (Exception ex) {
      Debug.WriteLine($"Failed regex text search: {ex}");
      return null;
    }
  }

  private static (int, int) IndexOf(ReadOnlyMemory<char> text,
                                    ReadOnlyMemory<char> searchedText, int startOffset = 0,
                                    TextSearchKind searchKind = TextSearchKind.Default,
                                    Regex regex = null) {
    if (text.Length == 0 || searchedText.Length == 0) {
      return (-1, 0);
    }

    if (searchKind.HasFlag(TextSearchKind.Regex)) {
      try {
        regex ??= CreateRegex(searchedText.ToString(), searchKind);
        var result = regex.Matches(text.ToString(), startOffset);

        if (result.Count > 0) {
          return (result[0].Index, result[0].Length);
        }
      }
      catch (Exception ex) {
        //? TODO: Handle invalid regex, report in UI
        Debug.WriteLine($"Failed regex text search: {ex}");
      }
    }
    else {
      var comparisonKind = searchKind.HasFlag(TextSearchKind.CaseInsensitive) ?
        StringComparison.OrdinalIgnoreCase :
        StringComparison.Ordinal;
      var adjustedText = text.Slice(startOffset);
      int index = adjustedText.Span.IndexOf(searchedText.Span, comparisonKind);

      if (index == -1) {
        return (-1, searchedText.Length);
      }

      if (searchKind.HasFlag(TextSearchKind.WholeWord)) {
        if (!IsWholeWord(searchedText.Span, text.Span, index + startOffset)) {
          return (-1, searchedText.Length);
        }
      }

      return (startOffset + index, searchedText.Length);
    }

    return (-1, searchedText.Length);
  }
}
