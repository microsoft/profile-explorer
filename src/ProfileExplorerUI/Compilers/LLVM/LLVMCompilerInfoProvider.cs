// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;
using ProfileExplorerCore;
using ProfileExplorerCore.IR;
using ProfileExplorerCore.Compilers.LLVM;
using ProfileExplorerCore.Session;
using ProfileExplorerCore.Providers;
using ProfileExplorerCore.Binary;
using ProfileExplorerCore.Diff;
using ProfileExplorer.UI.Compilers.Default;
using ProfileExplorerCore.Settings;
using ProfileExplorer.UI;
using ProfileExplorer.UI.Query;
using ProfileExplorerUI.Session;
/*using ProfileExplorer.UI.Query;
using ProfileExplorer.UI.Compilers.Default;*/

//namespace ProfileExplorer.UI.Compilers.LLVM;

public class LLVMCompilerInfoProvider : IUICompilerInfoProvider {
  private LLVMCompilerIRInfo ir_;
  private IUISession session_;
  private DefaultNameProvider names_;
  private DefaultRemarkProvider remarks_;
  private DefaultSectionStyleProvider styles_;

  public LLVMCompilerInfoProvider(IUISession session) {
    session_ = session;
    ir_ = new LLVMCompilerIRInfo();
    styles_ = new DefaultSectionStyleProvider(this);
    names_ = new DefaultNameProvider();
    remarks_ = new DefaultRemarkProvider(this);
  }

  public string CompilerIRName => "LLVM";
  public string CompilerDisplayName => "LLVM";
  public string DefaultSyntaxHighlightingFile => "LLVM";
  public string OpenFileFilter =>
    "IR Files|*.txt;*.log;*.ir;*.tup;*.out;*.pex|Profile Explorer Session Files|*.pex|All Files|*.*";
  public string OpenDebugFileFilter => "Debug Files|*.pdb|All Files|*.*";
  public IUISession Session => session_;
  public ICompilerIRInfo IR => ir_;
  public INameProvider NameProvider => names_;
  public ISectionStyleProvider SectionStyleProvider => styles_;
  public IRRemarkProvider RemarkProvider => remarks_;
  public List<QueryDefinition> BuiltinQueries => new();
  public List<FunctionTaskDefinition> BuiltinFunctionTasks => new();
  public List<FunctionTaskDefinition> ScriptFunctionTasks => new();

  ISession ICompilerInfoProvider.Session => Session;

  public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
    return new BasicBlockFoldingStrategy(function);
  }

  public IDiffInputFilter CreateDiffInputFilter() {
    return null;
  }

  public IDiffOutputFilter CreateDiffOutputFilter() {
    return new DefaultDiffOutputFilter();
  }

  public IDebugInfoProvider CreateDebugInfoProvider(DebugFileSearchResult debugFile) {
    return null;
  }

  DebugFileSearchResult ICompilerInfoProvider.FindDebugInfoFile(SymbolFileDescriptor symbolFile,
                                                                SymbolFileSourceSettings settings) {
    return DebugFileSearchResult.None;
  }

  public async Task<DebugFileSearchResult> FindDebugInfoFileAsync(string imagePath,
                                                                  SymbolFileSourceSettings settings = null) {
    return DebugFileSearchResult.None;
  }

  public async Task<DebugFileSearchResult> FindDebugInfoFileAsync(SymbolFileDescriptor symbolFile,
                                                                  SymbolFileSourceSettings settings = null) {
    return DebugFileSearchResult.None;
  }

  DebugFileSearchResult ICompilerInfoProvider.FindDebugInfoFile(string imagePath, SymbolFileSourceSettings settings) {
    return DebugFileSearchResult.None;
  }

  public async Task<IDebugInfoProvider> GetOrCreateDebugInfoProvider(IRTextFunction function) {
    return null;
  }

  public Task<BinaryFileSearchResult> FindBinaryFileAsync(BinaryFileDescriptor binaryFile,
                                                          SymbolFileSourceSettings settings = null) {
    return null;
  }

  public async Task<bool> AnalyzeLoadedFunction(FunctionIR function, IRTextSection section,
                                                FunctionDebugInfo funcDebugInfo) {
    //? TODO: var loopGraph = new LoopGraph(function);
    //loopGraph.FindLoops();
    return true;
  }

  public Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section) {
    return Task.CompletedTask;
  }

  public Task HandleLoadedDocument(IUILoadedDocument document, string modulePath) {
    return Task.CompletedTask;
  }

  public Task ReloadSettings() {
    return Task.CompletedTask;
  }

  public async Task<DebugFileSearchResult> FindDebugInfoFile(string imagePath, SymbolFileSourceSettings settings) {
    return Utils.LocateDebugInfoFile(imagePath, ".pdb");
  }

  public async Task<DebugFileSearchResult>
    FindDebugInfoFile(SymbolFileDescriptor symbolFile, SymbolFileSourceSettings settings = null) {
    return null;
  }
}