using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRExplorer.Scripting;
using IRExplorerCore.IR;

namespace IRExplorer.Query {
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
