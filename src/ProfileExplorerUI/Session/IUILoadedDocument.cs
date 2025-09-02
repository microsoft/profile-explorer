// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using ProfileExplorer.UI;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Binary;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Settings;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorerUI.Session;

public interface IUILoadedDocument : ILoadedDocument {
  public void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section);

  public object LoadPanelState(IToolPanel panel, IRTextSection section);

  new public IUILoadedDocumentState SerializeDocument();
}