// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;

namespace IRExplorer {
    /// <summary>
    ///     Interaction logic for OptionsWindow.xaml
    /// </summary>
    public partial class OptionsWindow : Window {
        public OptionsWindow() {
            InitializeComponent();
            DocumentOptionsPanel.Settings = App.Settings.DocumentSettings;
            GraphOptionsPanel.Settings = App.Settings.FlowGraphSettings;
            ExpressionGraphOptionsPanel.Settings = App.Settings.ExpressionGraphSettings;
            DiffOptionsPanel.Settings = App.Settings.DiffSettings;

        }
    }
}
