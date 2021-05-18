// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace IRExplorerCore.UTC {
    public sealed class UTCSectionReader : SectionReaderBase {
        private static readonly char[] WhitespaceChars = { ' ', '\t' };

        private const string SectionStartLine = "*********************";
        private static readonly string[] SeparatorLines = {
            "# # # # # # # # # # # # # # # # # # # # # # # # # # # # # #",
            "***********************************************************"
        };

        public UTCSectionReader(string filePath, bool expectSectionHeaders = true) :
            base(filePath, expectSectionHeaders) { }

        public UTCSectionReader(byte[] textData, bool expectSectionHeaders = true) :
            base(textData, expectSectionHeaders) { }

        protected override bool IsSectionStart(string line) {
            return line.Equals(SectionStartLine, StringComparison.Ordinal);
        }

        protected override bool IsFunctionStart(string line) {
            return line.StartsWith("ENTRY", StringComparison.Ordinal);
        }

        protected override bool IsBlockStart(string line) {
            return line.StartsWith("BLOCK", StringComparison.Ordinal);
        }

        protected override bool IsFunctionEnd(string line) {
            return line.StartsWith("EXIT", StringComparison.Ordinal);
        }

        protected override string ExtractSectionName(string line) {
            var sectionName = PreviousLine(1);

            if (sectionName == null) {
                return string.Empty;
            }

            if (sectionName.StartsWith(SeparatorLines[0], StringComparison.Ordinal) ||
                sectionName.StartsWith(SeparatorLines[1], StringComparison.Ordinal)) {
                sectionName = PreviousLine(2);
            }

            return sectionName != null ? sectionName.Trim() : string.Empty;
        }

        protected override string ExtractFunctionName(string line) {
            var parts = line.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[1].Trim() : string.Empty;
        }

        protected override string PreprocessLine(string line) {
            // Convert "DDD>actual line text" -> "actual line text"
            // Convert "DDD: actual line text" -> "actual line text"
            if (string.IsNullOrEmpty(line) || !char.IsDigit(line[0])) {
                return line;
            }

            for (int i = 1; i < line.Length; i++) {
                if (line[i] == '>') {
                    MarkPreprocessedLine(i);
                    return line.Substring(i + 1);
                }
                else if (line[i] == ':') {
                    MarkPreprocessedLine(i);
                    return line.Substring(i + 2);
                }
                else if (!char.IsDigit(line[i])) {
                    break;
                }
            }

            return line;
        }

        protected override bool ShouldSkipOutputLine(string line) {
            return string.IsNullOrWhiteSpace(line) ||
                   line.StartsWith(SectionStartLine, StringComparison.Ordinal);
        }

        protected override bool IsMetadataLine(string line) {
            return line.StartsWith("/// irx:", StringComparison.Ordinal);
        }

        protected override bool FunctionEndIsFunctionStart(string line) => false;
        protected override bool SectionStartIsFunctionStart(string line) => false;
    }
}
