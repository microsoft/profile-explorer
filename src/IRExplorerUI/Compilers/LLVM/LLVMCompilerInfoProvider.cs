// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerCore.LLVM;
using IRExplorerUI.Diff;
using IRExplorerUI.Query;
using IRExplorerUI.Compilers.Default;

namespace IRExplorerUI.Compilers.LLVM;

public class LLVMCompilerInfoProvider : ICompilerInfoProvider {
  private LLVMCompilerIRInfo ir_;
  private ISession session_;
  private DefaultNameProvider names_;
  private DefaultRemarkProvider remarks_;
  private DefaultSectionStyleProvider styles_;

  public LLVMCompilerInfoProvider() {
    ir_ = new LLVMCompilerIRInfo();
    styles_ = new DefaultSectionStyleProvider(this);
    names_ = new DefaultNameProvider();
    remarks_ = new DefaultRemarkProvider(this);
  }

  public string CompilerIRName => "LLVM";
  public string CompilerDisplayName => "LLVM";
  public string DefaultSyntaxHighlightingFile => "LLVM";
  public string OpenFileFilter =>
    "IR Files|*.txt;*.log;*.ir;*.tup;*.out;*.irx|IR Explorer Session Files|*.irx|All Files|*.*";
  public string OpenDebugFileFilter => "Debug Files|*.pdb|All Files|*.*";
  public ISession Session => session_;
  public ICompilerIRInfo IR => ir_;
  public INameProvider NameProvider => names_;
  public ISectionStyleProvider SectionStyleProvider => styles_;
  public IRRemarkProvider RemarkProvider => remarks_;
  public List<QueryDefinition> BuiltinQueries => new List<QueryDefinition>();
  public List<FunctionTaskDefinition> BuiltinFunctionTasks => new List<FunctionTaskDefinition>();
  public List<FunctionTaskDefinition> ScriptFunctionTasks => new List<FunctionTaskDefinition>();

  public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
    return new BasicBlockFoldingStrategy(function);
  }

  public IDiffInputFilter CreateDiffInputFilter() {
    return null;
  }

  public IDiffOutputFilter CreateDiffOutputFilter() {
    return new DefaultDiffOutputFilter();
  }

  public IDebugInfoProvider CreateDebugInfoProvider(string imagePath) {
    return new PDBDebugInfoProvider(App.Settings.SymbolOptions);
  }

  public IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile) {
    return null;
  }

  public async Task<DebugFileSearchResult> FindDebugInfoFile(string imagePath, SymbolFileSourceOptions options) {
    return Utils.LocateDebugInfoFile(imagePath, ".pdb");
  }

  public Task<BinaryFileSearchResult> FindBinaryFile(BinaryFileDescriptor binaryFile,
                                                     SymbolFileSourceOptions options = null) {
    return null;
  }

  public bool AnalyzeLoadedFunction(FunctionIR function, IRTextSection section) {
    //? TODO: var loopGraph = new LoopGraph(function);
    //loopGraph.FindLoops();
    return true;
  }

  public Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section) {
    return Task.CompletedTask;
  }

  public Task HandleLoadedDocument(LoadedDocument document, string modulePath) {
    return Task.CompletedTask;
  }

  public void ReloadSettings() {
  }
}
