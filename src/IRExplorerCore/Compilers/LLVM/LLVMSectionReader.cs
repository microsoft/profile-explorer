// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace IRExplorerCore.LLVM {
    public sealed class LLVMSectionReader : SectionReaderBase, IDisposable {
        private static readonly string[] SectionStartLines = {
            "*** IR Dump ",
            "; IR Dump ",
            "# *** IR Dump",
        };

        private static readonly string[] SectionEndLines = {
            "***",
            ";",
            "***"
        };

        private static readonly string[] FunctionBodyStart = {
            "define",
            "# Machine code for function"
        };

        private static readonly string[] FunctionBodyEnd = {
            "}",
            "# End machine code for function"
        };

        private static readonly string[] FunctionNameStart = {
            "@",
            "Machine code for function"
        };

        private static readonly string[] FunctionNameEnd = {
            "(",
            ":"
        };

        public LLVMSectionReader(string filePath, bool expectSectionHeaders = true) :
            base(filePath, expectSectionHeaders) { }

        public LLVMSectionReader(byte[] textData, bool expectSectionHeaders = true) :
            base(textData, expectSectionHeaders) { }

        protected override bool IsSectionStart(string line) {
            foreach (var pattern in SectionStartLines) {
                if (line.StartsWith(pattern, StringComparison.Ordinal)) {
                    return true;
                }
            }

            return false;
        }

        protected override bool IsFunctionStart(string line) {
            foreach (var pattern in FunctionBodyStart) {
                if (line.StartsWith(pattern, StringComparison.Ordinal)) {
                    return true;
                }
            }

            return false;
        }

        protected override bool IsBlockStart(string line) {
            //? TODO: Blocks seem to always start with N:
            // For machine bb.1
            return false;
        }

        protected override bool IsFunctionEnd(string line) {
            foreach (var pattern in FunctionBodyEnd) {
                if (line.StartsWith(pattern, StringComparison.Ordinal)) {
                    return true;
                }
            }

            return false;
        }

        protected override string ExtractSectionName(string line) {
            for (int i = 0; i < SectionStartLines.Length; i++) {
                var name = ExtractSectionNameImpl(line, SectionStartLines[i], SectionEndLines[i]);

                if (name != null) {
                    return name;
                }
            }

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
            for (int i = 0; i < FunctionNameStart.Length; i++) {
                var name = ExtractFunctionNameImpl(line, FunctionNameStart[i], FunctionNameEnd[i]);

                if (name != null) {
                    return name;
                }
            }

            return "";
        }

        private string ExtractFunctionNameImpl(string line, string nameStartMarker, string nameEndMarker) {
            // Function names start with @ and end before the ( starting the parameter list.
            int start = line.IndexOf(nameStartMarker, StringComparison.Ordinal);

            if (start == -1) {
                return null;
            }
            
            int end = line.IndexOf(nameEndMarker, start + nameStartMarker.Length, StringComparison.Ordinal);
            int length = end - start - nameStartMarker.Length;

            if (length > 0) {
                // If there are quotes around the name, ignore them.
                if (line[start + 1] == '"' &&
                    line[end - 1] == '"') {
                    return line.Substring(start + nameStartMarker.Length + 1, length - 2).Trim();
                }

                return line.Substring(start + nameStartMarker.Length, length).Trim();
            }

            return null;
        }

        protected override string PreprocessLine(string line) {
            return line;
        }

        protected override bool ShouldSkipOutputLine(string line) {
            return string.IsNullOrWhiteSpace(line);
        }
    }
}
