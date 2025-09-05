// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Compilers.LLVM;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Diff;
using ProfileExplorer.Core.Compilers.Default;

namespace ProfileExplorer.Core.Compilers.LLVM;

public class LLVMCompilerInfoProvider : ICompilerInfoProvider {
  private LLVMCompilerIRInfo ir_;
  private DefaultNameProvider names_;
  private readonly IBinaryFileFinder binaryFileFinder_;
  private readonly IDebugFileFinder debugFileFinder_;
  private readonly IDebugInfoProviderFactory debugInfoProviderFactory_;
  private readonly IDiffFilterProvider diffFilterProvider_;

  public LLVMCompilerInfoProvider() {
    ir_ = new LLVMCompilerIRInfo();
    names_ = new DefaultNameProvider();
    binaryFileFinder_ = new LLVMBinaryFileFinder();
    debugFileFinder_ = new LLVMDebugFileFinder();
    debugInfoProviderFactory_ = new LLVMDebugInfoProviderFactory();
    diffFilterProvider_ = new LLVMDiffFilterProvider();
  }

  public string CompilerIRName => "LLVM";
  public CompilerIRKind CompilerIRKind => CompilerIRKind.LLVM;
  public string CompilerDisplayName => "LLVM";
  public string DefaultSyntaxHighlightingFile => "LLVM";
  public string OpenFileFilter =>
    "IR Files|*.txt;*.log;*.ir;*.tup;*.out;*.pex|Profile Explorer Session Files|*.pex|All Files|*.*";
  public string OpenDebugFileFilter => "Debug Files|*.pdb|All Files|*.*";
  public ICompilerIRInfo IR => ir_;
  public INameProvider NameProvider => names_;
  public IBinaryFileFinder BinaryFileFinder => binaryFileFinder_;
  public IDebugFileFinder DebugFileFinder => debugFileFinder_;
  public IDebugInfoProviderFactory DebugInfoProviderFactory => debugInfoProviderFactory_;
  public IDiffFilterProvider DiffFilterProvider => diffFilterProvider_;

  public async Task<bool> AnalyzeLoadedFunction(FunctionIR function, IRTextSection section,
                                                ILoadedDocument loadedDoc, FunctionDebugInfo funcDebugInfo) {
    //? TODO: var loopGraph = new LoopGraph(function);
    //loopGraph.FindLoops();
    return true;
  }
}