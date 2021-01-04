// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace IRExplorerCore {
    public class IRPassOutput {
        public IRPassOutput(long dataStartOffset, long dataEndOffset, int startLine,
            int endLine) {
            DataStartOffset = dataStartOffset;
            DataEndOffset = dataEndOffset;
            StartLine = startLine;
            EndLine = endLine;
        }

        public long DataStartOffset { get; set; }
        public long DataEndOffset { get; set; }
        public long Size => DataEndOffset - DataStartOffset + 1;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int LineCount => EndLine - StartLine + 1;
        public bool HasPreprocessedLines { get; set; }

        public override bool Equals(object obj) {
            return obj is IRPassOutput output &&
                   DataStartOffset == output.DataStartOffset &&
                   DataEndOffset == output.DataEndOffset &&
                   StartLine == output.StartLine &&
                   EndLine == output.EndLine;
        }

        public override int GetHashCode() {
            return HashCode.Combine(DataStartOffset, DataEndOffset);
        }
    }
}