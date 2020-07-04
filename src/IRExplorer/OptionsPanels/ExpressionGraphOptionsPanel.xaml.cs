// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using IRExplorer.OptionsPanels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace IRExplorer.OptionsPanels {
    public partial class ExpressionGraphOptionsPanel : OptionsPanelBase {
        public const double DefaultHeight = 500;
        public const double MinimumHeight = 300;
        public const double DefaultWidth = 320;
        public const double MinimumWidth = 320;

        public ExpressionGraphOptionsPanel() {
            InitializeComponent();
        }
    }
}
