// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerUI.Diff;
using IRExplorerUI.Query;
using IRExplorerUI.Query.Builtin;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerCore.UTC;
using System.Collections.Generic;
using IRExplorerUI.UTC;

namespace IRExplorerUI.Compilers.UTC {
    public class UTCCompilerInfoProvider : ICompilerInfoProvider {
        private UTCCompilerIRInfo ir_;
        private UTCNameProvider names_;
        private UTCRemarkProvider remarks_;
        private UTCSectionStyleProvider styles_;

        public UTCCompilerInfoProvider() {
            ir_ = new UTCCompilerIRInfo();
            styles_ = new UTCSectionStyleProvider();
            names_ = new UTCNameProvider();
            remarks_ = new UTCRemarkProvider();
        }

        public string CompilerIRName => "UTC";
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

        public List<ElementQueryDefinition> BuiltinQueries => new List<ElementQueryDefinition>() {
            InstructionSSAInfoQuery.GetDefinition(),
            OperandSSAInfoQuery.GetDefinition(),
            UTCValueNumberQuery.GetDefinition(),
            UTCBuiltinInterferenceActions.GetDefinition(),
            UTCBuiltinInterferenceQuery.GetDefinition()
        };

        public bool AnalyzeLoadedFunction(FunctionIR function) {
            var loopGraph = new LoopGraph(function);
            loopGraph.FindLoops();
            return true;
        }
    }
}
