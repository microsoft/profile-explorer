// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ClosedXML.Excel;
using IRExplorerCore.IR;
using IRExplorerCore.Utilities;

namespace IRExplorerUI;

public class IRDocumentColumnData {
  public IRDocumentColumnData(int capacity = 0) {
    Columns = new List<OptionalColumn>();
    Values = new Dictionary<IRElement, ElementRowValue>(capacity);
  }

  public List<OptionalColumn> Columns { get; set; }
  public Dictionary<IRElement, ElementRowValue> Values { get; set; }
  public bool HasData => Values.Count > 0;
  public OptionalColumn MainColumn => Columns.Find(column => column.IsMainColumn);

  public static void ExportColumnsToExcel(IRDocumentColumnData columnData, IRElement tuple,
                                          IXLWorksheet ws, int rowId, int columnId) {
    foreach (var column in columnData.Columns) {
      var value = columnData.GetColumnValue(tuple, column);

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

  public OptionalColumn AddColumn(OptionalColumn column) {
    // Make a clone so that changing values such as the width
    // doesn't modify the column template.
    column = (OptionalColumn)column.Clone();
    Columns.Add(column);
    return column;
  }

  public ElementRowValue AddValue(ElementColumnValue value, IRElement element, OptionalColumn column) {
    if (!Values.TryGetValue(element, out var valueGroup)) {
      valueGroup = new ElementRowValue(element);
      Values[element] = valueGroup;
    }

    valueGroup.ColumnValues[column] = value;
    value.Element = element;
    return valueGroup;
  }

  public ElementRowValue GetValues(IRElement element) {
    if (Values.TryGetValue(element, out var valueGroup)) {
      return valueGroup;
    }

    return null;
  }

  public ElementColumnValue GetColumnValue(IRElement element, OptionalColumn column) {
    var values = GetValues(element);
    return values?[column];
  }

  public void Reset() {
  }
}

// Represents a value in the row associated with an element.
// Can be viewed as a cell in a spreadsheet.
public sealed class ElementColumnValue : BindableObject {
  public static readonly ElementColumnValue Empty = new ElementColumnValue(string.Empty);
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

  public ElementColumnValue(string text, long value = 0, double valueValuePercentage = 0.0,
                            int valueOrder = int.MaxValue, string tooltip = null) {
    Text = text;
    Value = value;
    ValuePercentage = valueValuePercentage;
    ValueOrder = valueOrder;
    TextWeight = FontWeights.Normal;
    TextColor = Brushes.Black;
    ToolTip = tooltip;
    ShowPercentageBar = true;
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
  public int Index { get; set; }
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