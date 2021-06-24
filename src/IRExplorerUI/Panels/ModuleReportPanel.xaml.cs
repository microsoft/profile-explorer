// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRExplorerUI.Document;
using IRExplorerCore;
using ProtoBuf;

namespace IRExplorerUI {
    public partial class ModuleReportPanel : ToolPanelControl {
        public ModuleReportPanel() {
            InitializeComponent();
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Other;

        #endregion
    }
}
