// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProfileExplorer.UI.Profile;
using ProfileExplorerCore.Profile.Data;

namespace ProfileExplorer.UI;

public delegate void OptionalColumnEventHandler(OptionalColumn column, GridViewColumnHeader columnHeader);

public class OptionalColumn : ICloneable {
  public OptionalColumnEventHandler HeaderClickHandler;
  public OptionalColumnEventHandler HeaderRightClickHandler;
  public OptionalColumnEventHandler HeaderDoubleClickHandler;
  private int hashCode_;

  private OptionalColumn(string bindingName, string cellTemplateName,
                         string columnName, string title, string tooltip,
                         IValueConverter converter = null,
                         double width = double.NaN,
                         string columnTemplateName = null,
                         OptionalColumnStyle style = null,
                         bool isVisible = true) {
    BindingName = bindingName;
    CellTemplateName = cellTemplateName;
    ColumnName = string.Intern(columnName);
    Title = title;
    Tooltip = tooltip;
    Width = width;
    Converter = converter;
    ColumnTemplateName = columnTemplateName;
    Style = style ?? new OptionalColumnStyle();
    IsVisible = isVisible;
    IsTemplateBinding = cellTemplateName != null;
  }

  public string BindingName { get; set; }
  public string CellTemplateName { get; set; }
  public string ColumnName { get; set; }
  public string ColumnTemplateName { get; set; }
  public string Title { get; set; }
  public string Tooltip { get; set; }
  public double Width { get; set; }
  public IValueConverter Converter { get; set; }
  public OptionalColumnStyle Style { get; set; }
  public PerformanceCounter PerformanceCounter { get; set; }
  public bool IsVisible { get; set; }
  public bool IsMainColumn { get; set; }
  public bool IsTemplateBinding { get; set; }
  public bool HasCustomStyle => !string.IsNullOrEmpty(ColumnTemplateName);
  public Style CustomStyle =>
    !string.IsNullOrEmpty(ColumnTemplateName) ? (Style)Application.Current.FindResource(ColumnTemplateName) : null;
  public bool IsPerformanceCounter => PerformanceCounter is {IsMetric: false};
  public bool IsPerformanceMetric => PerformanceCounter is {IsMetric: true};

  public object Clone() {
    var clone = new OptionalColumn(BindingName, CellTemplateName, ColumnName, Title, Tooltip,
                                   Converter, Width, ColumnTemplateName, Style, IsVisible) {
      HeaderClickHandler = HeaderClickHandler,
      HeaderRightClickHandler = HeaderRightClickHandler,
      HeaderDoubleClickHandler = HeaderDoubleClickHandler,
      PerformanceCounter = PerformanceCounter
    };

    return clone;
  }

  public static OptionalColumn Binding(string binding, string columnName, string title, string tooltip = null,
                                       IValueConverter converter = null, double width = double.NaN,
                                       string columnStyle = null,
                                       OptionalColumnStyle style = null, bool isVisible = true) {
    return new OptionalColumn(binding, null, columnName, title, tooltip,
                              converter, width, columnStyle, style, isVisible);
  }

  public static OptionalColumn Template(string binding, string templateName, string columnName, string title,
                                        string tooltip = null,
                                        IValueConverter converter = null, double width = double.NaN,
                                        string columnStyle = null,
                                        OptionalColumnStyle style = null, bool isVisible = true) {
    return new OptionalColumn(binding, templateName, columnName, title, tooltip,
                              converter, width, columnStyle, style, isVisible);
  }

  public static void RemoveListViewColumns(ListView listView, OptionalColumn[] columns,
                                           IGridViewColumnValueSorter columnSorter = null) {
    foreach (var column in columns) {
      RemoveListViewColumn(listView, column.ColumnName, columnSorter);
    }
  }

  public static void RemoveListViewColumns(ListView listView, Func<GridViewColumnHeader, bool> predicate = null,
                                           IGridViewColumnValueSorter columnSorter = null) {
    var functionGrid = (GridView)listView.View;
    var removedColumns = new List<GridViewColumn>();

    foreach (var column in functionGrid.Columns) {
      var header = column.Header as GridViewColumnHeader;

      if (predicate == null || predicate(header)) {
        removedColumns.Add(column);
      }
    }

    foreach (var column in removedColumns) {
      functionGrid.Columns.Remove(column);
      columnSorter?.UnregisterColumnHeader((GridViewColumnHeader)column.Header);
    }
  }

  public static List<(GridViewColumnHeader Header, GridViewColumn Column)> AddListViewColumns(
    ListView listView, IEnumerable<OptionalColumn> columns,
    IGridViewColumnValueSorter columnSorter = null,
    string titleSuffix = "", string tooltipSuffix = "", bool useValueConverter = true, int insertionIndex = -1) {
    var columnHeaders = new List<(GridViewColumnHeader, GridViewColumn)>();

    foreach (var column in columns) {
      if (column.IsVisible) {
        columnHeaders.Add(AddListViewColumn(listView, column, columnSorter, titleSuffix, tooltipSuffix,
                                            useValueConverter, insertionIndex));
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
    var columnHeader = new GridViewColumnHeader {
      Name = column.ColumnName,
      VerticalAlignment = VerticalAlignment.Stretch,
      HorizontalAlignment = HorizontalAlignment.Stretch,
      VerticalContentAlignment = VerticalAlignment.Stretch,
      HorizontalContentAlignment = HorizontalAlignment.Left,
      Padding = new Thickness(0, 0, 7, 0),
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
        CreateGridColumnBindingTemplate(column.BindingName, converter)
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

  private static DataTemplate CreateGridColumnBindingTemplate(string propertyName,
                                                              IValueConverter valueConverter = null) {
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
    var factory = new FrameworkElementFactory(typeof(ContentControl));
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
}