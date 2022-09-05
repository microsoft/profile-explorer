using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Compilers.ASM;

namespace IRExplorerUI.Compilers {
    public sealed class DisassemblerSectionLoader : IRTextSectionLoader {
        private IRTextSummary summary_;
        private string binaryFilePath_;
        private Disassembler disassembler_;
        private IDebugInfoProvider debugInfo_;
        private ICompilerInfoProvider compilerInfo_;
        private Dictionary<IRTextFunction, FunctionDebugInfo> funcToDebugInfoMap_;
        private DebugFileSearchResult debugInfoFile_;
        private bool isManagedImage_;

        public DebugFileSearchResult DebugInfoFile => debugInfoFile_;

        public DisassemblerSectionLoader(string binaryFilePath, ICompilerInfoProvider compilerInfo,
                                         IDebugInfoProvider debugInfo) {
            Initialize(compilerInfo.IR, cacheEnabled: false);
            binaryFilePath_ = binaryFilePath;
            compilerInfo_ = compilerInfo;
            debugInfo_ = debugInfo;
            isManagedImage_ = debugInfo != null;
            summary_ = new IRTextSummary();
            funcToDebugInfoMap_ = new Dictionary<IRTextFunction, FunctionDebugInfo>();
        }

        public override IRTextSummary LoadDocument(ProgressInfoHandler progressHandler) {
            progressHandler?.Invoke(null, new SectionReaderProgressInfo(true));

            if (!InitializeDebugInfo()) {
                return summary_;
            }

            if (!isManagedImage_) {
                // This preloads all code sections in the binary.
                disassembler_ = Disassembler.CreateForBinary(binaryFilePath_, debugInfo_);
            }

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

            progressHandler?.Invoke(null, new SectionReaderProgressInfo(false));
            return summary_;
        }

        private bool InitializeDebugInfo() {
            if (debugInfo_ != null) {
                if (debugInfo_.LoadDebugInfo("")) {
                    // For managed code, the code data is found on each function.
                    disassembler_ = Disassembler.CreateForMachine(debugInfo_);
                }

                return true;
            }

            debugInfo_ = compilerInfo_.CreateDebugInfoProvider(binaryFilePath_);
            debugInfoFile_ = compilerInfo_.FindDebugInfoFile(binaryFilePath_).Result;

            if (debugInfoFile_.Found && debugInfo_.LoadDebugInfo(debugInfoFile_)) {
                return true;
            }

            debugInfo_.Dispose();
            debugInfo_ = null;
            return false;
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

            if (isManagedImage_) {
                // For managed code, the code data is found on each function as a byte array.
                var methodCode = ((DotNetDebugInfoProvider)debugInfo_).FindMethodCode(funcInfo);

                if (methodCode != null) {
                    var code = methodCode.GetCodeBytes();

                    if (code != null) {
                        disassembler_.UseSymbolNameResolver(address => methodCode.FindCallTarget(address));
                        return disassembler_.DisassembleToText(code, funcInfo.StartRVA);
                    }
                }

                return "";
            }

            return disassembler_.DisassembleToText(funcInfo);
        }

        public override ReadOnlyMemory<char> GetSectionTextSpan(IRTextSection section) {
            return GetSectionText(section).AsMemory();
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
            return GetSectionText(section);
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
