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

        public event EventHandler ValueChanged;

        public override List<object> LoadValues() {
            var provider = compilerInfo_.SectionStyleProvider;

            if (provider.LoadSettings()) {
                return provider.SectionNameMarkers.ToObjectList();
            }

            return null;
        }

        public override void UpdateValues(List<object> values) {
            if (HasChanges) {
                var provider = compilerInfo_.SectionStyleProvider;
                provider.SectionNameMarkers = values.ConvertAll(item => (MarkedSectionName)item);
            }
        }

        public override bool SaveValues(List<object> values) {
            if (HasChanges) {
                var provider = compilerInfo_.SectionStyleProvider;
                provider.SectionNameMarkers = values.ConvertAll(item => (MarkedSectionName)item);
                return provider.SaveSettings();
            }

            return true;
        }

        public override object CreateNewValue() {
            return new MarkedSectionName();
        }

        public override List<object> ResetValues() {
            return null;
        }

        public override void OnValueChanged(object value) {
            ValueChanged?.Invoke(this, null);
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

        private void NotifySettingsChanged(bool force = false) {
            DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
                RaiseSettingsChanged(force);
            });
        }

        private void EditButton_Click(object sender, RoutedEventArgs e) {
            //var settingsPath = App.GetSectionsDefinitionFilePath("utc");
            //App.LaunchSettingsFileEditor(settingsPath);
            
            // Keep the settings popup open while showing the editor over it.
            if (Parent is OptionsPanelHostWindow popup) {
                popup.StaysOpen = true;
            }

            var valueManager = new SectionNameValueManager(compilerInfo_);
            valueManager.ValueChanged += (sender, e) => {
                NotifySettingsChanged(true);
            };

            // Show the value editor.
            var position = ParentPosition;
            position.Offset(24, 24);
            var editorPopup = new PropertyEditorPopup(valueManager, position, 600, 400, null);
            
            editorPopup.PopupClosed += (sender, e) => {
                if (Parent is OptionsPanelHostWindow popup) {
                    popup.StaysOpen = false;
                }

                editorPopup.IsOpen = false;

                if (valueManager.HasChanges) {
                    NotifySettingsChanged(true);
                }
            };

            editorPopup.PanelTitle = "Section name styles";
            editorPopup.StaysOpen = true;
            editorPopup.IsOpen = true;
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e) {
            compilerInfo_.SectionStyleProvider.LoadSettings();
        }
    }
}
