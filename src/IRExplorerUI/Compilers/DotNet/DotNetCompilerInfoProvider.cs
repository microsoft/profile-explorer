// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Threading.Tasks;
using IRExplorerCore;

namespace IRExplorerUI.Compilers.ASM;

public class DotNetCompilerInfoProvider : ASMCompilerInfoProvider {
  public DotNetCompilerInfoProvider(IRMode mode, ISession session) : base(mode, session) {
  }

  public override string CompilerIRName => "DotNet";
  public override string CompilerDisplayName => ".NET " + IR.Mode;
  public override string OpenFileFilter =>
    "ASM, Binary, Trace Files|*.asm;*.txt;*.log;*.exe;*.dll;*.sys;*.etl|All Files|*.*";
  public override string OpenDebugFileFilter => "Debug Files|*.json|All Files|*.*";

  public override async Task HandleLoadedDocument(LoadedDocument document, string modulePath) {
    document.DebugInfoFile = await FindDebugInfoFileAsync(modulePath);
  }
}