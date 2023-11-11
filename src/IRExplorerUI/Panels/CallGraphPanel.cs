// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace IRExplorerUI {
    public class CallGraphPanel : GraphPanel {
        public CallGraphPanel() : base() {

        }

        public CallGraphPanel(ISession session) {
            Session = session;
        }

        public override ToolPanelKind PanelKind => ToolPanelKind.CallGraph;
    }
}
