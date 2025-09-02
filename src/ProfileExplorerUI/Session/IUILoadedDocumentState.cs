// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using ProfileExplorer.UI;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;

namespace ProfileExplorerUI.Session;

public interface IUILoadedDocumentState : ILoadedDocumentState {
  // This interface is being removed as part of the refactoring.
  // Panel states are now managed by PanelStateManager and stored in SessionState.
}