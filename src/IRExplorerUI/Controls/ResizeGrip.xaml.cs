using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace IRExplorerUI.Controls {
    /// <summary>
    /// Interaction logic for ResizeGrip.xaml
    /// </summary>
    public partial class ResizeGrip : UserControl {
        private Cursor cursor_;
        private FrameworkElement control_;

        public FrameworkElement ResizedControl { get => control_; set => control_ = value; }

        public ResizeGrip() {
            InitializeComponent();
        }

        private void OnResizeThumbDragStarted(object sender, DragStartedEventArgs e) {
            cursor_ = control_.Cursor;
            Cursor = Cursors.SizeNWSE;
        }

        private void OnResizeThumbDragCompleted(object sender, DragCompletedEventArgs e) {
            Cursor = cursor_;
        }

        private void OnResizeThumbDragDelta(object sender, DragDeltaEventArgs e) {
            double yAdjust = control_.Height + e.VerticalChange;
            double xAdjust = control_.Width + e.HorizontalChange;

            xAdjust = (control_.ActualWidth + xAdjust) > control_.MinWidth ? xAdjust : control_.MinWidth;
            yAdjust = (control_.ActualHeight + yAdjust) > control_.MinHeight ? yAdjust : control_.MinHeight;

            control_.Width = xAdjust;
            control_.Height = yAdjust;
        }
    }
}
