// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;

namespace IRExplorerCore.MLIR {
    public sealed class MLIRSectionReader : SectionReaderBase, IDisposable {
        private static readonly char[] WhitespaceChars = { ' ', '\t' };
        private const string SectionStartLine = "// -----//";
        // \s*\/\/(-|\s*)----*\s*\/?\/?
        private const string SectionStartLineEnd = "//----- //";
        private const string SectionEndLine = "//----- //";
        private const string SectionEndLine2 = "-----";

        static readonly Regex SectionStartLineRegex;

        private bool isParsingModule;
        private bool isParsingSection;
        private bool isParsingFunction;
        private bool sawModule;
        private bool sawFunction;
        private int openBraces;
        private int closedBraces;
        private int functionStartOpenBraces;

        static MLIRSectionReader() {
            SectionStartLineRegex = new Regex(@"\s*\/\/(-|\s*)----*\s*\/?\/?", RegexOptions.Compiled);
        }

        public MLIRSectionReader(string filePath, bool expectSectionHeaders = true) :
            base(filePath, expectSectionHeaders) { }

        public MLIRSectionReader(byte[] textData, bool expectSectionHeaders = true) :
            base(textData, expectSectionHeaders) { }

        protected override bool IsSectionStart(string line) {
            //? TODO: Use Regex
            //? TODO: Without section names, having parsed a module should start a new section?
            isParsingSection = !isParsingSection && line.StartsWith(SectionStartLine, StringComparison.Ordinal);
            return isParsingSection;
        }

        protected override bool IsSectionEnd(string line) {
            //? TODO: Use Regex
            return line.StartsWith(SectionEndLine, StringComparison.Ordinal)
            || line.StartsWith(SectionEndLine2, StringComparison.Ordinal)
                   || (isParsingSection && (sawModule || sawFunction) && (closedBraces == openBraces));
        }

        protected override bool IsFunctionStart(string line) {
            int index = 0;

            while(index < line.Length && char.IsWhiteSpace(line[index])) {
                index++;
            }

            int funcIndex = line.IndexOf("func", index, StringComparison.Ordinal);

            if (funcIndex == -1) {
                return false;
            }

            if (funcIndex > 0 && line[funcIndex - 1] == '\"') {
                index++;
            }

            bool result = false;

            if (funcIndex == index && (funcIndex + 4) < line.Length &&
                line[funcIndex + 4] == '.') {
                result = line.EndsWith("{", StringComparison.Ordinal);
            }
            else if (funcIndex > 0 && line[funcIndex - 1] == '.') {
                result = line.EndsWith("{", StringComparison.Ordinal);
            }

            if (result) {
                functionStartOpenBraces = openBraces - closedBraces - 1;
                isParsingFunction = true;
                sawFunction = true;
            }

            return result;
        }

        protected override bool IsBlockStart(string line) {
            //? TODO: Blocks seem to always start with N:
            return false;
        }

        protected override bool IsFunctionEnd(string line) {
            return isParsingFunction && ((openBraces - closedBraces) == functionStartOpenBraces);
        }

        protected override void EndCurrentFunction() {
            isParsingFunction = false;
        }

        protected override void EndCurrentModule() {
            //? TODO: Assert !isParsingFunction
            isParsingModule = false;
            sawFunction = false;
        }

        protected override void EndCurrentSection() {
            //? TODO: Assert !isParsingModule, !isParsingFunction
            isParsingSection = false;
            sawModule = false;
            sawFunction = false;
        }

        protected override string ExtractSectionName(string line) {
            int start = line.IndexOf(SectionStartLine);

            if (start == -1) {
                return "";
            }

            int end = line.LastIndexOf(SectionEndLine);

            if (end == -1) {
                end = line.Length;
            }

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
            foreach(char c in line) {
                if (c == '{') {
                    openBraces++;
                }
                else if (c == '}') {
                    closedBraces++;
                }
            }

            return line;
        }

        protected override bool ShouldSkipOutputLine(string line) {
            //return string.IsNullOrWhiteSpace(line);
            return false;
        }

        protected override bool IsMetadataLine(string line) => false;

        protected override bool IsModuleStart(string line) {
            isParsingModule = !isParsingModule && line.StartsWith("module");
            return isParsingModule;
        }

        protected override bool IsModuleEnd(string line) {
            return isParsingModule && (closedBraces == openBraces);
        }

        protected override string ExtractModuleName(string line) {
            return ExtractFunctionName(line);
        }

        protected override bool FunctionEndIsFunctionStart(string line) => false;

        //? TODO: If section headers seen, disable this
        //? Actually use it in the parsing
        protected override bool FunctionStartIsSectionStart(string line) => false;
    }
}