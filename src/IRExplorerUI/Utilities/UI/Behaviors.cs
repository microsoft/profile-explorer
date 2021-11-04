using System.Collections.Generic;
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

    public class GridViewColumnVisibility {
        static Dictionary<GridViewColumn, double> originalColumnWidths_ = new Dictionary<GridViewColumn, double>();

        public static bool GetIsVisible(DependencyObject obj) {
            return (bool)obj.GetValue(IsVisibleProperty);
        }

        public static void SetIsVisible(DependencyObject obj, bool value) {
            obj.SetValue(IsVisibleProperty, value);
        }

        public static readonly DependencyProperty IsVisibleProperty =
            DependencyProperty.RegisterAttached("IsVisible", typeof(bool), 
                typeof(GridViewColumnVisibility), new UIPropertyMetadata(true, OnIsVisibleChanged));

        private static void OnIsVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            GridViewColumn column = d as GridViewColumn;

            if (column == null) {
                return;
            }

            if (GetIsVisible(column) == false) {
                originalColumnWidths_[column] = column.Width;
                column.Width = 0;
            }
            else if (column.Width == 0) {
                column.Width = originalColumnWidths_[column];
            }
            else {
                column.Width = double.NaN; // Auto
            }
        }
    }
}
