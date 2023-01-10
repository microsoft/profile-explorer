using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using IRExplorerCore.Utilities;

namespace IRExplorerUI;

// Based on https://stackoverflow.com/a/29123964
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

// Based on https://stackoverflow.com/a/3088387
public class GridViewColumnVisibility {
    private static Dictionary<ListView, List<(GridViewColumn Column, int Index)>> removedColumns_ = new();

    public static void UpdateListView(ListView lv) {
        GridView gridview = lv.View as GridView;
        if (gridview == null || gridview.Columns == null)
            return;

        removedColumns_ ??= new Dictionary<ListView, List<(GridViewColumn, int)>>();
        var columnList = removedColumns_.GetOrAddValue(lv, () => new List<(GridViewColumn Column, int Index)>());
        var addedColumns = new List<GridViewColumn>();

        // If some of the removed columns got re-enabled, insert them back.
        foreach (var removedColumn in columnList) {
            if (GetIsVisible(removedColumn.Column)) {
                int columnIndex = Math.Min(removedColumn.Index, gridview.Columns.Count);
                gridview.Columns.Insert(columnIndex, removedColumn.Column);
                addedColumns.Add(removedColumn.Column);
            }
        }

        // Discard columns that got re-enabled.
        foreach (var column in addedColumns) {
            var index = columnList.FindIndex(c => c.Column == column);
            if (index != -1) {
                columnList.RemoveAt(index);
            }
        }

        // Remove disabled columns and save them for later
        // in case they get re-enabled.
        List<GridViewColumn> toRemove = new List<GridViewColumn>();

        foreach (GridViewColumn gc in gridview.Columns) {
            if (GetIsVisible(gc) == false) {
                toRemove.Add(gc);
                columnList.Add((gc, toRemove.Count));
            }
        }
        
        foreach (GridViewColumn gc in toRemove) {
            gridview.Columns.Remove(gc);
        }
    }

    public static bool GetIsVisible(DependencyObject obj) {
        return (bool)obj.GetValue(IsVisibleProperty);
    }

    public static void SetIsVisible(DependencyObject obj, bool value) {
        obj.SetValue(IsVisibleProperty, value);
    }

    public static readonly DependencyProperty IsVisibleProperty =
        DependencyProperty.RegisterAttached("IsVisible", typeof(bool), typeof(GridViewColumnVisibility), new UIPropertyMetadata(true));


    public static bool GetEnabled(DependencyObject obj) {
        return (bool)obj.GetValue(EnabledProperty);
    }

    public static void SetEnabled(DependencyObject obj, bool value) {
        obj.SetValue(EnabledProperty, value);
    }

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(GridViewColumnVisibility), new UIPropertyMetadata(false,
            new PropertyChangedCallback(OnEnabledChanged)));

    private static void OnEnabledChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e) {
        ListView view = obj as ListView;
        if (view != null) {
            bool enabled = (bool)e.NewValue;
            if (enabled) {
                view.Loaded += (sender, e2) => {
                    UpdateListView((ListView)sender);
                };
                view.TargetUpdated += (sender, e2) => {
                    UpdateListView((ListView)sender);
                };
                view.DataContextChanged += (sender, e2) => {
                    UpdateListView((ListView)sender);
                };
            }
        }
    }
}
