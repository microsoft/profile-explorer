// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IRExplorerCore;
using Microsoft.Win32;
using Microsoft.Windows.EventTracing.Processes;

// Add Options struct 
// Save recent exec files with pdb pairs
// Caching that checks for same CRC
// Try to fill PDB path

namespace IRExplorerUI {
    public partial class BinaryOpenWindow : Window, INotifyPropertyChanged {
        private CancelableTaskInstance loadTask_;
        private bool isLoadingBinary_;
        private BinaryDisassemblerOptions options;

        public BinaryOpenWindow(ISession session, bool inDiffMode) {
            InitializeComponent();
            Session = session;
            Options = App.Settings.DisassemblerOptions;
            InDiffMode = inDiffMode;

            DataContext = this;
            loadTask_ = new CancelableTaskInstance();
        }

        public bool IsLoadingBinary {
            get => isLoadingBinary_;
            set {
                if (isLoadingBinary_ != value) {
                    isLoadingBinary_ = value;
                    OnPropertyChange(nameof(IsLoadingBinary));
                    OnPropertyChange(nameof(InputControlsEnabled));
                }
            }
        }

        public ISession Session { get; set; }
        public bool InputControlsEnabled => !isLoadingBinary_;
        public event PropertyChangedEventHandler PropertyChanged;

        public BinaryDisassemblerOptions Options {
            get => options;
            set {
                options = value;
                NotifyPropertyChanged(nameof(Options));
            }
        }

        public string BinaryFilePath { get; set; }
        public string DiffBinaryFilePath { get; set; }
        public string DebugFilePath { get; set; }
        public string DiffDebugFilePath { get; set; }
        public string OutputFilePath { get; set; }
        public string DiffOutputFilePath { get; set; }
        public bool InDiffMode { get; set; }

        private async void OpenButton_Click(object sender, RoutedEventArgs e) {
            await OpenFile();
        }

        private async Task OpenFile() {
            BinaryFilePath = Utils.CleanupPath(BinaryFilePath);
            DebugFilePath = Utils.CleanupPath(DebugFilePath);
            DiffBinaryFilePath = Utils.CleanupPath(DiffBinaryFilePath);
            DiffDebugFilePath = Utils.CleanupPath(DiffDebugFilePath);

            if (Utils.ValidateFilePath(BinaryFilePath, BinaryAutocompleteBox, "binary", this) &&
                Utils.ValidateOptionalFilePath(DebugFilePath, DebugAutocompleteBox, "debug", this)
                && (!InDiffMode ||
                    (Utils.ValidateFilePath(DiffBinaryFilePath, DiffBinaryAutocompleteBox, "binary", this) &&
                     Utils.ValidateOptionalFilePath(DiffDebugFilePath, DiffDebugAutocompleteBox, "debug", this)))) {
                if (InDiffMode) {
                    App.Settings.AddRecentComparedFiles(BinaryFilePath, DiffBinaryFilePath);
                }
                else {
                    App.Settings.AddRecentFile(BinaryFilePath);
                }
                

                App.SaveApplicationSettings();
                var task = loadTask_.CreateTask();
                IsLoadingBinary = true;

                string outputFile = await OutputSingleFile(task, BinaryFilePath, DebugFilePath);
                string diffOutputFile = InDiffMode ? await OutputSingleFile(task, DiffBinaryFilePath, DiffDebugFilePath) : null;

                if (outputFile != null) {
                    OutputFilePath = outputFile;
                    DiffOutputFilePath = diffOutputFile;
                    DialogResult = true;
                    Close();
                }
                else if(!task.IsCanceled) {
                    MessageBox.Show($"Filed to dissasemble file {BinaryFilePath}", "IR Explorer",
                                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }

                IsLoadingBinary = false;
            }
        }

        private async Task<string> OutputSingleFile(CancelableTask task, string binaryFilePath, string debugFilePath) {
            var dissasembler = new BinaryDisassembler(Options);
            var outputFile = await dissasembler.DisassembleAsync(binaryFilePath, debugFilePath, progressInfo => {
                Dispatcher.BeginInvoke((Action)(() => {
                    LoadProgressBar.Maximum = progressInfo.Total;
                    LoadProgressBar.Value = progressInfo.Current;

                    LoadProgressLabel.Text = progressInfo.Stage switch {
                        BinaryDisassemblerStage.Disassembling => "Disassembling",
                        BinaryDisassemblerStage.PostProcessing => "Post-processing",
                    };

                    if (progressInfo.Total != 0) {
                        double percentage = (double)progressInfo.Current / (double)progressInfo.Total;
                        ProgressPercentLabel.Text = $"{Math.Round(percentage * 100)} %";
                        LoadProgressBar.IsIndeterminate = false;
                    }
                    else {
                        LoadProgressBar.IsIndeterminate = true;
                    }
                }));
            }, task);

            return outputFile;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            if(IsLoadingBinary) {
                loadTask_.CancelTask();
                return;
            }

            App.SaveApplicationSettings();
            DialogResult = false;
            Close();
        }

        private void RecentButton_Click(object sender, RoutedEventArgs e) {
            var menu = RecentButton.ContextMenu;
            menu.Items.Clear();

            foreach (var pathPair in App.Settings.RecentComparedFiles) {
                var item = new MenuItem();
                item.Header = $"Base: {pathPair.Item1}\nDiff: {pathPair.Item2}";
                item.Tag = pathPair;
                item.Click += RecentMenuItem_Click;
                menu.Items.Add(item);
                menu.Items.Add(new Separator());
            }

            var clearMenuItem = new MenuItem {
                Header = "Clear"
            };

            clearMenuItem.Click += RecentMenuItem_Click;
            menu.Items.Add(clearMenuItem);
            menu.IsOpen = true;
        }

        private void RecentMenuItem_Click(object sender, RoutedEventArgs e) {
            RecentButton.ContextMenu.IsOpen = false;
            var menuItem = sender as MenuItem;

            if (menuItem.Tag == null) {
                App.Settings.ClearRecentComparedFiles();
            }
            else {
                var pathPair = menuItem.Tag as Tuple<string, string>;
                BinaryFilePath = pathPair.Item1;
                DebugFilePath = pathPair.Item2;
                OpenFile();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            BinaryAutocompleteBox.Focus();
        }

        private void BinaryBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(BinaryAutocompleteBox, "Binary Files|*.exe;*.dll;*.sys;|All Files|*.*");

        private void DebugBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(DebugAutocompleteBox, "Debug Info Files|*.pdb|All Files|*.*");

        private void DiffBinaryBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(DiffBinaryAutocompleteBox, "Binary Files|*.exe;*.dll;*.sys;|All Files|*.*");

        private void DiffDebugBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(DiffDebugAutocompleteBox, "Debug Info Files|*.pdb|All Files|*.*");

        private void DisasmBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(DisasmAutocompleteBox, "Executables|*.exe|All Files|*.*");

        private void ToolBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(ToolAutocompleteBox, "Executables|*.exe|All Files|*.*");

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e) {
            Options.Reset();
            App.SaveApplicationSettings();
            DataContext = null;
            DataContext = this;
        }
        
        private void DetectButton_Click(object sender, RoutedEventArgs e) {
            var disasmPath = Options.DetectDissasembler();

            if(string.IsNullOrEmpty(disasmPath)) {
                using var centerForm = new DialogCenteringHelper(this);

                MessageBox.Show("Failed to find system dissasembler", "IR Explorer",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Options.DissasemblerPath = disasmPath;
            NotifyPropertyChanged(nameof(Options));
        }

        public void NotifyPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void BinaryAutocompleteBox_OnTextChanged(object sender, RoutedEventArgs e) {
            var path = Utils.FindPDBFile(Utils.CleanupPath(BinaryFilePath));

            if (path != null && string.IsNullOrEmpty(DebugFilePath)) {
                
                DebugAutocompleteBox.Text = path;
                DebugFilePath = path;
            }
        }

        private void DiffBinaryAutocompleteBox_OnTextChanged(object sender, RoutedEventArgs e) {
            var path = Utils.FindPDBFile(Utils.CleanupPath(DiffBinaryFilePath));

            if (path != null && string.IsNullOrEmpty(DiffDebugFilePath)) {
                DiffDebugAutocompleteBox.Text = path;
                DiffDebugFilePath = path;
            }
        }
    }
}
