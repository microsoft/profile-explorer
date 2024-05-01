// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Folding;
using IRExplorerUI.OptionsPanels;
using IRExplorerUI.Profile;

namespace IRExplorerUI.Document;

public partial class DocumentColumns : UserControl, INotifyPropertyChanged {
  private TextViewSettingsBase settings_;
  private OptionalColumnSettings columnSettings_;
  private List<ElementRowValue> profileDataRows_;
  private List<(GridViewColumnHeader Header, GridViewColumn Column)> profileColumnHeaders_;
  private double columnsListItemHeight_;
  private Brush selectedLineBrush_;
  private ListCollectionView profileRowCollection_;
  private List<(int StartOffset, int EndOffset)> foldedTextRegions_;
  private int rowFilterIndex_;
  private MarkedDocument associatedDocument_;
  private OptionsPanelHostWindow optionsPanelWindow_;

  public DocumentColumns() {
    InitializeComponent();
    profileColumnHeaders_ = new List<(GridViewColumnHeader Header, GridViewColumn Column)>();
    profileDataRows_ = new List<ElementRowValue>();
    foldedTextRegions_ = new List<(int StartOffset, int EndOffset)>();
  }

  public event EventHandler<ScrollChangedEventArgs> ScrollChanged;
  public event EventHandler<int> RowSelected;
  public event PropertyChangedEventHandler PropertyChanged;
  public event EventHandler<OptionalColumn> ColumnSettingsChanged;

  public double ColumnsListItemHeight {
    get => columnsListItemHeight_;
    set {
      if (Math.Abs(columnsListItemHeight_ - value) > double.Epsilon) {
        columnsListItemHeight_ = value;
        OnPropertyChanged();
      }
    }
  }

  public TextViewSettingsBase Settings {
    get => settings_;
    set => settings_ = value;
  }

  public OptionalColumnSettings ColumnSettings {
    get => columnSettings_;
    set => columnSettings_ = value;
  }

  public bool UseSmallerFontSize { get; set; }
  public double TextFontSize => UseSmallerFontSize ? settings_.FontSize - 1 : settings_.FontSize;

  public void SelectRow(int index) {
    if (index >= 0 && ColumnsList.Items.Count > index) {
      ColumnsList.SelectedIndex = index;
    }
  }

  public void Reset() {
    // Unregister column header event handlers.
    foreach (var columnHeader in profileColumnHeaders_) {
      columnHeader.Header.MouseLeftButtonDown -= ColumnHeaderOnClick;
      columnHeader.Header.MouseDoubleClick -= ColumnHeaderOnDoubleClick;
      columnHeader.Header.MouseRightButtonUp -= ColumnHeaderOnRightClick;
      columnHeader.Header.MouseRightButtonUp -= ColumnSettingsClickHandler;
    }

    OptionalColumn.RemoveListViewColumns(ColumnsList);
    ColumnsList.ItemsSource = null;
    profileColumnHeaders_.Clear();
    foldedTextRegions_.Clear();
  }

  public async Task Display(IRDocumentColumnData columnData, MarkedDocument associatedDocument) {
    Reset(); // Remove any existing columns.
    UpdateColumnsList();

    if (!columnData.HasData) {
      return;
    }

    associatedDocument_ = associatedDocument;
    var function = associatedDocument.Function;
    int rowCount = associatedDocument.LineCount;

    var elementValueList = await Task.Run(() => {
      var elementValueList = new List<ElementRowValue>(function.TupleCount);
      var oddBackColor = settings_.AlternateBackgroundColor.AsBrush();
      var blockSeparatorColor = settings_.ShowBlockSeparatorLine ? settings_.BlockSeparatorColor.AsBrush() : null;
      var font = new FontFamily(settings_.FontName);
      double fontSize = TextFontSize;

      var comparer = new IRDocumentColumnData.ColumnComparer();
      Dictionary<OptionalColumn, ElementColumnValue> dummyCells = new(comparer);
      Dictionary<OptionalColumn, ElementColumnValue> separatorDummyCells = new(comparer);

      ElementColumnValue MakeDummyCell(bool hasSeparator) {
        var columnValue = new ElementColumnValue("");
        columnValue.TextFont = font;
        columnValue.TextSize = fontSize;
        return columnValue;
      }

      ElementColumnValue GetDummyCell(OptionalColumn column, bool hasSeparator) {
        return hasSeparator ? separatorDummyCells[column] : dummyCells[column];
      }

      ElementRowValue MakeDummyRow(bool hasSeparator, Brush backColor = null) {
        var row = new ElementRowValue(null) {
          BackColor = backColor,
          BorderBrush = blockSeparatorColor
        };

        foreach (var column in columnData.Columns) {
          var cell = MakeDummyCell(hasSeparator);
          row.ColumnValues[column] = cell;
          columnData.AddColumnValue(cell, column);

          if (hasSeparator) {
            separatorDummyCells[column] = cell;
          }
          else {
            dummyCells[column] = cell;
          }
        }

        if (hasSeparator) {
          row.BorderThickness = new Thickness(0, 0, 0, 1);
          row.BorderBrush = blockSeparatorColor;
        }

        SetValueBorder(row);
        return row;
      }

      void SetValueBorder(ElementRowValue row) {
        foreach (var value in row.Values) {
            value.BorderBrush = blockSeparatorColor;
            value.BorderThickness = new Thickness(1, 0, 1, 0);
        }
      }

      var dummyRow = MakeDummyRow(false);
      var separatorDummyRow = MakeDummyRow(true);
      var oddDummyRow = MakeDummyRow(false, oddBackColor);
      var oddSeparatorDummyRow = MakeDummyRow(true, oddBackColor);
      int prevLine = -1;
      bool prevIsOddBlock = false;

      void AddDummyRows(int count, bool isOddBlock) {
        for (int i = 0; i < count; i++) {
          elementValueList.Add(isOddBlock ? oddDummyRow: dummyRow);
        }
      }

      profileDataRows_ = new List<ElementRowValue>();

      foreach (var block in function.SortedBlocks) {
        bool isOddBlock = block.HasOddIndexInFunction;

        for (int i = 0; i < block.Tuples.Count; i++) {
          var tuple = block.Tuples[i];
          int currentLine = tuple.TextLocation.Line;
          bool isSeparatorLine = settings_.ShowBlockSeparatorLine &&
                                 block.IndexInFunction < function.BlockCount - 1 &&
                                 i == block.TupleCount - 1;

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
                  rowValues.ColumnValues[column] = GetDummyCell(column, isSeparatorLine);
                }
              }
            }

            // Add a separator line at the bottom of the current row
            // if the next instr. is found in another block.
            if (isSeparatorLine) {
              rowValues.BorderBrush = blockSeparatorColor;
              rowValues.BorderThickness = new Thickness(0, 0, 0, 1);
            }

            SetValueBorder(rowValues);
            profileDataRows_.Add(rowValues);
          }
          else {
            // No data at all, use an empty row.
            if (isSeparatorLine) {
              rowValues = isOddBlock ? oddSeparatorDummyRow : separatorDummyRow;
            }
            else {
              rowValues = isOddBlock ? oddDummyRow : dummyRow;
            }
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

      return elementValueList;
    });

    // Handle clicks on the column headers.
    Trace.WriteLine($"Show {columnData.Columns.Count} columns");
    var sortedColumns = columnSettings_.FilterAndSortColumns(columnData.Columns);
    profileColumnHeaders_ = OptionalColumn.AddListViewColumns(ColumnsList, sortedColumns);

    foreach (var columnHeader in profileColumnHeaders_) {
      columnHeader.Header.MouseLeftButtonDown += ColumnHeaderOnClick;
      columnHeader.Header.MouseDoubleClick += ColumnHeaderOnDoubleClick;
      columnHeader.Header.MouseRightButtonUp += ColumnHeaderOnRightClick;
      columnHeader.Header.MouseRightButtonUp += ColumnSettingsClickHandler;
    }

    // Display the columns.
    profileRowCollection_ = new ListCollectionView(elementValueList);
    profileRowCollection_.Filter += ProfileListRowFilter;
    UpdateColumnWidths();
    ColumnsList.ItemsSource = profileRowCollection_;
    UpdateColumnsList();
  }

  private void ColumnSettingsClickHandler(object sender, RoutedEventArgs e) {
    if (optionsPanelWindow_ != null) {
      optionsPanelWindow_.Close();
      optionsPanelWindow_ = null;
      return;
    }

    var columnHeader = (GridViewColumnHeader)sender;
    var column = (OptionalColumn)columnHeader.Tag;
    optionsPanelWindow_ = OptionsPanelHostWindow.Create<ColumnOptionsPanel, OptionalColumnStyle>(
      column.Style.Clone(), columnHeader, null,
      async (newSettings, commit) => {
        if (!newSettings.Equals(column.Style)) {
          column.Style = newSettings;
          ColumnSettingsChanged?.Invoke(this, column);

          if (commit) {
            columnSettings_.AddColumnStyle(column, newSettings);
            App.SaveApplicationSettings();
          }

          return newSettings.Clone();
        }

        if (commit) {
          columnSettings_.AddColumnStyle(column, newSettings);
          App.SaveApplicationSettings();
        }

        return null;
      },
      () => optionsPanelWindow_ = null,
      new Point(0, columnHeader.ActualHeight - 1));
  }

  public void BuildColumnsVisibilityMenu(IRDocumentColumnData columnData, MenuItem menu,
                                         Action columnsChanged) {
    // Add the columns at the end of the menu,
    // keeping the original items.
    var defaultItems = DocumentUtils.SaveDefaultMenuItems(menu);
    menu.Items.Clear();

    foreach (var column in columnData.Columns) {
      var item = new MenuItem {
        Header = column.Title,
        Tag = column,
        IsCheckable = true,
        IsChecked = column.IsVisible,
        StaysOpenOnClick = true,
        Style = (Style)Application.Current.FindResource("SubMenuItemHeaderStyle")
      };

      item.Checked += (sender, args) => {
        if (sender is MenuItem menuItem &&
            menuItem.Tag is OptionalColumn column) {
          column.IsVisible = menuItem.IsChecked;
          columnSettings_.SetColumnVisibility(column, menuItem.IsChecked);
          columnsChanged();
        }
      };
      item.Unchecked += (sender, args) => {
        if (sender is MenuItem menuItem &&
            menuItem.Tag is OptionalColumn column) {
          column.IsVisible = menuItem.IsChecked;
          columnSettings_.SetColumnVisibility(column, menuItem.IsChecked);
          columnsChanged();
        }
      };

      defaultItems.Add(item);
    }

    DocumentUtils.RestoreDefaultMenuItems(menu, defaultItems);
  }

  public void SetupFoldedTextRegions(IEnumerable<FoldingSection> regions) {
    foldedTextRegions_.Clear();

    foreach (var region in regions) {
      if (region.IsFolded) {
        foldedTextRegions_.Add((region.StartOffset, region.EndOffset));
      }
    }
    
    foldedTextRegions_.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
    rowFilterIndex_ = 0;
    profileRowCollection_.Refresh();
  }

  public void HandleTextRegionFolded(FoldingSection section) {
    HandleTextRegionFolded((section.StartOffset, section.EndOffset));
  }

  public void HandleTextRegionFolded((int StartOffset, int EndOffset) section) {
    if (foldedTextRegions_.Contains(section)) {
      return;
    }

    foldedTextRegions_.Add(section);
    foldedTextRegions_.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
    rowFilterIndex_ = 0;
    profileRowCollection_.Refresh();
  }

  public void HandleTextRegionUnfolded(FoldingSection section) {
    HandleTextRegionUnfolded((section.StartOffset, section.EndOffset));
  }

  public void HandleTextRegionUnfolded((int StartOffset, int EndOffset) section) {
    foldedTextRegions_.Remove(section);
    rowFilterIndex_ = 0;
    profileRowCollection_.Refresh();
  }

  private bool ProfileListRowFilter(object item) {
    // Filter out rows to keep in sync with the collapsed
    // block foldings in the associated document.
    foreach (var range in foldedTextRegions_) {
      var startLine = associatedDocument_.GetLineByOffset(range.StartOffset);
      var endLine = associatedDocument_.GetLineByOffset(range.EndOffset);

      if (startLine.LineNumber - 1 > rowFilterIndex_) {
        break; // Early stop in sorted range list.
      }

      if (rowFilterIndex_ >= startLine.LineNumber &&
          rowFilterIndex_ < endLine.LineNumber) {
        rowFilterIndex_++;
        return false;
      }
    }

    rowFilterIndex_++;
    return true;
  }

  public void UpdateColumnWidths() {
    if (profileDataRows_ == null ||
        profileColumnHeaders_ == null) {
      return;
    }

    var maxColumnTextSize = new Dictionary<OptionalColumn, double>();
    var maxColumnExtraSize = new Dictionary<OptionalColumn, double>();
    var font = new FontFamily(settings_.FontName);
    double fontSize = TextFontSize;
    double maxBarWidth = settings_.ProfileMarkerSettings.MaxPercentageBarWidth;
    const double columnMargin = 8;

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
      var columnContentSize = Utils.MeasureString((int)pair.Value, settings_.FontName, TextFontSize);
      double columnWidth = columnContentSize.Width + columnMargin;
      maxColumnTextSize[pair.Key] = Math.Ceiling(columnWidth);

      // Also set the initial width of each column header.
      // For the header, consider image and percentage bar too.
      if (maxColumnExtraSize.TryGetValue(pair.Key, out double extraSize)) {
        columnWidth += extraSize;
      }

      if (columnWidth == 0 && columnSettings_.RemoveEmptyColumns) {
        pair.Key.IsVisible = false;
      }
      else {
        var columnTitleSize = Utils.MeasureString(pair.Key.Title, settings_.FontName, TextFontSize);
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

  public void UpdateColumnsList() {
    ColumnsList.Background = settings_.BackgroundColor.AsBrush();
    ColumnsList.Background = ColorBrushes.GetBrush(settings_.BackgroundColor);
    ColumnsListItemHeight = Utils.MeasureString("0123456789ABCXYZ!?|()",
      settings_.FontName, TextFontSize).Height;
  }

  private void ColumnHeaderOnClick(object sender, RoutedEventArgs e) {
    if (((GridViewColumnHeader)sender).Tag is OptionalColumn column &&
        column.HeaderClickHandler != null) {
      column.HeaderClickHandler(column, (GridViewColumnHeader)sender);
      UpdateColumnWidths();
      e.Handled = true;
    }
  }

  private void ColumnHeaderOnRightClick(object sender, RoutedEventArgs e) {
    if (((GridViewColumnHeader)sender).Tag is OptionalColumn column &&
        column.HeaderRightClickHandler != null) {
      column.HeaderRightClickHandler(column, (GridViewColumnHeader)sender);
      UpdateColumnWidths();
      e.Handled = true;
    }
  }

  private void ColumnHeaderOnDoubleClick(object sender, RoutedEventArgs e) {
    if (((GridViewColumnHeader)sender).Tag is OptionalColumn column &&
        column.HeaderDoubleClickHandler != null) {
      column.HeaderDoubleClickHandler(column, (GridViewColumnHeader)sender);
      UpdateColumnWidths();
      e.Handled = true;
    }
  }

  private void ColumnsList_ScrollChanged(object sender, ScrollChangedEventArgs e) {
    if (Math.Abs(e.VerticalChange) < double.Epsilon) {
      return;
    }

    ScrollChanged?.Invoke(this, e);
  }

  private void ColumnsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
    if(ColumnsList.SelectedItem != null) {
      RowSelected?.Invoke(this, ColumnsList.SelectedIndex);
    }
  }
}