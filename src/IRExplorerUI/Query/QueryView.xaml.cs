using System.Windows.Controls;
using System.Windows.Data;
using IRExplorerUI.Scripting;

namespace IRExplorerUI.Query {
    public partial class QueryView : UserControl {
        private ElementQueryInfo query_;

        public QueryView() {
            InitializeComponent();
        }

        public ElementQueryInfo Query {
            get => query_;
            set {
                if (value != query_) {
                    query_ = value;
                    DataContext = query_;
                    InputElementList.ItemsSource = new CollectionView(query_.Data.InputValues);
                    OutputElementList.ItemsSource = new CollectionView(query_.Data.OutputValues);
                }
            }
        }
    }
}
