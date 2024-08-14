// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.UI;

public class CallGraphPanel : GraphPanel {
  public CallGraphPanel() {
  }

  public CallGraphPanel(ISession session) {
    Session = session;
  }

  public override ToolPanelKind PanelKind => ToolPanelKind.CallGraph;
}