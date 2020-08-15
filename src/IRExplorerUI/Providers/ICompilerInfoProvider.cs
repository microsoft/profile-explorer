// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerUI.Diff;
using IRExplorerCore;
using IRExplorerCore.IR;
using System.Collections.Generic;
using IRExplorerUI.Query;
using System.Threading.Tasks;
using System;

namespace IRExplorerUI {
    public interface ICompilerInfoProvider {
        string CompilerIRName { get; }

        ICompilerIRInfo IR { get; }
        INameProvider NameProvider { get; }
        ISectionStyleProvider SectionStyleProvider { get; }
        IRRemarkProvider RemarkProvider { get; }
        List<ElementQueryDefinition> BuiltinQueries { get; }
        List<DocumentTaskDefinition> BuiltinScripts { get; }

        bool AnalyzeLoadedFunction(FunctionIR function);
        IRFoldingStrategy CreateFoldingStrategy(FunctionIR function);
        IDiffOutputFilter CreateDiffOutputFilter();
    }
}
