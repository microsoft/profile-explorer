using System;
using System.Windows.Controls;
using System.Windows.Data;
using IRExplorerUI.Scripting;

namespace IRExplorerUI.Query {
    public partial class QueryView : UserControl {
        private ElementQueryDefinition query_;

        public QueryView() {
            InitializeComponent();
            this.DataContextChanged += QueryView_DataContextChanged;
        }

        private void QueryView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e) {
            if (e.OldValue != null) {
                Closed -= ((ElementQueryInfoView)e.OldValue).OnClose;
            }

            if (e.NewValue != null) {
                Closed += ((ElementQueryInfoView)e.NewValue).OnClose;
            }
        }

        public ElementQueryDefinition Query {
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

        public event EventHandler Closed;

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            Closed?.Invoke(this, null);
        }
    }
}
