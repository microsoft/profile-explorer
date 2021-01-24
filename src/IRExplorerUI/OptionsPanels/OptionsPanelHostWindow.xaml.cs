// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using IRExplorerUI.Controls;

namespace IRExplorerUI.OptionsPanels {
    public partial class OptionsPanelHostWindow : DraggablePopup, IOptionsPanel {
        private bool closed_;
        private IOptionsPanel optionsPanel_;

        public OptionsPanelHostWindow(UserControl panel, Point position,
                                      double width, double height,
                                      UIElement referenceElement,
                                      bool showResetButton = true) {
            InitializeComponent();
            
            // Offset to account for drop shadow margin.
            position.Offset(6, 0);
            Initialize(position, width, height, referenceElement);

            PanelResizeGrip.ResizedControl = this;
            ShowResetButton = showResetButton;
            DataContext = this;

            optionsPanel_ = (IOptionsPanel)panel;
            optionsPanel_.PanelClosed += SettingsPanel_PanelClosed;
            optionsPanel_.PanelReset += SettingsPanel_PanelReset;
            optionsPanel_.SettingsChanged += SettingsPanel_SettingsChanged;
            optionsPanel_.StayOpenChanged += OptionsPanel_StayOpenChanged;
            PanelHost.Content = panel;
        }

        public bool ShowResetButton { get; set; }

        protected override void OnOpened(EventArgs e) {
            base.OnOpened(e);
            optionsPanel_.Initialize(this);
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);

            optionsPanel_.PanelClosed -= SettingsPanel_PanelClosed;
            optionsPanel_.PanelReset -= SettingsPanel_PanelReset;
            optionsPanel_.SettingsChanged -= SettingsPanel_SettingsChanged;

            if (!closed_) {
                closed_ = true;
                PanelClosed?.Invoke(this, e);
            }
        }

        public void Initialize(FrameworkElement parent) {

        }

        private void SettingsPanel_SettingsChanged(object sender, bool force) {
            if (Settings == null) {
                return; // Ignore event triggered during GetSettingsSnapshot.
            }

            SettingsChanged?.Invoke(this, force);
        }

        private void SettingsPanel_PanelReset(object sender, EventArgs e) {
            PanelReset?.Invoke(this, e);
        }

        private void SettingsPanel_PanelClosed(object sender, EventArgs e) {
            closed_ = true;
            PanelClosed?.Invoke(this, e);
        }

        private void OptionsPanel_StayOpenChanged(object sender, bool staysOpen) {
            StaysOpen = staysOpen;
        }

        public SettingsBase Settings {
            get => optionsPanel_.Settings;
            set => optionsPanel_.Settings = value != null ? value.Clone() : null;
        }
        
        public T GetSettingsSnapshot<T>() where T: SettingsBase {
            var newSettings = (T)Settings.Clone();
            Settings = null;
            Settings = newSettings;
            return newSettings;
        }

        public void ResetSettings() {
            var newSettings = Settings.Clone();
            newSettings.Reset();
            Settings = null;
            Settings = newSettings;
        }

        public event EventHandler PanelClosed;
        public event EventHandler PanelReset;
        public event EventHandler<bool> SettingsChanged;
        public event EventHandler<bool> StayOpenChanged;

        public void PanelClosing() { }
        public void PanelResetting() { }
        public void PanelResetted() { }

        private void ResetButton_Click(object sender, RoutedEventArgs e) {
            using var centerForm = new DialogCenteringHelper(this);

            if (MessageBox.Show("Do you want to reset all settings?", "IR Explorer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
                return;
            }

            optionsPanel_.PanelResetting();
            PanelReset?.Invoke(this, e);
            optionsPanel_.PanelResetted();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            closed_ = true;
            optionsPanel_.PanelClosing();
            PanelClosed?.Invoke(this, e);
        }
    }
}
