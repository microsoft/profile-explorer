// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text;
using ProfileExplorer.Core.Compilers.Default;
using ProfileExplorer.Core.Diff;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Compilers.LLVM;

public class LLVMDiffFilterProvider : IDiffFilterProvider {
  public IDiffInputFilter CreateDiffInputFilter() => null;
  public IDiffOutputFilter CreateDiffOutputFilter() => new DefaultDiffOutputFilter();
}