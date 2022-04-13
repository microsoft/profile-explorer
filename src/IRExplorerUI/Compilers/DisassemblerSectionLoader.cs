using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Utilities;

namespace IRExplorerUI.Compilers {
    public class DisassemblerSectionLoader : IRTextSectionLoader {
        private IRTextSummary summary_;
        private string binaryFilePath_;
        private Disassembler disassembler_;
        private IDebugInfoProvider debugInfo_;
        private ICompilerInfoProvider compilerInfo_;
        private Dictionary<IRTextFunction, DebugFunctionInfo> funcToDebugInfoMap_;
        private string debugFilePath_;

        public string DebugFilePath => debugFilePath_;

        public DisassemblerSectionLoader(string binaryFilePath, ICompilerInfoProvider compilerInfo) {
            Initialize(compilerInfo.IR, cacheEnabled: false);
            binaryFilePath_ = binaryFilePath;
            compilerInfo_ = compilerInfo;
            summary_ = new IRTextSummary();
            funcToDebugInfoMap_ = new Dictionary<IRTextFunction, DebugFunctionInfo>();
        }

        public override IRTextSummary LoadDocument(ProgressInfoHandler progressHandler) {
            progressHandler?.Invoke(null, new SectionReaderProgressInfo(true));

            debugInfo_ = compilerInfo_.CreateDebugInfoProvider(binaryFilePath_);
            debugFilePath_ = compilerInfo_.FindDebugInfoFile(binaryFilePath_).Result;

            if (debugInfo_.LoadDebugInfo(debugFilePath_)) {
                disassembler_ = Disassembler.CreateForBinary(binaryFilePath_, debugInfo_);

                foreach (var funcInfo in debugInfo_.EnumerateFunctions()) {
                    if (funcInfo.RVA == 0) {
                        continue; // Some entries don't represent real functions.
                    }

                    var func = new IRTextFunction(funcInfo.Name);
                    var section = new IRTextSection(func, func.Name, IRPassOutput.Empty);
                    func.AddSection(section);
                    summary_.AddFunction(func);
                    summary_.AddSection(section);
                    funcToDebugInfoMap_[func] = funcInfo;
                }
            }

            progressHandler?.Invoke(null, new SectionReaderProgressInfo(false));
            return summary_;
        }

        public override string GetDocumentOutputText() {
            return "";
        }

        public override byte[] GetDocumentTextBytes() {
            return new byte[] { };
        }

        public override ParsedIRTextSection LoadSection(IRTextSection section) {
            var text = GetSectionText(section);

            if (string.IsNullOrEmpty(text)) {
                return null;
            }

            // Function size needed by parser to properly set instr. sizes.
            long functionSize = 0;

            if (funcToDebugInfoMap_.TryGetValue(section.ParentFunction, out var funcInfo)) {
                functionSize = funcInfo.Size;
            }

            var (sectionParser, errorHandler) = InitializeParser(functionSize);
            FunctionIR function;

            if (sectionParser == null) {
                function = new FunctionIR();
            }
            else {
                function = sectionParser.ParseSection(section, text);
            }

            return new ParsedIRTextSection(section, text.AsMemory(), function);
        }

        public override string GetSectionText(IRTextSection section) {
            if (!funcToDebugInfoMap_.TryGetValue(section.ParentFunction, out var funcInfo)) {
                return "";
            }

            return disassembler_.DisassembleToText(funcInfo);
        }

        public override ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section) {
            return default;
        }

        public override string GetSectionOutputText(IRPassOutput output) {
            return "";
        }

        public override ReadOnlyMemory<char> GetSectionPassOutputTextSpan(IRPassOutput output) {
            return ReadOnlyMemory<char>.Empty;
        }

        public override List<string> GetSectionPassOutputTextLines(IRPassOutput output) {
            return new List<string>();
        }

        public override string GetRawSectionText(IRTextSection section) {
            return null;
        }

        public override string GetRawSectionPassOutput(IRPassOutput output) {
            return "";
        }

        public override ReadOnlyMemory<char> GetRawSectionTextSpan(IRTextSection section) {
            return GetRawSectionText(section).AsMemory();
        }

        public override ReadOnlyMemory<char> GetRawSectionPassOutputSpan(IRPassOutput output) {
            return default;
        }

        protected override void Dispose(bool disposing) {
            disassembler_?.Dispose();
            debugInfo_?.Dispose();
        }
    }
}
