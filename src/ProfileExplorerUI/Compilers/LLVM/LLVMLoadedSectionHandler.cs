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
using ProfileExplorer.UI.Compilers.Default;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.UI;
using ProfileExplorer.UI.Query;
using ProfileExplorerUI.Session;

namespace ProfileExplorer.UI.Compilers.LLVM;

public class LLVMLoadedSectionHandler : ILoadedSectionHandler {
  public Task HandleLoadedSection(IRDocument document, FunctionIR function, IRTextSection section) {
    return Task.CompletedTask;
  }
}