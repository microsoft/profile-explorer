// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace Core.Lexer {
    public static class CharTable {
        static readonly byte[] CHAR_TYPE = {
            0,    0,    0,    0,    0,    0,    0,    0,  // 8
            0,    1,    1,    1,    1,    1,    0,    0,  // 16
            0,    0,    0,    0,    0,    0,    0,    0,  // 24
            0,    0,    0,    0,    0,    0,    0,    0,  // 32
            1,    0,    0,    0,    26,    0,    0,   0,  // 40
            0,    0,    0,    0,    0,    0,    8,    0,  // 48
            28,   28,   28,   28,   28,   28,   28,   28, // 56
            28,   28,    0,    0,    0,    0,    0,   16, // 64
            18,   26,   26,   26,   26,   26,   26,   18, // 72
            18,   18,   18,   18,   18,   18,   18,   18, // 80
            18,   18,   18,   18,   18,   18,   18,   18, // 88
            26,   18,   18,    0,    0,    0,    0,   18, // 96
            0,    26,   26,   26,   26,   26,   26,   18, // 104
            18,   18,   18,   18,   18,   18,   18,   18, // 112
            18,   18,   18,   18,   18,   18,   18,   18, // 120
            26,   18,   18,   0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0,
            0,    0,    0,    0,    0,    0,    0,    0
        };

        static readonly byte CHAR_WHITESPACE = 1;
        static readonly byte CHAR_LETTER = 2;
        static readonly byte CHAR_DIGIT = 4;
        static readonly byte CHAR_NUMBER = 8;
        static readonly byte CHAR_IDENTIFIER = 16;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDigit(char value) {
            return (CHAR_TYPE[(byte)value] & CHAR_DIGIT) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWhitespace(char value) {
            return (CHAR_TYPE[(byte)value] & CHAR_WHITESPACE) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLetter(char value) {
            return (CHAR_TYPE[(byte)value] & CHAR_LETTER) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNumberChar(char value) {
            return (CHAR_TYPE[(byte)value] & CHAR_NUMBER) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsIdentifierChar(char value) {
            return (CHAR_TYPE[(byte)value] & CHAR_IDENTIFIER) != 0;
        }
    }
}
