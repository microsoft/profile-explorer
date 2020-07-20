using Microsoft.Win32;
using System;
using System.Windows.Input;

namespace IRExplorer.OptionsPanels {
    /// <summary>
    /// Interaction logic for DiffOptionsPanel.xaml
    /// </summary>
    public partial class DiffOptionsPanel : OptionsPanelBase {
        public const double DefaultHeight = 560;
        public const double MinimumHeight = 300;
        public const double DefaultWidth = 320;
        public const double MinimumWidth = 320;

        public DiffOptionsPanel() {
            InitializeComponent();
            PreviewMouseUp += DiffOptionsPanel_PreviewMouseUp;
            PreviewKeyUp += DiffOptionsPanel_PreviewKeyUp;
            ExternalAppPathTextbox.ExtensionFilter = "*.exe";
        }

        private void DiffOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e) {
            if (ExternalAppPathTextbox.IsKeyboardFocusWithin ||
                ExternalAppArgsTextbox.IsKeyboardFocusWithin) {
                return;
            }

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

        private void ExternalAppPathButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            var fileDialog = new OpenFileDialog {
                DefaultExt = "*.exe",
                Filter = "Executables|*.exe"
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                ExternalAppPathTextbox.Text = fileDialog.FileName;
            }
        }
    }
}
