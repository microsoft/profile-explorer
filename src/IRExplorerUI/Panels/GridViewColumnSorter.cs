// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace IRExplorerUI {
    class GridViewColumnSorter<T> where T : Enum {
        public delegate int ValueCompareDelegate(object x, object y, T field, ListSortDirection direction);
        public delegate T ColumnFieldMappingDelegate(string columnName);

        class ValueComparer : IComparer {
            private ValueCompareDelegate compareFunc_;
            private ListSortDirection direction_;
            private T sortingField_;

            public ValueComparer(T sortingField, ListSortDirection direction,
                ValueCompareDelegate compareFunc) {
                sortingField_ = sortingField;
                direction_ = direction;
                compareFunc_ = compareFunc;
            }

            public int Compare(object x, object y) {
                return compareFunc_(x, y, sortingField_, direction_);
            }
        }

        private ColumnFieldMappingDelegate fieldMapping_;
        private ValueCompareDelegate valueComparer_;
        private SortAdorner sortAdorner_;
        private GridViewColumnHeader sortColumn_;
        private ListView listView_;

        public GridViewColumnSorter(ListView listView, ColumnFieldMappingDelegate fieldMapping,
            ValueCompareDelegate valueComparer) {
            listView_ = listView;
            fieldMapping_ = fieldMapping;
            valueComparer_ = valueComparer;

            var gridView = listView.View as GridView;
            Debug.Assert(gridView != null);

            foreach (var column in gridView.Columns) {
                if (column.Header is GridViewColumnHeader header) {
                    header.Click += ColumnHeader_Click;
                }
            }
        }

        void ColumnHeader_Click(object sender, RoutedEventArgs e) {
            if (sortColumn_ != null) {
                AdornerLayer.GetAdornerLayer(sortColumn_)?.Remove(sortAdorner_);
            }

            var sortingDirection = ListSortDirection.Ascending;
            var column = sender as GridViewColumnHeader;
            Debug.Assert(column != null);

            if (sortColumn_ == column && sortAdorner_.Direction == sortingDirection) {
                sortingDirection = ListSortDirection.Descending;
            }

            sortColumn_ = column;
            sortAdorner_ = new SortAdorner(sortColumn_, sortingDirection);
            AdornerLayer.GetAdornerLayer(sortColumn_)?.Add(sortAdorner_);

            if (!(listView_.ItemsSource is ListCollectionView view)) {
                return; // No function selected yet.
            }

            T sortField = fieldMapping_(column.Name);
            view.CustomSort = new ValueComparer(sortField, sortingDirection, valueComparer_);
            listView_.Items.Refresh();
        }
    }
}