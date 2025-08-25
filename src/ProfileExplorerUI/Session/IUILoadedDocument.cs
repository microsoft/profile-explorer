// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using ProfileExplorer.UI;
using ProfileExplorerCore2;
using ProfileExplorerCore2.Binary;
using ProfileExplorerCore2.Profile.Data;
using ProfileExplorerCore2.Providers;
using ProfileExplorerCore2.Session;
using ProfileExplorerCore2.Settings;
using ProfileExplorerCore2.Utilities;

namespace ProfileExplorerUI.Session;

public interface IUILoadedDocument : ILoadedDocument {
  public event EventHandler DocumentChanged;

  public void SavePanelState(object stateObject, IToolPanel panel, IRTextSection section);

  public object LoadPanelState(IToolPanel panel, IRTextSection section);

  public void SetupDocumentWatcher();

  new public IUILoadedDocumentState SerializeDocument();

  public void ChangeDocumentWatcherState(bool enabled);
}