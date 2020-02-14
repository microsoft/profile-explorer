// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows.Controls;

namespace Client.Options {
    public partial class GraphOptionsPanel : UserControl {
        public GraphOptionsPanel() {
            InitializeComponent();
        }

        public event EventHandler PanelClosed;
        public event EventHandler PanelReset;

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            PanelClosed?.Invoke(this, new EventArgs());
        }

        private void ResetButton_Click(object sender, System.Windows.RoutedEventArgs e) {
            PanelReset?.Invoke(this, new EventArgs());
        }
    }
}
