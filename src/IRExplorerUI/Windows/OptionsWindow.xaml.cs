// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Windows;

namespace IRExplorerUI {
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
