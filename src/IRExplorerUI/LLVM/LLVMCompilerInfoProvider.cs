// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.UTC;
using IRExplorerCore.LLVM;

namespace IRExplorerUI.LLVM {
    public class LLVMCompilerInfoProvider : ICompilerInfoProvider {
        private LLVMCompilerIRInfo ir_;
        private UTCNameProvider names_;
        private UTCRemarkProvider remarks_;
        private UTCSectionStyleProvider styles_;

        public LLVMCompilerInfoProvider() {
            ir_ = new LLVMCompilerIRInfo();
            styles_ = new UTCSectionStyleProvider();
            names_ = new UTCNameProvider();
            remarks_ = new UTCRemarkProvider();
        }

        public string CompilerIRName => "LLVM";
        public ICompilerIRInfo IR => ir_;
        public INameProvider NameProvider => names_;
        public ISectionStyleProvider SectionStyleProvider => styles_;
        public IRRemarkProvider RemarkProvider => remarks_;

        public IRFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            return new UTCFoldingStrategy(function);
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            return new UTCDiffOutputFilter();
        }

        public bool AnalyzeLoadedFunction(FunctionIR function) {
            //? TODO: var loopGraph = new LoopGraph(function);
            //loopGraph.FindLoops();
            return true;
        }
    }
}
