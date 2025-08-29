// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace ProfileExplorer.UI.Profile;

public class CallerCalleePanel : CallTreePanel {
  public CallerCalleePanel() {
  }

  public CallerCalleePanel(IUISession session) {
    Session = session;
  }

  public override ToolPanelKind PanelKind => ToolPanelKind.CallerCallee;
}