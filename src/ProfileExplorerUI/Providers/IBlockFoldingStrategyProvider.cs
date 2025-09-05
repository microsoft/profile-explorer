// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Windows.Media;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.UI.Providers;

public interface IBlockFoldingStrategyProvider {
  IBlockFoldingStrategy CreateFoldingStrategy(FunctionIR function);
}
