// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
namespace ProfileExplorer.UI.Profile;

public class CallerCalleePanel : CallTreePanel {
  public CallerCalleePanel() {
  }

  public CallerCalleePanel(ISession session) {
    Session = session;
  }

  public override ToolPanelKind PanelKind => ToolPanelKind.CallerCallee;
}