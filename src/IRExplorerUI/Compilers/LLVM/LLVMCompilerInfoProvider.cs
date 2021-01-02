// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.LLVM;

namespace IRExplorerUI.Compilers.LLVM {
    public class LLVMCompilerInfoProvider : ICompilerInfoProvider {
        private LLVMCompilerIRInfo ir_;
        private ISession session_;
        private UTCNameProvider names_;
        private LLVMRemarkProvider remarks_;
        private UTCSectionStyleProvider styles_;

        public LLVMCompilerInfoProvider() {
            ir_ = new LLVMCompilerIRInfo();
            styles_ = new UTCSectionStyleProvider();
            names_ = new UTCNameProvider();
            remarks_ = new LLVMRemarkProvider();
        }

        public string CompilerIRName => "LLVM";
        public string DefaultSyntaxHighlightingFile => "LLVM";
        public ISession Session => session_;
        public ICompilerIRInfo IR => ir_;
        public INameProvider NameProvider => names_;
        public ISectionStyleProvider SectionStyleProvider => styles_;
        public IRRemarkProvider RemarkProvider => remarks_;

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            return new BaseBlockFoldingStrategy(function);
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            return new UTCDiffOutputFilter();
        }

        public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>() { };
        public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>() { };
        public List<FunctionTaskDefinition> ScriptFunctionTasks => new List<FunctionTaskDefinition>() { };

        public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
            //? TODO: var loopGraph = new LoopGraph(function);
            //loopGraph.FindLoops();
            return true;
        }

        public void ReloadSettings() {
        }
    }
}
