using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using LumenWorks.Framework.IO.Csv;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Controls.Primitives;

//? TODO: Filter by size
//? Other colors for columns

namespace RLDiffViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        class AppOptions
        {
            public string IRExplorerPath { get; set; }
            public string DiffReportPath { get; set; }
            public string BaseFolderPath { get; set; }
            public string DiffFolderPath { get; set; }
            public string SectionName { get; set; }
            public string Target { get; set; }
            public string FunctionFilter { get; set; }
            public string TestFilter { get; set; }
        }

        private AppOptions options_;
        private int columnIndex_;
        private DataTable dataTable_;
        private int sortedColumnIndex_;
        private ListSortDirection sortedColumnDirection_;

        public MainWindow()
        {
            InitializeComponent();
            options_ = new AppOptions()
            {
                Target = "amd64",
                IRExplorerPath = @"c:\ir-explorer\src\IRExplorerUI\bin\Release\net5.0\irexplorer.exe",
                SectionName = @"Tuples after Global Optimizer (-db20 == DB_GLOBOPT)"
            };

            var args = Environment.GetCommandLineArgs();

            if (args.Length >= 5)
            {
                options_.DiffReportPath = args[3];
                options_.BaseFolderPath = args[1];
                options_.DiffFolderPath = args[2];
                options_.Target = args[4];
                LoadDiffReport();
            }

            DataContext = options_;
        }

        private void DiffView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = DiffView.SelectedItem as DataRowView;
            var fileName = item.Row.Field<string>(0);
            var funcName = item.Row.Field<string>(1);

            var fileDir = Path.GetDirectoryName(fileName);
            fileName = Path.GetFileName(fileName);
            var baseFilePath = Path.Combine(options_.BaseFolderPath, fileDir, $"master.{options_.Target}", fileName);
            var diffFilePath = Path.Combine(options_.DiffFolderPath, fileDir, $"diffs.{options_.Target}", fileName);
            var psi = new ProcessStartInfo(options_.IRExplorerPath, $"{baseFilePath} {diffFilePath} -func {funcName} -section \"{options_.SectionName}\"")
            {
                UseShellExecute = true,
            };

            Process.Start(psi);
        }

        private void DiffView_AutoGeneratingColumn(object sender, System.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e)
        {
            columnIndex_++;

            if (columnIndex_ > 0 && (columnIndex_ % 3 == 0))
            {
                var templateName = "OtherDataItemCellTemplate";

                if (e.PropertyName.ToLower().Contains("instr"))
                {
                    templateName = "InstructionDataItemCellTemplate";
                }
                else if (e.PropertyName.ToLower().Contains("load"))
                {
                    templateName = "LoadDataItemCellTemplate";
                }
                else if (e.PropertyName.ToLower().Contains("store"))
                {
                    templateName = "StoreDataItemCellTemplate";
                }


                e.Column = new CustomDataGridTemplateColumn
                {
                    CellTemplate = (DataTemplate)DiffView.FindResource(templateName),
                    CellEditingTemplate = (DataTemplate)DiffView.FindResource(templateName),
                    Header = e.PropertyName,
                    ColumnName = e.PropertyName
                };
            }
            else
            {
                e.Column = new DataGridTextColumn
                {
                    Binding = new Binding(e.PropertyName),
                    Header = e.PropertyName
                };
            }
        }

        private void columnHeader_Click(object sender, RoutedEventArgs e)
        {
            var columnHeader = sender as DataGridColumnHeader;
            if (columnHeader != null)
            {
                var direction = columnHeader.Column.SortDirection;
                var currentDirection = direction.HasValue ? direction.Value : ListSortDirection.Ascending;
                columnHeader.Column.SortDirection = currentDirection == ListSortDirection.Ascending ?
                                                    ListSortDirection.Descending : ListSortDirection.Ascending;
                sortedColumnIndex_ = columnHeader.Column.DisplayIndex;
                sortedColumnDirection_ = currentDirection;
                DiffView.ItemsSource = SortDiffs(dataTable_, sortedColumnIndex_, sortedColumnDirection_).DefaultView;
            }
        }

        private DataTable SortDiffs(DataTable table, int index, ListSortDirection sortDirection)
        {
            var data = table.ApplySort((r1, r2) =>
            {
                if (index >= 2)
                {
                    if (sortDirection == ListSortDirection.Ascending)
                    {
                        return Convert.ToInt32(r1.ItemArray[index]) - Convert.ToInt32(r2.ItemArray[index]);
                    }
                    else
                    {
                        return Convert.ToInt32(r2.ItemArray[index]) - Convert.ToInt32(r1.ItemArray[index]);
                    }
                }
                else
                {
                    if (sortDirection == ListSortDirection.Ascending)
                    {
                        return r1.Field<string>(index).CompareTo(r2.Field<string>(index));
                    }
                    else
                    {
                        return r2.Field<string>(index).CompareTo(r1.Field<string>(index));
                    }
                }
            });

            columnIndex_ = 0;
            return data;
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadDiffReport();
        }

        private void LoadDiffReport()
        {
            if (string.IsNullOrEmpty(options_.DiffReportPath) ||
                            !File.Exists(options_.DiffReportPath.Trim()))
            {
                MessageBox.Show("Diff report (CSV) file path is missing or invalid", "RL Diff Result Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            else if (string.IsNullOrEmpty(options_.BaseFolderPath) ||
                     !Directory.Exists(options_.BaseFolderPath.Trim()))
            {
                MessageBox.Show("Base folder path is missing or invalid", "RL Diff Result Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            else if (string.IsNullOrEmpty(options_.DiffFolderPath) ||
                     !Directory.Exists(options_.DiffFolderPath.Trim()))
            {
                MessageBox.Show("Diff folder path is missing or invalid", "RL Diff Result Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var csvReader = new CachedCsvReader(new StreamReader(options_.DiffReportPath.Trim()), true))
                {
                    var table = new DataTable();
                    csvReader.DuplicateHeaderEncountered += (s, e) => e.HeaderName = $"{e.HeaderName}_{1 + e.Index - 2}";
                    table.Load(csvReader);
                    DiffView.ItemsSource = table.DefaultView;
                    dataTable_ = table;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load diff report: {ex.Message}", "RL Diff Result Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestFilterTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var result = dataTable_.ApplyFilters(FunctionFilterTextbox.Text, TestFilterTextbox.Text);
            DiffView.ItemsSource = SortDiffs(result, sortedColumnIndex_, sortedColumnDirection_).DefaultView;
        }

        private void FunctionFilterTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var result = dataTable_.ApplyFilters(FunctionFilterTextbox.Text, TestFilterTextbox.Text);
            DiffView.ItemsSource = SortDiffs(result, sortedColumnIndex_, sortedColumnDirection_).DefaultView;
        }
    }

    public static class DataTableExtensions
    {
        public static DataTable ApplySort(this DataTable table, Comparison<DataRow> comparison)
        {
            DataTable clone = table.Clone();
            List<DataRow> rows = new List<DataRow>();
            foreach (DataRow row in table.Rows)
            {
                rows.Add(row);
            }

            rows.Sort(comparison);

            foreach (DataRow row in rows)
            {
                clone.Rows.Add(row.ItemArray);
            }

            return clone;
        }

        public static DataTable ApplyFilters(this DataTable table, string functionFilter, string testFilter)
        {
            DataTable clone = table.Clone();
            List<DataRow> rows = new List<DataRow>();
            foreach (DataRow row in table.Rows)
            {
                if (!string.IsNullOrEmpty(functionFilter))
                {
                    var function = row.Field<string>(1);

                    if (!function.ToLower().Contains(functionFilter.ToLower()))
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(testFilter))
                {
                    var test = row.Field<string>(0);

                    if (!test.ToLower().Contains(testFilter.ToLower()))
                    {
                        continue;
                    }
                }

                rows.Add(row);
            }

            foreach (DataRow row in rows)
            {
                clone.Rows.Add(row.ItemArray);
            }

            return clone;
        }
    }

    public class CustomDataGridTemplateColumn : DataGridTemplateColumn
    {
        public string ColumnName
        {
            get;
            set;
        }

        protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
        {
            ContentPresenter cp = (ContentPresenter)base.GenerateElement(cell, dataItem);
            BindingOperations.SetBinding(cp, ContentPresenter.ContentProperty, new Binding(this.ColumnName));
            return cp;
        }
    }
}
