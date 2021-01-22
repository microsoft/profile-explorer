using System;
using System.Windows;
using System.Windows.Input;
using IRExplorerUI.Controls;

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

        private void NotifySettingsChanged() {
            DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
                RaiseSettingsChanged(null);
            });
        }

        private void EditButton_Click(object sender, RoutedEventArgs e) {
            //var settingsPath = App.GetSectionsDefinitionFilePath("utc");
            //App.LaunchSettingsFileEditor(settingsPath);

            var prov = compilerInfo_.SectionStyleProvider;

            if (prov.LoadSettings()) {
                var p = new PropertyEditorPopup(new Point(0, 0), 400, 300, this);
                p.PanelTitle = "Section name styles";
                p.Editor.Initialize(prov.SectionNameMarkers);
                p.IsOpen = true;
            }

        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e) {
            compilerInfo_.SectionStyleProvider.LoadSettings();
        }
    }
}
