using System;
using System.Windows;
using System.Windows.Input;
using IRExplorerUI.Controls;
using IRExplorerUI.Utilities;

namespace IRExplorerUI.OptionsPanels {
    public partial class SectionOptionsPanel : OptionsPanelBase {
        public const double DefaultHeight = 320;
        public const double MinimumHeight = 200;
        public const double DefaultWidth = 350;
        public const double MinimumWidth = 350;

        private ICompilerInfoProvider compilerInfo_;

        public SectionOptionsPanel(ICompilerInfoProvider compilerInfo) {
            InitializeComponent();
            compilerInfo_ = compilerInfo;
            PreviewMouseUp += SectionOptionsPanel_PreviewMouseUp;
            PreviewKeyUp += SectionOptionsPanel_PreviewKeyUp;
        }

        private void SectionOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e) {
            NotifySettingsChanged();
        }

        private void SectionOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
            NotifySettingsChanged();
        }

        private void NotifySettingsChanged(bool force = false) {
            DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
                RaiseSettingsChanged(force);
            });
        }

        private void EditButton_Click(object sender, RoutedEventArgs e) {
            var valueManager = new SectionNameValueManager(compilerInfo_);
            valueManager.ValueChanged += (sender, e) => {
                NotifySettingsChanged(true);
            };

            var editorPopup = 
                PropertyEditorPopup.ShowOverPanel(this, valueManager,
                                                  "Section name styles", 600, 400);
            editorPopup.Closed += (sender, args) => {
                if (valueManager.HasChanges) {
                    valueManager.SaveValues(editorPopup.Values);
                }
            };
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e) {
            compilerInfo_.SectionStyleProvider.LoadSettings();
        }
    }
}
