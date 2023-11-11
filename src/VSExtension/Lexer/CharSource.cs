// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.CompilerServices;

namespace IRExplorerCore.Lexer {
    public sealed class CharSource {
        public static readonly char EOF = '\0';
        public static readonly char NewLine = '\n';

        public CharSource(string text) {
            Text = text;
            Position = 0;
        }

        public string Text {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
        }

        public int Position {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            set;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public char NextChar() {
            if (Position >= Text.Length) {
                Position++;
                return EOF;
            }

            return Text[Position++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public char PeekChar() {
            return Position >= Text.Length ? EOF : Text[Position];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public char Skip(int count = 1) {
            Position += count;
            return PeekChar();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public char GoBack(int count = 1) {
            Position -= count;
            return PeekChar();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory<char> Extract(int position, int length) {
            return Text.AsMemory(position, length);
        }
    }
}
