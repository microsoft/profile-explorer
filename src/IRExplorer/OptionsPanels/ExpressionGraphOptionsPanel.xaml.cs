// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows;
using System.Windows.Controls;

namespace Client.Options {
    public partial class ExpressionGraphOptionsPanel : UserControl {
        public ExpressionGraphOptionsPanel() {
            InitializeComponent();
        }

        public event EventHandler PanelClosed;
        public event EventHandler PanelReset;

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            PanelClosed?.Invoke(this, new EventArgs());
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e) {
            PanelReset?.Invoke(this, new EventArgs());
        }
    }
}
