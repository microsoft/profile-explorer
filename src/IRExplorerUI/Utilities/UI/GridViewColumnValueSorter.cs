// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace IRExplorerUI {
    class GridViewColumnValueSorter<T> where T : Enum {
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

        private Dictionary<T, GridViewColumnHeader> fieldColumnMap_;
        private ColumnFieldMappingDelegate fieldMapping_;
        private ValueCompareDelegate valueComparer_;
        private SortAdorner sortAdorner_;
        private GridViewColumnHeader sortColumn_;
        private ListView listView_;

        public GridViewColumnValueSorter(ListView listView, ColumnFieldMappingDelegate fieldMapping,
            ValueCompareDelegate valueComparer) {
            listView_ = listView;
            fieldMapping_ = fieldMapping;
            valueComparer_ = valueComparer;
            fieldColumnMap_ = new Dictionary<T, GridViewColumnHeader>();

            var gridView = listView.View as GridView;
            Debug.Assert(gridView != null);

            foreach (var column in gridView.Columns) {
                if (column.Header is GridViewColumnHeader header) {
                    header.Click += ColumnHeader_Click;

                    // If there is no name, the mapping is checked later
                    // since it may be an optional column.
                    if (!string.IsNullOrEmpty(header.Name)) {
                        T field = fieldMapping_(header.Name);
                        fieldColumnMap_[field] = header;
                    }
                }
            }
        }

        public void SortByField(T field, ListSortDirection direction = ListSortDirection.Ascending) {
            if (!fieldColumnMap_.TryGetValue(field, out var column)) {
                // Field may be associated with a column added later.
                var gridView = listView_.View as GridView;

                foreach (var gridColumn in gridView.Columns) {
                    if (gridColumn.Header is GridViewColumnHeader header &&
                        !string.IsNullOrEmpty(header.Name)) {
                        if(fieldMapping_(header.Name).Equals(field)) {
                            column = header;
                            break;
                        }
                    }
                }
            }

            SortColumn(column, direction, field);
        }

        void ColumnHeader_Click(object sender, RoutedEventArgs e) {
            if (sortColumn_ != null) {
                AdornerLayer.GetAdornerLayer(sortColumn_)?.Remove(sortAdorner_);
            }

            var sortingDirection = ListSortDirection.Ascending;
            var header = sender as GridViewColumnHeader;
            Debug.Assert(header != null);
            Debug.Assert(!string.IsNullOrEmpty(header.Name));

            // Invert direction if the same column is clicked.
            if (sortColumn_ == header && sortAdorner_.Direction == sortingDirection) {
                sortingDirection = ListSortDirection.Descending;
            }

            // Map from the column name to the enum value.
            T sortField = fieldMapping_(header.Name);
            SortColumn(header, sortingDirection, sortField);
        }

        private void SortColumn(GridViewColumnHeader column, 
                                ListSortDirection sortingDirection, T sortField) {
            sortColumn_ = column;
            sortAdorner_ = new SortAdorner(sortColumn_, sortingDirection);
            AdornerLayer.GetAdornerLayer(sortColumn_)?.Add(sortAdorner_);

            if (!(listView_.ItemsSource is ListCollectionView view)) {
                return;
            }

            view.CustomSort = new ValueComparer(sortField, sortingDirection, valueComparer_);
            listView_.Items.Refresh();
        }
    }
}