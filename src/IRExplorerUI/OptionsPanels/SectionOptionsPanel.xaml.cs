﻿using System;
using System.Windows;
using System.Windows.Input;

namespace IRExplorerUI.OptionsPanels {
    public partial class SectionOptionsPanel : OptionsPanelBase {
        public const double DefaultHeight = 280;
        public const double MinimumHeight = 280;
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
            var settingsPath = App.GetSectionsDefinitionFilePath("utc");
            App.LaunchSettingsFileEditor(settingsPath);
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e) {
            compilerInfo_.SectionStyleProvider.LoadSettings();
        }
    }
}