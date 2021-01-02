// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace IRExplorerCore {
    public struct TextLocation {
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TextLocation(int offset, int line) {
            Offset = offset;
            Line = line;
        }

        public override string ToString() {
            return $"offset: {Offset}, line {Line}";
        }

        public override bool Equals(object obj) {
            return obj is TextLocation location &&
                   Offset == location.Offset &&
                   Line == location.Line;
        }

        public override int GetHashCode() {
            int hashCode = -1429159632;
            hashCode = hashCode * -1521134295 + Offset.GetHashCode();
            hashCode = hashCode * -1521134295 + Line.GetHashCode();
            return hashCode;
        }

        public static bool operator ==(TextLocation a, TextLocation b) {
            return a.Equals(b);
        }

        public static bool operator !=(TextLocation a, TextLocation b) {
            return !a.Equals(b);
        }
    }
}
