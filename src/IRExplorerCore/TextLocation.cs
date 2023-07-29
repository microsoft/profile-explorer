// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;

namespace IRExplorerCore {
    public struct TextLocation : IComparable<TextLocation> {
        public int Offset {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set;
        }

        public int Line {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set;
        }

        public int Column {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextLocation(int offset, int line, int column = 0) {
            Offset = offset;
            Line = line;
            Column = column;
        }

        public override string ToString() {
            return $"offset: {Offset}, line: {Line}";
        }

        public override bool Equals(object obj) {
            return obj is TextLocation location &&
                   Offset == location.Offset &&
                   Line == location.Line &&
                   Column == location.Column;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Offset, Line);
        }

        public int CompareTo(TextLocation other) {
            return Offset.CompareTo(other.Offset);
        }

        public static bool operator ==(TextLocation a, TextLocation b) {
            return a.Equals(b);
        }

        public static bool operator !=(TextLocation a, TextLocation b) {
            return !a.Equals(b);
        }

        public static bool operator <(TextLocation left, TextLocation right) {
            return left.CompareTo(right) < 0;
        }

        public static bool operator >(TextLocation left, TextLocation right) {
            return left.CompareTo(right) > 0;
        }

        public static bool operator <=(TextLocation left, TextLocation right) {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >=(TextLocation left, TextLocation right) {
            return left.CompareTo(right) >= 0;
        }
    }
}