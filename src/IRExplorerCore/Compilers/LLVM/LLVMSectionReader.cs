// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace IRExplorerCore.LLVM {
    public sealed class LLVMSectionReader : SectionReaderBase, IDisposable {
        private static readonly char[] WhitespaceChars = { ' ', '\t' };
        private const string SectionStartLine = "*** IR Dump ";
        private const string SectionEndLine = "***";

        public LLVMSectionReader(string filePath, bool expectSectionHeaders = true) :
            base(filePath, expectSectionHeaders) { }

        public LLVMSectionReader(byte[] textData, bool expectSectionHeaders = true) :
            base(textData, expectSectionHeaders) { }

        protected override bool IsSectionStart(string line) {
            return line.StartsWith(SectionStartLine, StringComparison.Ordinal);
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
            int start = line.IndexOf(SectionStartLine);

            if (start == -1) {
                return "";
            }

            int end = line.LastIndexOf(SectionEndLine);
            int length = end - start - SectionStartLine.Length;

            if (length > 0) {
                return line.Substring(start + SectionStartLine.Length, length).Trim();
            }

            return "";
        }

        protected override string ExtractFunctionName(string line) {
            // Function names start with @ and end before the ( starting the parameter list.
            int start = line.IndexOf('@');

            if (start == -1) {
                return "";
            }

            int end = line.IndexOf('(', start + 1);
            int length = end - start - 1;

            if (length > 0) {
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

        protected override bool IsMetadataLine(string line) => false;

        protected override bool FunctionEndIsFunctionStart(string line) => false;
        protected override bool SectionStartIsFunctionStart(string line) => false;
    }
}
