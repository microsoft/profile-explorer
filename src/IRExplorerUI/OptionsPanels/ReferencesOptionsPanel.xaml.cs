// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace IRExplorerUI.OptionsPanels {
    public partial class ReferencesOptionsPanel : OptionsPanelBase {
        public const double DefaultHeight = 260;
        public const double MinimumHeight = 200;
        public const double DefaultWidth = 360;
        public const double MinimumWidth = 360;

        private ReferenceSettings settings_;

        public ReferencesOptionsPanel() {
            InitializeComponent();
            PreviewMouseUp += DocumentOptionsPanel_PreviewMouseUp;
            PreviewKeyUp += DocumentOptionsPanel_PreviewKeyUp;
        }

        public override void Initialize(FrameworkElement parent) {
            base.Initialize(parent);
            settings_ = (ReferenceSettings)Settings;
        }

        public override void OnSettingsChanged(SettingsBase newSettings) {
            settings_ = (ReferenceSettings)newSettings;
        }

        private void DocumentOptionsPanel_PreviewKeyUp(object sender, KeyEventArgs e) {
            NotifySettingsChanged();
        }

        private void DocumentOptionsPanel_PreviewMouseUp(object sender, MouseButtonEventArgs e) {
            NotifySettingsChanged();
        }

        private void NotifySettingsChanged(bool force = false) {
            DelayedAction.StartNew(TimeSpan.FromMilliseconds(100), () => {
                RaiseSettingsChanged(force);
            });
        }

        public override void PanelClosing() {
            
        }

        public override void PanelResetting() {
        }
    }
}
