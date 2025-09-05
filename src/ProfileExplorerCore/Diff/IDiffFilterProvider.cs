// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Providers;

namespace ProfileExplorer.Core.Diff;

public interface IDiffFilterProvider {
  IDiffInputFilter CreateDiffInputFilter();
  IDiffOutputFilter CreateDiffOutputFilter();
}
