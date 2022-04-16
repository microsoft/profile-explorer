// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorerUI.Diff;
using IRExplorerCore;
using IRExplorerCore.IR;
using System.Collections.Generic;
using IRExplorerUI.Query;
using System.Threading.Tasks;
using IRExplorerUI.Compilers;
using IRExplorerUI.Compilers.ASM;

namespace IRExplorerUI {
    public interface ICompilerInfoProvider {
        string CompilerIRName { get; }
        string CompilerDisplayName { get; }
        string DefaultSyntaxHighlightingFile { get; }
        ISession Session { get; }

        ICompilerIRInfo IR { get; }
        INameProvider NameProvider { get; }
        ISectionStyleProvider SectionStyleProvider { get; }
        IRRemarkProvider RemarkProvider { get; }
        List<QueryDefinition> BuiltinQueries { get; }
        List<FunctionTaskDefinition> BuiltinFunctionTasks { get; }
        List<FunctionTaskDefinition> ScriptFunctionTasks { get; }
        string OpenFileFilter { get; }
        string OpenDebugFileFilter { get; }

        void ReloadSettings();

        bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section);
        Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section);
        Task HandleLoadedDocument(LoadedDocument document, string modulePath);
        IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function);
        IDiffInputFilter CreateDiffInputFilter();
        IDiffOutputFilter CreateDiffOutputFilter();
        IDebugInfoProvider CreateDebugInfoProvider(string imagePath);
        Task<string> FindDebugInfoFile(string imagePath, SymbolFileSourceOptions options = null, string disasmOutputPath = null);
        Task<string> FindBinaryFile(BinaryFileDescription binaryFile, SymbolFileSourceOptions options = null);
        IDisassembler CreateDisassembler(string modulePath);
    }
}
