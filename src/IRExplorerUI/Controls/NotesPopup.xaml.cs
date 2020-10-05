﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IRExplorerUI.Controls {
    /// <summary>
    /// Interaction logic for NotesPopup.xaml
    /// </summary>
    public partial class NotesPopup : DraggablePopup {
        public NotesPopup(Point position, double width, double height,
                          UIElement referenceElement) {
            InitializeComponent();
            Initialize(position, width, height, referenceElement);
            PanelResizeGrip.ResizedControl = this;
        }

        public void SetText(string text) {
            TextView.Text = text;
            TextView.EnableIRSyntaxHighlighting();
        }
    }
}
