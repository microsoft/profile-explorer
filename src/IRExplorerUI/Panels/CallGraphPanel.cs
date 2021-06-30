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
