﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using IRExplorerCore.IR;

namespace IRExplorerUI.Document;

public partial class DocumentColumns : UserControl, INotifyPropertyChanged {
  private DocumentSettings settings_;
  private double previousVerticalOffset_;
  private List<ElementRowValue> profileDataRows_;
  private List<(GridViewColumnHeader Header, GridViewColumn Column)> profileColumnHeaders_;
  private double columnsListItemHeight_;
  private Brush selectedLineBrush_;

  public DocumentColumns() {
    InitializeComponent();
    settings_ = App.Settings.DocumentSettings;
    profileColumnHeaders_ = new List<(GridViewColumnHeader Header, GridViewColumn Column)>();
    profileDataRows_ = new List<ElementRowValue>();
  }

  public event EventHandler<ScrollChangedEventArgs> ScrollChanged;
  public event PropertyChangedEventHandler PropertyChanged;

  public double ColumnsListItemHeight {
    get => columnsListItemHeight_;
    set {
      if (columnsListItemHeight_ != value) {
        columnsListItemHeight_ = value;
        OnPropertyChanged();
      }
    }
  }

  public Brush SelectedLineBrush {
    get => selectedLineBrush_;
    set {
      selectedLineBrush_ = value;
      OnPropertyChanged();
    }
  }

  public void SelectRow(int index) {
    if (index >= 0 && ColumnsList.Items.Count > index) {
      ColumnsList.SelectedIndex = index;
    }
  }

  public void Reset() {
    OptionalColumn.RemoveListViewColumns(ColumnsList);
  }

  public void Display(IRDocumentColumnData columnData, int rowCount,
                      FunctionIR function) {
    Reset();
    ColumnsList.ItemsSource = null;

    if (columnData.HasData) {
      var oddBackColor = settings_.AlternateBackgroundColor.AsBrush();
      var blockSeparatorColor = settings_.ShowBlockSeparatorLine ? settings_.BlockSeparatorColor.AsBrush() : null;
      var font = new FontFamily(settings_.FontName);
      double fontSize = settings_.FontSize;

      ElementColumnValue MakeDummyCell() {
        var columnValue = ElementColumnValue.Empty;

        //? if (showColumnSeparators) {
        columnValue.BorderBrush = blockSeparatorColor;
        columnValue.BorderThickness = new Thickness(0, 0, 1, 0);
        columnValue.TextFont = font;
        columnValue.TextSize = fontSize;
        return columnValue;
      }

      ElementRowValue MakeDummyRow(Brush backColor = null) {
        var row = new ElementRowValue(null) {
          BackColor = backColor,
          BorderBrush = blockSeparatorColor
        };

        foreach (var column in columnData.Columns) {
          row.ColumnValues[column] = MakeDummyCell();
        }

        return row;
      }

      var elementValueList = new List<ElementRowValue>(function.TupleCount);
      var dummyValues = MakeDummyRow();
      var oddDummyValues = MakeDummyRow(oddBackColor);

      int prevLine = -1;
      bool prevIsOddBlock = false;

      void AddDummyRows(int count, bool isOddBlock) {
        for (int i = 0; i < count; i++) {
          elementValueList.Add(isOddBlock ? oddDummyValues : dummyValues);
        }
      }

      profileDataRows_ = new List<ElementRowValue>();

      foreach (var block in function.SortedBlocks) {
        bool isOddBlock = block.HasOddIndexInFunction;

        for (int i = 0; i < block.Tuples.Count; i++) {
          var tuple = block.Tuples[i];
          int currentLine = tuple.TextLocation.Line;
          bool isSeparatorLine = settings_.ShowBlockSeparatorLine &&
                                 i == block.Tuples.Count - 1;

          // Add dummy empty list view lines to match document text.
          if (currentLine > prevLine + 1) {
            AddDummyRows(currentLine - prevLine - 1, isOddBlock);
          }

          // Check if there is any data associated with the element.
          // Have the row match the background color used in the doc.
          var rowValues = columnData.GetValues(tuple);

          if (rowValues != null) {
            rowValues.BorderBrush = blockSeparatorColor;

            if (rowValues.BackColor == null && isOddBlock) {
              rowValues.BackColor = oddBackColor;
            }

            foreach (var columnValue in rowValues.ColumnValues) {
              columnValue.Value.TextFont = font;
              columnValue.Value.TextSize = fontSize;
            }

            // Add dummy cells for the missing ones, needed for column separators.
            if (rowValues.ColumnValues.Count != columnData.Columns.Count) {
              foreach (var column in columnData.Columns) {
                if (!rowValues.Columns.Contains(column)) {
                  rowValues.ColumnValues[column] = MakeDummyCell();
                }
              }
            }

            profileDataRows_.Add(rowValues);
          }
          else {
            if (isSeparatorLine) {
              rowValues = isOddBlock ? MakeDummyRow(oddBackColor) : MakeDummyRow();
            }
            else {
              rowValues = isOddBlock ? oddDummyValues : dummyValues;
            }
          }

          // Add a separator line at the bottom of the current row
          // if the next instr. is found in another block.
          if (isSeparatorLine) {
            rowValues.BorderThickness = new Thickness(0, 0, 0, 1);
            rowValues.BorderBrush = blockSeparatorColor;
          }

          //? if (showColumnSeparators) {
          foreach (var value in rowValues.Values) {
            value.BorderBrush = blockSeparatorColor;
            value.BorderThickness = new Thickness(0, 0, 1, 0);
          }

          elementValueList.Add(rowValues);
          prevLine = currentLine;
        }

        prevIsOddBlock = isOddBlock;
      }

      // Add empty lines at the end to match document,
      // otherwise scrolling can get out of sync.
      if (rowCount != prevLine + 1) {
        AddDummyRows(rowCount - prevLine, prevIsOddBlock);
      }

      profileColumnHeaders_ = OptionalColumn.AddListViewColumns(ColumnsList, columnData.Columns);

      foreach (var columnHeader in profileColumnHeaders_) {
        columnHeader.Header.Click += ColumnHeaderOnClick;
        columnHeader.Header.MouseDoubleClick += ColumnHeaderOnDoubleClick;
        columnHeader.Header.ContextMenu = new ContextMenu();

        //? TODO: Add context menu
        //? columnHeader.Header.ContextMenu.Items.Add(new MenuItem() { Header = "hey" });
      }

      UpdateColumnWidths();
      ColumnsList.ItemsSource = new ListCollectionView(elementValueList);
      ColumnsList.Background = settings_.BackgroundColor.AsBrush();
    }

    UpdateColumnsList(settings_);
  }

  public void UpdateColumnWidths() {
    if (profileDataRows_ == null ||
        profileColumnHeaders_ == null) {
      return;
    }

    var maxColumnTextSize = new Dictionary<OptionalColumn, double>();
    var maxColumnExtraSize = new Dictionary<OptionalColumn, double>();
    var font = new FontFamily(settings_.FontName);
    double fontSize = settings_.FontSize;
    const double maxBarWidth = 50; //? TODO: Shared def with Styles.xaml TimePercentageColumnValueTemplate
    const double columnMargin = 4;

    foreach (var rowValues in profileDataRows_) {
      foreach (var columnValue in rowValues.ColumnValues) {
        columnValue.Value.TextFont = font;
        columnValue.Value.TextSize = fontSize;

        // Remember the max text length for each column
        // to later set the MinWidth for alignment.
        maxColumnTextSize.CollectMaxValue(columnValue.Key, columnValue.Value.Text.Length);

        if (columnValue.Value.ShowPercentageBar ||
            columnValue.Value.ShowIcon) {
          double extraColumnWidth = columnMargin;

          if (columnValue.Value.ShowPercentageBar) {
            extraColumnWidth += columnValue.Value.ValuePercentage * maxBarWidth;
          }

          if (columnValue.Value.ShowIcon) {
            // Width of the icon is at most the height of the row.
            extraColumnWidth += ColumnsListItemHeight + columnMargin;
          }

          maxColumnExtraSize.CollectMaxValue(columnValue.Key, extraColumnWidth);
        }
      }
    }

    // Set the MinWidth of the text for each cell.
    foreach (var pair in maxColumnTextSize) {
      var columnContentSize = Utils.MeasureString((int)pair.Value, settings_.FontName, settings_.FontSize);
      double columnWidth = columnContentSize.Width + columnMargin;
      maxColumnTextSize[pair.Key] = Math.Ceiling(columnWidth);

      // Also set the initial width of each column header.
      // For the header, consider image and percentage bar too.
      if (maxColumnExtraSize.TryGetValue(pair.Key, out double extraSize)) {
        columnWidth += extraSize;
      }

      //? TODO: Pass options and check RemoveEmptyColumns
      if (columnWidth == 0) {
        pair.Key.IsVisible = false;
      }
      else {
        var columnTitleSize = Utils.MeasureString(pair.Key.Title, settings_.FontName, settings_.FontSize);
        var gridColumn = profileColumnHeaders_.Find(item => item.Header.Tag.Equals(pair.Key));

        if (gridColumn.Column == null || gridColumn.Header == null) {
          continue;
        }

        columnWidth = Math.Max(columnWidth, columnTitleSize.Width + columnMargin);
        gridColumn.Header.Width = columnWidth;
        gridColumn.Column.Width = columnWidth;
      }

      //Trace.WriteLine($"Column width {columnWidth} for {pair.Key.ColumnName}");
    }

    foreach (var row in profileDataRows_) {
      foreach (var columnValue in row.ColumnValues) {
        columnValue.Value.MinTextWidth = maxColumnTextSize[columnValue.Key];
      }
    }
  }

  public void ScrollToVerticalOffset(double offset) {
    // Sync scrolling with the optional columns.
    var columnScrollViewer = Utils.FindChild<ScrollViewer>(ColumnsList);

    if (columnScrollViewer != null) {
      columnScrollViewer.ScrollToVerticalOffset(offset);
    }
  }

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  private void UpdateColumnsList(DocumentSettings settings) {
    SelectedLineBrush = settings.SelectedValueColor.AsBrush();
    ColumnsList.Background = ColorBrushes.GetBrush(settings.BackgroundColor);
    ColumnsListItemHeight = Utils.MeasureString("0123456789ABCFEFGH", settings.FontName, settings.FontSize).Height;
  }

  private void ColumnHeaderOnClick(object sender, RoutedEventArgs e) {
    if (((GridViewColumnHeader)sender).Tag is OptionalColumn column &&
        column.HeaderClickHandler != null) {
      column.HeaderClickHandler(column);
      UpdateColumnWidths();
    }
  }

  private void ColumnHeaderOnDoubleClick(object sender, RoutedEventArgs e) {
    if (((GridViewColumnHeader)sender).Tag is OptionalColumn column &&
        column.HeaderDoubleClickHandler != null) {
      column.HeaderDoubleClickHandler(column);
      UpdateColumnWidths();
    }
  }

  private void ColumnsList_ScrollChanged(object sender, ScrollChangedEventArgs e) {
    if (Math.Abs(e.VerticalChange) < double.Epsilon) {
      return;
    }

    ScrollChanged?.Invoke(this, e);
  }
}
