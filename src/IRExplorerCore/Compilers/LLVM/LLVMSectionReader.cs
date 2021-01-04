// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace IRExplorerCore.LLVM {
    public sealed class LLVMSectionReader : SectionReaderBase, IDisposable {
        private static readonly char[] WhitespaceChars = { ' ', '\t' };
        private const string SectionStartLine = "*** IR Dump ";
        private const string SectionEndLine = "***";
        private const string SectionStartLine2 = "; IR Dump ";
        private const string SectionEndLine2 = ";";

        public LLVMSectionReader(string filePath, bool expectSectionHeaders = true) :
            base(filePath, expectSectionHeaders) { }

        public LLVMSectionReader(byte[] textData, bool expectSectionHeaders = true) :
            base(textData, expectSectionHeaders) { }

        protected override bool IsSectionStart(string line) {
            return line.StartsWith(SectionStartLine, StringComparison.Ordinal) ||
                   line.StartsWith(SectionStartLine2, StringComparison.Ordinal);
        }

        protected override bool IsFunctionStart(string line) {
            return line.StartsWith("define", StringComparison.Ordinal) &&
                   line.EndsWith("{", StringComparison.Ordinal);
        }

        protected override bool IsBlockStart(string line) {
            //? TODO: Blocks seem to always start with N:
            return false;
        }

        protected override bool IsFunctionEnd(string line) {
            return line.StartsWith("}", StringComparison.Ordinal);
        }

        protected override string ExtractSectionName(string line) {
            var name = ExtractSectionNameImpl(line, SectionStartLine, SectionEndLine);
            if (name != null) return name;

            name = ExtractSectionNameImpl(line, SectionStartLine2, SectionEndLine2);
            if (name != null) return name;
            return "";
        }

        private string ExtractSectionNameImpl(string line, string lineStartMarker, string lineEndMarker) {
            int start = line.IndexOf(lineStartMarker, StringComparison.Ordinal);

            if (start == -1) {
                return null;
            }

            int end = line.LastIndexOf(lineEndMarker, StringComparison.Ordinal);
            int length = end - start - lineStartMarker.Length;

            if (length > 0) {
                return line.Substring(start + lineStartMarker.Length, length).Trim();
            }

            return null;
        }

        protected override string ExtractFunctionName(string line) {
            // Function names start with @ and end before the ( starting the parameter list.
            int start = line.IndexOf('@', StringComparison.Ordinal);

            if (start == -1) {
                return "";
            }

            int end = line.IndexOf('(', start + 1);
            int length = end - start - 1;

            if (length > 0) {
                // If there are quotes around the name, ignore them.
                if (line[start + 1] == '"' &&
                    line[end - 1] == '"') {
                    return line.Substring(start + 2, length - 2);
                }

                return line.Substring(start + 1, length);
            }

            return "";
        }

        protected override string PreprocessLine(string line) {
            return line;
        }

        protected override bool ShouldSkipOutputLine(string line) {
            return string.IsNullOrWhiteSpace(line);
        }
    }
}
