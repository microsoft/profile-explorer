using IRExplorer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IRExplorer.OptionsPanels {
    /// <summary>
    /// Interaction logic for DiffOptionsPanel.xaml
    /// </summary>
    public partial class DiffOptionsPanel : OptionsPanelBase {
        public DiffOptionsPanel() {
            InitializeComponent();
            PreviewMouseUp += DiffOptionsPanel_PreviewMouseUp;
            PreviewKeyUp += DiffOptionsPanel_PreviewKeyUp;
        }

        private void DiffOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e) {
            NotifySettingsChanged();
        }

        private void DiffOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
            NotifySettingsChanged();
        }

        private void NotifySettingsChanged() {
            DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
                RaiseSettingsChanged(null);
            });
        }
    }
}
