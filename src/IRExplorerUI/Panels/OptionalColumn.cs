using System;
using System.Collections;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Generic;
using System.Windows.Forms;
using Application = System.Windows.Application;
using Binding = System.Windows.Data.Binding;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListView = System.Windows.Controls.ListView;

namespace IRExplorerUI {
    public class OptionalColumnAppearance {
        public bool ShowPercentageBar { get; set; }
        public bool ShowMainColumnPercentageBar { get; set; }
        public Brush PercentageBarBackColor { get; set; }
        public Brush TextColor { get; set; }

        public bool ShowIcon { get; set; }
        public bool ShowMainColumnIcon { get; set; }

        public bool PickColorForPercentage { get; set; }

        public bool UseBackColor { get; set; }
        public bool UseMainColumnBackColor { get; set; }

        public ColorPalette BackColorPalette { get; set; }
        public bool InvertColorPalette { get; set; }
    }

    public delegate void OptionalColumnEventHandler(OptionalColumn column);

    public class OptionalColumn : ICloneable {
        private OptionalColumn(string bindingName, string cellTemplateName,
                               string columnName, string title, string tooltip,
                               IValueConverter converter = null,
                               double width = Double.NaN, 
                               string columnStyle = null,
                               OptionalColumnAppearance appearance = null,
                               bool isVisible = true) {
            BindingName = bindingName;
            CellTemplateName = cellTemplateName;
            ColumnName = String.Intern(columnName);
            Title = title;
            Tooltip = tooltip; 
            Width = width;
            Converter = converter;
            ColumnStyle = columnStyle;
            Appearance = appearance ?? new OptionalColumnAppearance();
            IsVisible = isVisible;
            IsTemplateBinding = cellTemplateName != null;
        }

        public static OptionalColumn Binding(string binding, string columnName, string title, string tooltip = null,
            IValueConverter converter = null, double width = Double.NaN, string columnStyle = null, 
            OptionalColumnAppearance appearance = null, bool isVisible = true) {
            return new OptionalColumn(binding, null, columnName, title, tooltip, 
                                      converter, width, columnStyle, appearance, isVisible);
        }

        public static OptionalColumn Template(string binding, string templateName, string columnName, string title, string tooltip = null,
            IValueConverter converter = null, double width = Double.NaN, string columnStyle = null,
            OptionalColumnAppearance appearance = null, bool isVisible = true) {
            return new OptionalColumn(binding, templateName, columnName, title, tooltip, 
                                      converter, width, columnStyle, appearance, isVisible);
        }

        public string BindingName { get; set; }
        public string CellTemplateName { get; set; }
        public string ColumnName { get; set; }
        public string ColumnStyle { get; set; }
        public string Title { get; set; }
        public string Tooltip { get; set; }
        public double Width { get; set; }
        public IValueConverter Converter { get; set; }
        public OptionalColumnAppearance Appearance { get; set; }
        public bool IsVisible { get; set; }
        public bool IsMainColumn { get; set; }
        public bool IsTemplateBinding { get; set; }

        public OptionalColumnEventHandler HeaderClickHandler;
        public OptionalColumnEventHandler HeaderDoubleClickHandler;

        private int hashCode_;

        public bool HasCustomStyle => !string.IsNullOrEmpty(ColumnStyle);
        public Style CustomStyle => !string.IsNullOrEmpty(ColumnStyle) ? (Style)Application.Current.FindResource(ColumnStyle) : null;

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

        public static List<(GridViewColumnHeader Header, GridViewColumn Column)> AddListViewColumns(ListView listView, IEnumerable<OptionalColumn> columns,
            IGridViewColumnValueSorter columnSorter = null,
            string titleSuffix = "", string tooltipSuffix = "", bool useValueConverter = true, int insertionIndex = -1) {
            var columnHeaders = new List<(GridViewColumnHeader, GridViewColumn)>();

            foreach (var column in columns) {
                if (column.IsVisible) {
                    columnHeaders.Add(AddListViewColumn(listView, column, columnSorter, titleSuffix, tooltipSuffix, useValueConverter, insertionIndex));
                    insertionIndex = insertionIndex != -1 ? insertionIndex + 1 : -1;
                }
            }

            return columnHeaders;
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
        
        public static (GridViewColumnHeader Header, GridViewColumn Column)
            AddListViewColumn(ListView listView, OptionalColumn column, 
                                                             IGridViewColumnValueSorter columnSorter = null,
                                                             string titleSuffix = "", string tooltipSuffix = "",
                                                             bool useValueConverter = true, int index = -1) {
            var functionGrid = (GridView)listView.View;
            var columnHeader = new GridViewColumnHeader() {
                Name = column.ColumnName,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0,0,7,0),
                Width = column.Width,
                Content = string.Format(column.Title, titleSuffix),
                ToolTip = string.Format(column.Tooltip, tooltipSuffix),
                OverridesDefaultStyle = column.HasCustomStyle,
                Style = column.CustomStyle,
                Tag = column
            };

            var converter = useValueConverter ? column.Converter : null;
            var gridColumn = new GridViewColumn {
                Header = columnHeader,
                Width = column.Width,
                CellTemplate = column.IsTemplateBinding ?
                               CreateGridColumnTemplateBindingTemplate(column.BindingName, column.CellTemplateName) :
                               CreateGridColumnBindingTemplate(column.BindingName, converter),
                //HeaderContainerStyle = (Style)Application.Current.FindResource("ListViewHeaderStyle")
            };

            if (index != -1) {
                functionGrid.Columns.Insert(index, gridColumn);
            }
            else {
                functionGrid.Columns.Add(gridColumn);
            }

            columnSorter?.RegisterColumnHeader(columnHeader);
            return (columnHeader, gridColumn);
        }

        public static int FindListViewColumnIndex(string name, ListView listView) {
            var functionGrid = (GridView)listView.View;
            int index = 0;

            foreach (var column in functionGrid.Columns) {
                if (column.Header is GridViewColumnHeader columnHeader &&
                    columnHeader.Name == name) {
                    return index;
                }

                index++;
            }

            return -1;
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

        public override bool Equals(object? obj) {
            return obj is OptionalColumn other &&
                   other.IsTemplateBinding == IsTemplateBinding &&
                   other.ColumnName.Equals(ColumnName);
        }

        public override int GetHashCode() {
            if (hashCode_ != 0) {
                return hashCode_;
            }

            hashCode_ = HashCode.Combine(ColumnName, IsTemplateBinding);
            return hashCode_;
        }

        public object Clone() {
            var clone = new OptionalColumn(BindingName, CellTemplateName, ColumnName, Title, Tooltip,
                                      Converter, Width, ColumnStyle, Appearance, IsVisible) {
                HeaderClickHandler = HeaderClickHandler,
                HeaderDoubleClickHandler = HeaderDoubleClickHandler,
            };

            return clone;
        }
    }
}