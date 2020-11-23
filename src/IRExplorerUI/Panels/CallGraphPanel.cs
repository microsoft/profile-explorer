namespace IRExplorerUI {
    public class CallGraphPanel : GraphPanel {
        public CallGraphPanel(ISession session) {
            Session = session;
        }

        public override ToolPanelKind PanelKind => ToolPanelKind.CallGraph;
    }
}
