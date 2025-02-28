// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Windows;
using System.Windows.Media;
using ClosedXML.Excel;
using HtmlAgilityPack;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Utilities;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace ProfileExplorer.UI;

public class IRDocumentColumnData {
  public IRDocumentColumnData(int capacity = 0) {
    Columns = new List<OptionalColumn>();
    Rows = new Dictionary<IRElement, ElementRowValue>(capacity);
    ColumnValues = new Dictionary<OptionalColumn, List<ElementColumnValue>>(new ColumnComparer());
  }

  public List<OptionalColumn> Columns { get; set; }
  public Dictionary<IRElement, ElementRowValue> Rows { get; set; }
  public Dictionary<OptionalColumn, List<ElementColumnValue>> ColumnValues { get; set; }
  public bool HasData => Rows.Count > 0;
  public OptionalColumn MainColumn => Columns.Find(column => column.IsMainColumn);

  public void ExportColumnsToExcel(IRElement tuple, IXLWorksheet ws,
                                   int rowId, int columnId) {
    foreach (var column in Columns) {
      var value = GetColumnValue(tuple, column);

      if (value != null) {
        ws.Cell(rowId, columnId).Value = value.Text.Replace(" ms", "");

        if (value.BackColor != null && value.BackColor is SolidColorBrush colorBrush) {
          var color = XLColor.FromArgb(colorBrush.Color.A, colorBrush.Color.R, colorBrush.Color.G,
                                       colorBrush.Color.B);
          ws.Cell(rowId, columnId).Style.Fill.BackgroundColor = color;

          if (column.IsMainColumn) {
            ws.Cell(rowId, 1).Style.Fill.BackgroundColor = color;
            ws.Cell(rowId, 2).Style.Fill.BackgroundColor = color;
          }
        }

        if (value.TextWeight != FontWeights.Normal) {
          ws.Cell(rowId, columnId).Style.Font.Bold = true;

          if (column.IsMainColumn) {
            ws.Cell(rowId, 1).Style.Font.Bold = true;
            ws.Cell(rowId, 2).Style.Font.Bold = true;
          }
        }
      }

      columnId++;
    }
  }

  public void ExportColumnsAsHTML(IRElement tuple, HtmlDocument doc, HtmlNode tr) {
    string CellStyle =
      @"text-align:left;vertical-align:top;word-wrap:break-word;max-width:500px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-size:14px;font-family:Arial, sans-serif;";

    foreach (var column in Columns) {
      var value = GetColumnValue(tuple, column);
      var td = doc.CreateElement("td");
      string style = CellStyle;

      if (value != null) {
        td.InnerHtml = HttpUtility.HtmlEncode(value.Text);

        if (value.BackColor is SolidColorBrush colorBrush) {
          style += $"background-color:{Utils.ColorToString(colorBrush.Color)};";
        }

        if (value.TextWeight != FontWeights.Normal) {
          style += $"font-weight:bold;";
        }
      }

      // Apply main column style for entire row.
      if (column.IsMainColumn && tr.ChildNodes.Count >= 2) {
        tr.ChildNodes[0].SetAttributeValue("style", style);
        tr.ChildNodes[1].SetAttributeValue("style", style);
      }

      td.SetAttributeValue("style", style);
      tr.AppendChild(td);
    }
  }

  public void ExportColumnsAsMarkdown(IRElement tuple, StringBuilder sb) {
    foreach (var column in Columns) {
      var value = GetColumnValue(tuple, column);
      string text = value != null ? value.Text : "";
      sb.Append($" {text} |");
    }
  }

  public OptionalColumn AddColumn(OptionalColumn column) {
    // Make a clone so that changing values such as the width
    // doesn't modify the column template.
    var columnClone = (OptionalColumn)column.Clone();

    if (!string.IsNullOrEmpty(column.Style.AlternateTitle)) {
      columnClone.Title = column.Style.AlternateTitle;
    }

    Columns.Add(columnClone);
    return columnClone;
  }

  public OptionalColumn GetColumn(OptionalColumn templateColumn) {
    return Columns.Find(column => column.ColumnName == templateColumn.ColumnName);
  }

  public ElementRowValue AddValue(ElementColumnValue value, IRElement element, OptionalColumn column) {
    if (!Rows.TryGetValue(element, out var rowValues)) {
      rowValues = new ElementRowValue(element);
      Rows[element] = rowValues;
    }

    rowValues.ColumnValues[column] = value;
    value.Element = element;
    AddColumnValue(value, column);
    return rowValues;
  }

  public void AddColumnValue(ElementColumnValue value, OptionalColumn column) {
    if (!ColumnValues.TryGetValue(column, out var list)) {
      list = new List<ElementColumnValue>();
      ColumnValues[column] = list;
    }

    list.Add(value);
  }

  public ElementRowValue GetValues(IRElement element) {
    if (Rows.TryGetValue(element, out var valueGroup)) {
      return valueGroup;
    }

    return null;
  }

  public void AddRow(ElementRowValue rowValues, IRElement element) {
    rowValues.Element = element;
    Rows[element] = rowValues;
  }

  public ElementColumnValue GetColumnValue(IRElement element, OptionalColumn column) {
    var values = GetValues(element);
    return values?[column];
  }

  public void Reset() {
  }

  public class ColumnComparer : IEqualityComparer<OptionalColumn> {
    public bool Equals(OptionalColumn x, OptionalColumn y) {
      if (ReferenceEquals(x, y))
        return true;
      if (ReferenceEquals(x, null))
        return false;
      if (ReferenceEquals(y, null))
        return false;
      if (x.GetType() != y.GetType())
        return false;
      return x.ColumnName == y.ColumnName;
    }

    public int GetHashCode(OptionalColumn obj) {
      return obj.ColumnName != null ? obj.ColumnName.GetHashCode() : 0;
    }
  }
}

// Represents a value in the row associated with an element.
// Can be viewed as a cell in a spreadsheet.
public sealed class ElementColumnValue : BindableObject {
  public static readonly ElementColumnValue Empty = new(string.Empty);
  private Thickness borderThickness_;
  private Brush borderBrush_;
  private string text_;
  private double minTextWidth_;
  private string toolTip_;
  private Brush textColor_;
  private Brush backColor_;
  private ImageSource icon_;
  private bool showPercentageBar_;
  private Brush percentageBarBackColor__;
  private double percentageBarBorderThickness_;
  private Brush percentageBarBorderBrush_;
  private FontWeight textWeight_;
  private double textSize_;
  private FontFamily textFont_;
  private bool canShowPercentageBar_;
  private double percentageBarMaxWidth_;
  private bool canShowBackgroundColor_;
  private bool canShowIcon_;

  public ElementColumnValue(string text, long value = 0, double valueValuePercentage = 0.0,
                            int valueOrder = int.MaxValue, string tooltip = null) {
    Text = text;
    Value = value;
    ValuePercentage = valueValuePercentage;
    ValueOrder = valueOrder;
    TextWeight = FontWeights.Normal;
    TextColor = Brushes.Black;
    ToolTip = tooltip;
    CanShowPercentageBar = true;
    CanShowBackgroundColor = true;
    CanShowIcon = true;
  }

  public IRElement Element { get; set; }
  public long Value { get; set; }
  public double ValuePercentage { get; set; }
  public int ValueOrder { get; set; }

  public Thickness BorderThickness {
    get => borderThickness_;
    set => SetAndNotify(ref borderThickness_, value);
  }

  public Brush BorderBrush {
    get => borderBrush_;
    set => SetAndNotify(ref borderBrush_, value);
  }

  public string Text {
    get => text_;
    set => SetAndNotify(ref text_, value);
  }

  public double MinTextWidth {
    get => minTextWidth_;
    set => SetAndNotify(ref minTextWidth_, value);
  }

  public string ToolTip {
    get => toolTip_;
    set => SetAndNotify(ref toolTip_, value);
  }

  public Brush TextColor {
    get => textColor_;
    set => SetAndNotify(ref textColor_, value);
  }

  public Brush BackColor {
    get => backColor_;
    set => SetAndNotify(ref backColor_, value);
  }

  public bool CanShowBackgroundColor {
    get => canShowBackgroundColor_;
    set => SetAndNotify(ref canShowBackgroundColor_, value);
  }

  public bool CanShowIcon {
    get => canShowIcon_;
    set => SetAndNotify(ref canShowIcon_, value);
  }

  public ImageSource Icon {
    get => icon_;
    set {
      SetAndNotify(ref icon_, value);
      Notify(nameof(ShowIcon));
    }
  }

  public bool ShowIcon => icon_ != null;

  public bool ShowPercentageBar {
    get => showPercentageBar_;
    set => SetAndNotify(ref showPercentageBar_, value);
  }

  public bool CanShowPercentageBar {
    get => canShowPercentageBar_;
    set => SetAndNotify(ref canShowPercentageBar_, value);
  }

  public Brush PercentageBarBackColor {
    get => percentageBarBackColor__;
    set => SetAndNotify(ref percentageBarBackColor__, value);
  }

  public double PercentageBarBorderThickness {
    get => percentageBarBorderThickness_;
    set => SetAndNotify(ref percentageBarBorderThickness_, value);
  }

  public Brush PercentageBarBorderBrush {
    get => percentageBarBorderBrush_;
    set => SetAndNotify(ref percentageBarBorderBrush_, value);
  }

  public double PercentageBarMaxWidth {
    get => percentageBarMaxWidth_;
    set => SetAndNotify(ref percentageBarMaxWidth_, value);
  }

  public FontWeight TextWeight {
    get => textWeight_;
    set => SetAndNotify(ref textWeight_, value);
  }

  public double TextSize {
    get => textSize_;
    set => SetAndNotify(ref textSize_, value);
  }

  public FontFamily TextFont {
    get => textFont_;
    set => SetAndNotify(ref textFont_, value);
  }
}

// Represents a set of values (by column) associated with an element.
// Can be view as a row with cells in a spreadsheet.
public sealed class ElementRowValue : BindableObject {
  private Brush backColor_;
  private Thickness borderThickness_;
  private Brush borderBrush_;

  public ElementRowValue(IRElement element) {
    Element = element;
    ColumnValues = new Dictionary<OptionalColumn, ElementColumnValue>();
  }

  public IRElement Element { get; set; }
  public Dictionary<OptionalColumn, ElementColumnValue> ColumnValues { get; set; }
  public ICollection<ElementColumnValue> Values => ColumnValues.Values;
  public ICollection<OptionalColumn> Columns => ColumnValues.Keys;
  public int Count => ColumnValues.Count;

  public Brush BackColor {
    get => backColor_;
    set => SetAndNotify(ref backColor_, value);
  }

  public Thickness BorderThickness {
    get => borderThickness_;
    set => SetAndNotify(ref borderThickness_, value);
  }

  public Brush BorderBrush {
    get => borderBrush_;
    set => SetAndNotify(ref borderBrush_, value);
  }

  public object Tag { get; set; }
  public ElementColumnValue this[OptionalColumn column] => ColumnValues.GetValueOrNull(column);

  public ElementColumnValue this[string columnName] {
    get {
      foreach (var pair in ColumnValues) {
        if (pair.Key.ColumnName == columnName) {
          return pair.Value;
        }
      }

      return null;
    }
  }
}