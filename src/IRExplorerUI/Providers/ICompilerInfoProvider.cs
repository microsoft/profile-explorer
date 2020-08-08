// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerUI.Diff;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public interface ICompilerInfoProvider {
        string CompilerIRName { get; }

        ICompilerIRInfo IR { get; }
        INameProvider NameProvider { get; }
        ISectionStyleProvider SectionStyleProvider { get; }
        IRRemarkProvider RemarkProvider { get; }

        bool AnalyzeLoadedFunction(FunctionIR function);
        IRFoldingStrategy CreateFoldingStrategy(FunctionIR function);
        IDiffOutputFilter CreateDiffOutputFilter();
    }
}
