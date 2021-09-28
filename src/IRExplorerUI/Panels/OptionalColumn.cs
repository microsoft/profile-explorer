using System;
using System.Collections;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Generic;

namespace IRExplorerUI {
    public class OptionalColumn {
        private OptionalColumn(string bindingName, string columnName, string title, string tooltip,
            IValueConverter converter = null, double width = Double.NaN, bool isVisible = true) {
            BindingName = bindingName;
            ColumnName = columnName;
            Title = title;
            Tooltip = tooltip; 
            Width = width;
            IsVisible = isVisible;
            Converter = converter;
        }

        public static OptionalColumn Binding(string binding, string columnName, string title, string tooltip = null,
            IValueConverter converter = null, double width = Double.NaN, bool isVisible = true) {
            return new OptionalColumn(binding, columnName, title, tooltip, converter, width, isVisible);
        }

        public static OptionalColumn Template(string binding, string templateName, string columnName, string title, string tooltip = null,
            IValueConverter converter = null, double width = Double.NaN, bool isVisible = true) {
            return new OptionalColumn(binding, columnName, title, tooltip, converter, width, isVisible) {
                IsTemplateBinding = true, 
                TemplateName = templateName
            };
        }

        public bool IsTemplateBinding { get; set; }
        public string BindingName { get; set; }
        public string TemplateName { get; set; }
        public string ColumnName { get; set; }
        public string Title { get; set; }
        public string Tooltip { get; set; }
        public double Width { get; set; }
        public bool IsVisible { get; set; }
        public IValueConverter Converter { get; set; }

        public static void RemoveListViewColumns(ListView listView,  OptionalColumn[] columns,
            IGridViewColumnValueSorter columnSorter = null) {
            foreach (var column in columns) {
                RemoveListViewColumn(listView, column.ColumnName);
            }
        }

        public static void RemoveListViewColumns(ListView listView, IGridViewColumnValueSorter columnSorter = null) {
            var functionGrid = (GridView)listView.View;

            while(functionGrid.Columns.Count > 0) {
                var column = functionGrid.Columns[0];

                if (column.Header is GridViewColumnHeader header) {
                    columnSorter?.UnregisterColumnHeader(header);
                }

                functionGrid.Columns.Remove(column);
            }
        }

        public static void AddListViewColumns(ListView listView, IEnumerable<OptionalColumn> columns,
            IGridViewColumnValueSorter columnSorter = null,
            string titleSuffix = "", string tooltipSuffix = "", bool useValueConverter = true) {
            foreach (var column in columns) {
                if (column.IsVisible) {
                    AddListViewColumn(listView, column, columnSorter, titleSuffix, tooltipSuffix, useValueConverter);
                }
            }
        }
        
        public static GridViewColumnHeader RemoveListViewColumn(ListView listView, string columnName,
                                                                IGridViewColumnValueSorter columnSorter = null) {
            var functionGrid = (GridView)listView.View;

            foreach (var column in functionGrid.Columns) {
                if (column.Header is GridViewColumnHeader header) {
                    if (header.Name == columnName) {
                        functionGrid.Columns.Remove(column);
                        columnSorter?.UnregisterColumnHeader(header);
                        return header;
                    }
                }
            }

            return null;
        }
        
        public static GridViewColumnHeader AddListViewColumn(ListView listView, OptionalColumn column, 
                                                             IGridViewColumnValueSorter columnSorter = null,
                                                             string titleSuffix = "", string tooltipSuffix = "",
                                                             bool useValueConverter = true, int index = -1) {
            var functionGrid = (GridView)listView.View;
            var columnHeader = new GridViewColumnHeader() {
                Name = column.ColumnName,
                VerticalAlignment = VerticalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                Width = column.Width,
                Content = string.Format(column.Title, titleSuffix),
                ToolTip = string.Format(column.Tooltip, tooltipSuffix)
            };

            var converter = useValueConverter ? column.Converter : null;
            var gridColumn = new GridViewColumn {
                Header = columnHeader,
                Width = column.Width,
                CellTemplate = column.IsTemplateBinding ?
                               CreateGridColumnTemplateBindingTemplate(column.BindingName, column.TemplateName) :
                               CreateGridColumnBindingTemplate(column.BindingName, converter),
                HeaderContainerStyle = (Style)Application.Current.FindResource("ListViewHeaderStyle")
            };

            if (index != -1) {
                functionGrid.Columns.Insert(index, gridColumn);
            }
            else {
                functionGrid.Columns.Add(gridColumn);
            }

            columnSorter?.RegisterColumnHeader(columnHeader);
            return columnHeader;
        }

        private static DataTemplate CreateGridColumnBindingTemplate(string propertyName, IValueConverter valueConverter = null) {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Left);

            var binding = new Binding(propertyName);
            binding.Converter = valueConverter;
            factory.SetBinding(TextBlock.TextProperty, binding);
            template.VisualTree = factory;
            return template;
        }

        private static DataTemplate CreateGridColumnTemplateBindingTemplate(string propertyName, string sourceTampleteName) {
            //? Preloading XAML is faster
            //? https://stackoverflow.com/questions/24620656/how-does-use-xamlreader-to-load-from-a-xaml-file-from-within-the-assembly/24623673

            var template = new DataTemplate();
            var sourceTemplate = (DataTemplate)Application.Current.FindResource(sourceTampleteName);
            FrameworkElementFactory factory = new FrameworkElementFactory(typeof(ContentControl));
            var binding = new Binding(propertyName);
            factory.SetBinding(ContentControl.ContentProperty, binding);
            factory.SetValue(ContentControl.ContentTemplateProperty, sourceTemplate);
            template.VisualTree = factory;
            return template;
        }
    }
}