using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

// Based on answer from https://stackoverflow.com/a/29123964
namespace IRExplorerUI {
    public static class ContextMenuLeftClickBehavior {
        public static bool GetIsLeftClickEnabled(DependencyObject obj) {
            return (bool)obj.GetValue(IsLeftClickEnabledProperty);
        }

        public static void SetIsLeftClickEnabled(DependencyObject obj, bool value) {
            obj.SetValue(IsLeftClickEnabledProperty, value);
        }

        public static readonly DependencyProperty IsLeftClickEnabledProperty = DependencyProperty.RegisterAttached(
            "IsLeftClickEnabled",
            typeof(bool),
            typeof(ContextMenuLeftClickBehavior),
            new UIPropertyMetadata(false, OnIsLeftClickEnabledChanged));

        private static void OnIsLeftClickEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) {
            var uiElement = sender as UIElement;

            if (uiElement != null) {
                bool IsEnabled = e.NewValue is bool && (bool)e.NewValue;

                if (IsEnabled) {
                    if (uiElement is ButtonBase)
                        ((ButtonBase)uiElement).Click += OnMouseLeftButtonUp;
                    else
                        uiElement.MouseLeftButtonUp += OnMouseLeftButtonUp;
                }
                else {
                    if (uiElement is ButtonBase)
                        ((ButtonBase)uiElement).Click -= OnMouseLeftButtonUp;
                    else
                        uiElement.MouseLeftButtonUp -= OnMouseLeftButtonUp;
                }
            }
        }

        private static void OnMouseLeftButtonUp(object sender, RoutedEventArgs e) {
            var fe = sender as FrameworkElement;
            if (fe != null) {
                // if we use binding in our context menu, then it's DataContext won't be set when we show the menu on left click
                // (it seems setting DataContext for ContextMenu is hardcoded in WPF when user right clicks on a control, although I'm not sure)
                // so we have to set up ContextMenu.DataContext manually here
                if (fe.ContextMenu.DataContext == null) {
                    fe.ContextMenu.SetBinding(FrameworkElement.DataContextProperty, new Binding { Source = fe.DataContext });
                }

                fe.ContextMenu.IsOpen = true;
            }
        }

    }

    public static class GridViewBehaviors {
        public static readonly DependencyProperty CollapseableColumnProperty =
            DependencyProperty.RegisterAttached("CollapseableColumn", typeof(bool), typeof(GridViewBehaviors),
                new UIPropertyMetadata(false, OnCollapseableColumnChanged));

        public static bool GetCollapseableColumn(DependencyObject d) {
            return (bool)d.GetValue(CollapseableColumnProperty);
        }

        public static void SetCollapseableColumn(DependencyObject d, bool value) {
            d.SetValue(CollapseableColumnProperty, value);
        }

        private static void OnCollapseableColumnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) {
            GridViewColumnHeader header = sender as GridViewColumnHeader;
            if (header == null)
                return;

            header.IsVisibleChanged += new DependencyPropertyChangedEventHandler(AdjustWidth);
        }

        static void AdjustWidth(object sender, DependencyPropertyChangedEventArgs e) {
            GridViewColumnHeader header = sender as GridViewColumnHeader;
            if (header == null)
                return;

            if (header.Visibility == Visibility.Collapsed)
                header.Column.Width = 0;
            else
                header.Column.Width = double.NaN;   // "Auto"
        }
    }
}
