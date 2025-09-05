// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Providers;

namespace ProfileExplorer.UI.Document;

public class BasicBlockFoldingStrategyProvider : IBlockFoldingStrategyProvider {
  public IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function) {
    return new BasicBlockFoldingStrategy(function);
  }
}