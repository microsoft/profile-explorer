// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ProfileExplorer.UI;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;

namespace ProfileExplorerUI.Session;

public interface IUILoadedDocumentState : ILoadedDocumentState{
  List<Tuple<int, PanelObjectPairState>> PanelStates { get; set; }
}