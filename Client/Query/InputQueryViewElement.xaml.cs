using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Client.Scripting;
using Core.IR;

namespace Client.Query
{
    public class IRElementDragDropSelection
    {
        public IRElement Element { get; set; }
    }

    public partial class InputQueryViewElement : UserControl
    {
        public InputQueryViewElement()
        {
            InitializeComponent();
        }

        private void Image_MouseMove(object sender, MouseEventArgs e)
        {
            var element = sender as FrameworkElement;
            if(e.LeftButton == MouseButtonState.Pressed)
            {
                var selection = new IRElementDragDropSelection();
                var data = new DataObject(typeof(IRElementDragDropSelection), selection);
                
                if(DragDrop.DoDragDrop(element, data, DragDropEffects.All) == DragDropEffects.All)
                {
                    ((QueryValue)element.DataContext).Value = selection.Element;
                }
            }
        }
    }
}
