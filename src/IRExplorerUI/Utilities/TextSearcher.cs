// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Xceed.Wpf.Toolkit;

namespace IRExplorerUI {
    [Flags]
    [TypeConverter(typeof(EnumNameConverter))]
    public enum TextSearchKind {
        [Description("Case sensitive")]
        Default = 1 << 0, // Case sensitive
        [Description("Case insensitive")]
        CaseInsensitive = 1 << 1,
        [Description("Regex")]
        Regex = 1 << 2,
        [Browsable(false)]
        WholeWord = 1 << 3,
        [Description("Case sensitive, whole word")]
        CaseSensitiveWholeWord = Default | WholeWord,
        [Description("Case insensitive, whole word")]
        CaseInsensitiveWholeWord = CaseInsensitive | WholeWord
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
        private static bool IsWordLetter(char letter) {
            return char.IsLetter(letter) || char.IsDigit(letter) || letter == '_';
        }

        public static bool IsWholeWord(string matchedText, string text, int startOffset) {
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

        private static Regex CreateRegex(string pattern, TextSearchKind searchKind) {
            try {
                //? TODO: Regex should also honor the IgnoreCase/WholeWord flags
                //? TODO: Check if compiled regex is faster (should be...)
                var options = RegexOptions.None;

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

        private static (int, int) IndexOf(string text, string searchedText, int startOffset = 0,
                                           TextSearchKind searchKind = TextSearchKind.Default,
                                           Regex regex = null) {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchedText)) {
                return (-1, 0);
            }

            if (searchKind.HasFlag(TextSearchKind.Regex)) {
                try {
                    regex ??= CreateRegex(searchedText, searchKind);
                    var result = regex.Matches(text, startOffset);

                    if (result.Count > 0) {
                        return (result[0].Index, result[0].Length);
                    }
                }
                catch (Exception ex) {
                    //? TODO: Handle invalid regex
                    Debug.WriteLine($"Failed regex text search: {ex}");
                }
            }
            else if (!searchKind.HasFlag(TextSearchKind.CaseInsensitive)) {
                int index = text.IndexOf(searchedText, startOffset, StringComparison.Ordinal);

                if (index != -1 && searchKind.HasFlag(TextSearchKind.WholeWord)) {
                    if (!IsWholeWord(searchedText, text, index)) {
                        return (-1, searchedText.Length);
                    }
                }

                return (index, searchedText.Length);
            }
            else if (searchKind.HasFlag(TextSearchKind.CaseInsensitive)) {
                int index = text.IndexOf(searchedText, startOffset, StringComparison.OrdinalIgnoreCase);

                if (index != -1 && searchKind.HasFlag(TextSearchKind.WholeWord)) {
                    if (!IsWholeWord(searchedText, text, index)) {
                        return (-1, searchedText.Length);
                    }
                }

                return (index, searchedText.Length);
            }
            else {
                throw new InvalidOperationException($"Unknown search kind: {searchKind}");
            }

            return (-1, searchedText.Length);
        }

        public static List<TextSearchResult> AllIndexesOf(string text, string searchedText,
                                                          int startOffset = 0,
                                                          TextSearchKind searchKind = TextSearchKind.Default,
                                                          CancelableTask cancelableTask = null) {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchedText)) {
                return new List<TextSearchResult>();
            }

            var regex = searchKind.HasFlag(TextSearchKind.Regex)
                ? CreateRegex(searchedText, searchKind)
                : null;

            var offsetList = new List<TextSearchResult>();
            (int offset, int length) = IndexOf(text, searchedText, startOffset, searchKind, regex);

            while (offset != -1 && offset < text.Length) {
                offsetList.Add(new TextSearchResult(offset, length));
                offset += length;

                if (cancelableTask != null && cancelableTask.IsCanceled) {
                    break;
                }

                (offset, length) = IndexOf(text, searchedText, offset, searchKind, regex);
            }

            return offsetList;
        }

        public static TextSearchResult? FirstIndexof(string text, string searchedText, 
                                                    int startOffset = 0,
                                                    TextSearchKind searchKind = TextSearchKind.Default) {
            (int offset, int length) = IndexOf(text, searchedText, startOffset, searchKind, null);

            if (offset != -1) {
                return new TextSearchResult(offset, length);
            }
            return null;
        }

        public static bool Contains(string text, string searchedText,
                                    TextSearchKind searchKind = TextSearchKind.Default) {
            (int offset, _) = IndexOf(text, searchedText, 0, searchKind, null);
            return offset != -1;
        }
    }
}
