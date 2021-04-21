﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace IRExplorerCore.ASM {
    public class ASMIRSectionReader : SectionReaderBase {
        public ASMIRSectionReader(string filePath, bool expectSectionHeaders) :
            base(filePath, expectSectionHeaders) { }
        public ASMIRSectionReader(byte[] textData, bool expectSectionHeaders) :
            base(textData, expectSectionHeaders) { }

        protected override string ExtractFunctionName(string line) {
            return line.Substring(0, line.Length - 1);
        }

        protected override string ExtractSectionName(string line) {
            return ExtractFunctionName(line);
        }

        protected override bool IsBlockStart(string line) {
            return false;
        }

        protected override bool IsFunctionEnd(string line) => string.IsNullOrEmpty(line) || IsFunctionStart(line);

        private readonly Regex functionStartRegex = new Regex(@"[^\s]+:$", RegexOptions.Compiled);

        protected override bool IsFunctionStart(string line) => functionStartRegex.IsMatch(line);

        protected override bool IsMetadataLine(string line) => false;

        protected override bool IsSectionStart(string line) => IsFunctionStart(line);

        protected override string PreprocessLine(string line) => line;

        protected override bool ShouldSkipOutputLine(string line) => throw new NotImplementedException();

        protected override bool FunctionEndIsFunctionStart(string line) => !string.IsNullOrEmpty(line);
        protected override bool SectionStartIsFunctionStart(string line) => true;
    }
}
