namespace IRExplorerUI {
    public class CallerCalleePanel : CallTreePanel {
        public CallerCalleePanel() : base() {

        }

        public CallerCalleePanel(ISession session) {
            Session = session;
        }

        public override ToolPanelKind PanelKind => ToolPanelKind.CallerCallee;
    }
}
