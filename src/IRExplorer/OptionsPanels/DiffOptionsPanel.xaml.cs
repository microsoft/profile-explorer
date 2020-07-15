using System;
using System.Windows.Input;

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
