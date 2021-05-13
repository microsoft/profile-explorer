// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerUI.Diff;
using IRExplorerUI.UTC;
using IRExplorerUI.Query;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.ASM;
using System;

namespace IRExplorerUI.Compilers.ASM {
    public class ASMCompilerInfoProvider : ICompilerInfoProvider {
        private readonly ISession session_;
        private readonly UTCNameProvider names_ = new UTCNameProvider();
        private readonly UTCSectionStyleProvider styles_ = new UTCSectionStyleProvider();
        private readonly UTCRemarkProvider remarks_;
        private readonly ASMCompilerIRInfo ir_;

        public ASMCompilerInfoProvider(ISession session) {
            session_ = session;
            remarks_ = new UTCRemarkProvider(this);
            ir_ = new ASMCompilerIRInfo();
        }

        public string CompilerIRName => "ASM";

        public string OpenFileFilter => "Asm Files|*.asm|All Files|*.*";

        public string DefaultSyntaxHighlightingFile => "ASM";

        public ISession Session => session_;

        public ICompilerIRInfo IR => ir_;

        public INameProvider NameProvider => names_;

        public ISectionStyleProvider SectionStyleProvider => styles_;

        public IRRemarkProvider RemarkProvider => remarks_;

        public List<QueryDefinition> BuiltinQueries => throw new NotImplementedException();

        public List<FunctionTaskDefinition> BuiltinFunctionTasks => throw new NotImplementedException();

        public List<FunctionTaskDefinition> ScriptFunctionTasks => throw new NotImplementedException();

        public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
            return true;
        }

        public IDiffOutputFilter CreateDiffOutputFilter() {
            throw new NotImplementedException();
        }

        public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
            return new BaseBlockFoldingStrategy(function);
        }

        public void ReloadSettings() {
            IRModeUtilities.SetIRModeFromSettings(ir_);
        }
    }
}
