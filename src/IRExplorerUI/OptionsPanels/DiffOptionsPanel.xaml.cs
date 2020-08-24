using Microsoft.Win32;
using System;
using System.Windows.Input;
using IRExplorerUI.Diff;
using System.Windows;

namespace IRExplorerUI.OptionsPanels {
    /// <summary>
    /// Interaction logic for DiffOptionsPanel.xaml
    /// </summary>
    public partial class DiffOptionsPanel : OptionsPanelBase {
        public const double DefaultHeight = 600;
        public const double MinimumHeight = 200;
        public const double DefaultWidth = 340;
        public const double MinimumWidth = 340;

        public DiffOptionsPanel() {
            InitializeComponent();
            PreviewMouseUp += DiffOptionsPanel_PreviewMouseUp;
            PreviewKeyUp += DiffOptionsPanel_PreviewKeyUp;
            ExternalAppPathTextbox.ExtensionFilter = "*.exe";
        }

        private void DiffOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e) {
            if (ExternalAppPathTextbox.IsKeyboardFocusWithin) {
                return;
            }

            NotifySettingsChanged();
        }

        private void DiffOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
            if (ExternalAppPathButton.IsMouseDirectlyOver ||
                ExternalAppPathTextbox.IsKeyboardFocusWithin) {
                return;
            }

            NotifySettingsChanged();
        }

        private void NotifySettingsChanged() {
            DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
                RaiseSettingsChanged(null);
            });
        }

        private void ExternalAppPathButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            using var centerForm = new DialogCenteringHelper(this);

            var fileDialog = new OpenFileDialog {
                DefaultExt = "bcompare.exe",
                Filter = "BC executables|bcompare.exe"
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                ExternalAppPathTextbox.Text = fileDialog.FileName;
            }
        }

        private void DefaultAppPathButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            var path = BeyondCompareDiffBuilder.FindBeyondCompareExecutable();

            if (!string.IsNullOrEmpty(path)) {
                ExternalAppPathTextbox.Text = path;
            }
            else {
                using var centerForm = new DialogCenteringHelper(this);
                MessageBox.Show($"Could not find Beyond Compare executable", "IR Explorer",
                                  MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }
    }
}
