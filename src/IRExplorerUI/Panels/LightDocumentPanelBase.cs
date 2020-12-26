// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using IRExplorerUI.OptionsPanels;

namespace IRExplorerUI {
    public class LightDocumentPanelBase : ToolPanelControl {
        private LightIRDocument textView_;
        private LightDocumentSettings settings_;
        private bool optionsPanelVisible_;
        private OptionsPanelHostWindow optionsPanel_;

        public void Initialize(LightIRDocument textView) {
            textView_ = textView;
        }

        public LightDocumentSettings Settings {
            get => settings_;
            set {
                settings_ = value;
                textView_.Settings = Settings;
            }
        }

        public override void OnRegisterPanel() {
            base.OnRegisterPanel();
            Settings = App.Settings.LoadLightDocumentSettings(PanelKind);
        }

        private void OptionsPanel_PanelClosed(object sender, EventArgs e) {
            CloseOptionsPanel();
        }

        private void OptionsPanel_PanelReset(object sender, EventArgs e) {
            optionsPanel_.ResetSettings();
            LoadNewSettings(true);
        }

        public void ToggleOptionsPanelVisibility() {
            if (optionsPanelVisible_) {
                CloseOptionsPanel();
            }
            else {
                ShowOptionsPanel();
            }
        }

        protected virtual void ShowOptionsPanel() {
            if (optionsPanelVisible_) {
                return;
            }

            var width = Math.Max(LightDocumentOptionsPanel.MinimumWidth,
                    Math.Min(textView_.ActualWidth, LightDocumentOptionsPanel.DefaultWidth));
            var height = Math.Max(LightDocumentOptionsPanel.MinimumHeight,
                Math.Min(textView_.ActualHeight, LightDocumentOptionsPanel.DefaultHeight));
            var position = textView_.PointToScreen(new Point(textView_.ActualWidth - width, 0));
            optionsPanel_ = new OptionsPanelHostWindow(new LightDocumentOptionsPanel(),
                                                       position, width, height, this);

            optionsPanel_.Settings = Settings.Clone();
            optionsPanel_.PanelClosed += OptionsPanel_PanelClosed;
            optionsPanel_.PanelReset += OptionsPanel_PanelReset;
            optionsPanel_.SettingsChanged += OptionsPanel_SettingsChanged;
            optionsPanel_.IsOpen = true;
            optionsPanelVisible_ = true;
        }

        private void OptionsPanel_SettingsChanged(object sender, EventArgs e) {
            if (optionsPanelVisible_) {
                LoadNewSettings(false);
            }
        }

        protected virtual void CloseOptionsPanel() {
            if (!optionsPanelVisible_) {
                return;
            }

            LoadNewSettings(true);
            optionsPanel_.IsOpen = false;
            optionsPanel_.PanelClosed -= OptionsPanel_PanelClosed;
            optionsPanel_.PanelReset -= OptionsPanel_PanelReset;
            optionsPanelVisible_ = false;
            optionsPanel_ = null;
        }

        private bool LoadNewSettings(bool commit) {
            var newSettings = optionsPanel_.GetSettingsSnapshot<LightDocumentSettings>();

            if (newSettings.HasChanges(Settings)) {
                if (commit) {
                    App.Settings.SaveLightDocumentSettings(PanelKind, newSettings);
                    App.SaveApplicationSettings();
                }

                Settings = newSettings;
                return true;
            }

            return false;
        }
    }
}