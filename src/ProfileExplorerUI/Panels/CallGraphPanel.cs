// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorerCore2.Session;

namespace ProfileExplorer.UI;

public class CallGraphPanel : GraphPanel {
  public CallGraphPanel() {
  }

  public CallGraphPanel(IUISession session) {
    Session = session;
  }

  public override ToolPanelKind PanelKind => ToolPanelKind.CallGraph;
}