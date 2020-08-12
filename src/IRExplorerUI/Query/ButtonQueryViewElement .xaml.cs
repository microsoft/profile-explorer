using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using IRExplorerCore.IR;

namespace IRExplorerUI.Query {
    public partial class ButtonQueryViewElement : UserControl {
        public ButtonQueryViewElement() {
            InitializeComponent();
        }

        private void Button_Click(object sender, System.Windows.RoutedEventArgs e) {
            QueryButton button = (QueryButton)DataContext;
            button.Action?.Invoke(button, button.Data);
        }
    }
}
