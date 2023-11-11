// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRExplorerUI.Scripting;
using IRExplorerCore.IR;

namespace IRExplorerUI.Query {
    public class IRElementDragDropSelection {
        public IRElement Element { get; set; }
    }

    public partial class InputQueryViewElement : UserControl {
        public InputQueryViewElement() {
            InitializeComponent();
        }

        private void Image_MouseMove(object sender, MouseEventArgs e) {
            var element = sender as FrameworkElement;

            if (e.LeftButton == MouseButtonState.Pressed) {
                var selection = new IRElementDragDropSelection();
                var data = new DataObject(typeof(IRElementDragDropSelection), selection);

                if (DragDrop.DoDragDrop(element, data, DragDropEffects.All) == DragDropEffects.All) {
                    ((QueryValue)element.DataContext).Value = selection.Element;
                }
            }
        }
    }
}
