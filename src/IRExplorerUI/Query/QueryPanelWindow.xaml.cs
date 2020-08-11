using System.Windows;
using IRExplorerUI.Document;
using IRExplorerUI.Query.Builtin;

namespace IRExplorerUI.Query {
    public partial class QueryPanelWindow : DraggablePopup {
        public const double DefaultHeight = 300;
        public const double MinimumHeight = 100;
        public const double DefaultWidth = 250;
        public const double MinimumWidth = 100;

        public QueryPanelWindow(Point position, double width, double height,
                                UIElement referenceElement, ISessionManager session) {
            InitializeComponent();
            Initialize(position, width, height, referenceElement);
            PanelResizeGrip.ResizedControl = this;

            Session = session;

            var queries = Session.CompilerInfo.BuiltinQueries;

            foreach (var query in queries) {
                QPanel.AddQuery(query);
                //? TODO: should be done only if a query is used
                query.CreateQueryInstance(session);
            }
        }

        public ISessionManager Session { get; set; }
    }
}
