// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using ProfileExplorer.UI;
using ProfileExplorerCore;
using ProfileExplorerCore.Binary;
using ProfileExplorerCore.Profile.Data;
using ProfileExplorerCore.Providers;
using ProfileExplorerCore.Session;
using ProfileExplorerCore.Settings;
using ProfileExplorerCore.Utilities;

namespace ProfileExplorerUI.Session;

public interface IUILoadedDocument : ILoadedDocument {
  public event EventHandler DocumentChanged;

  public void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section);

  public object LoadPanelState(IToolPanel panel, IRTextSection section);

  public void SetupDocumentWatcher();

  new public IUILoadedDocumentState SerializeDocument();

  public void ChangeDocumentWatcherState(bool enabled);
}