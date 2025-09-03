// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text;
using ProfileExplorer.Core.Diff;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.Core.Compilers.ASM;

public class ASMDiffFilterProvider : IDiffFilterProvider {
  private readonly ASMCompilerInfoProvider outer_;
  public ASMDiffFilterProvider(ASMCompilerInfoProvider outer) { outer_ = outer; }
  public IDiffInputFilter CreateDiffInputFilter() {
    var filter = new ASMDiffInputFilter();
    filter.Initialize(CoreSettingsProvider.DiffSettings, outer_.IR);
    return filter;
  }

  public IDiffOutputFilter CreateDiffOutputFilter() {
    return new BasicDiffOutputFilter();
  }
}