// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Compilers.Architecture;
using ProfileExplorer.Core.Diff;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Compilers.ASM;

public class ASMCompilerInfoProvider : ICompilerInfoProvider {
  private readonly ASMNameProvider names_ = new();
  private readonly ASMCompilerIRInfo ir_;
  private readonly IBinaryFileFinder binaryFileFinder_;
  private readonly IDebugFileFinder debugFileFinder_;
  private readonly IDebugInfoProviderFactory debugInfoProviderFactory_;
  private readonly IDiffFilterProvider diffFilterProvider_;

  public ASMCompilerInfoProvider(IRMode mode) {
    ir_ = new ASMCompilerIRInfo(mode);
    binaryFileFinder_ = new ASMBinaryFileFinder();
    debugFileFinder_ = new ASMDebugFileFinder();
    debugInfoProviderFactory_ = new ASMDebugInfoProviderFactory();
    diffFilterProvider_ = new ASMDiffFilterProvider(this);
  }

  public virtual string CompilerIRName => "ASM";
  public virtual string CompilerDisplayName => "ASM " + ir_.Mode;
  public virtual string OpenFileFilter =>
    "ASM, Binary, Trace Files|*.asm;*.txt;*.log;*.exe;*.dll;*.sys;*.etl|All Files|*.*";
  public virtual string OpenDebugFileFilter => "Debug Files|*.pdb|All Files|*.*";
  public virtual string DefaultSyntaxHighlightingFile => (ir_.Mode == IRMode.ARM64 ? "ARM64" : "x86") + " ASM IR";
  public ICompilerIRInfo IR => ir_;
  public INameProvider NameProvider => names_;
  public IBinaryFileFinder BinaryFileFinder => binaryFileFinder_;
  public IDebugFileFinder DebugFileFinder => debugFileFinder_;
  public IDebugInfoProviderFactory DebugInfoProviderFactory => debugInfoProviderFactory_;
  public IDiffFilterProvider DiffFilterProvider => diffFilterProvider_;

  public async Task<bool> AnalyzeLoadedFunction(FunctionIR function, IRTextSection section, ILoadedDocument loadedDoc, FunctionDebugInfo funcDebugInfo) {
    // Annotate the instructions with debug info (line numbers, source files)
    // if the debug file is specified and available.
    var debugInfo = DebugInfoProviderFactory.GetOrCreateDebugInfoProvider(section.ParentFunction, loadedDoc);

    if (debugInfo != null) {
      await Task.Run(() => {
        if (funcDebugInfo != null) {
          return debugInfo.AnnotateSourceLocations(function, funcDebugInfo);
        }
        else {
          return debugInfo.AnnotateSourceLocations(function, section.ParentFunction);
        }
      });
    }

    return true;
  }
}