using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using IRExplorerUI.Controls;
using IRExplorerUI.Utilities;

namespace IRExplorerUI.OptionsPanels {
    public class SectionNameValueManager : PropertyValueManager {
        private ICompilerInfoProvider compilerInfo_;

        public SectionNameValueManager(ICompilerInfoProvider compilerInfo) {
            compilerInfo_ = compilerInfo;
        }

        public override List<object> LoadValues() {
            var prov = compilerInfo_.SectionStyleProvider;

            if (prov.LoadSettings()) {
                return prov.SectionNameMarkers.ToObjectList();
            }

            return null;
        }

        public override bool SaveValues(List<object> values) {
            return true;
        }

        public override object CreateNewValue() {
            return new MarkedSectionName();
        }

        public override List<object> ResetValues() {
            return null;
        }
    }

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
                var valueManager = new SectionNameValueManager(compilerInfo_);
                var p = new PropertyEditorPopup(valueManager, new Point(0, 0), 400, 300, this);
                p.PanelTitle = "Section name styles";
                p.IsOpen = true;
            }

        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e) {
            compilerInfo_.SectionStyleProvider.LoadSettings();
        }
    }
}
