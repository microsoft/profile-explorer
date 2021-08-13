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

        public BinaryOpenWindow(ISession session) {
            InitializeComponent();
            Session = session;
            Options = App.Settings.DissasemblerOptions;
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

        public BinaryDissasemblerOptions Options { get; set; }
        public string BinaryFilePath { get; set; }
        public string DebugFilePath { get; set; }

        private async void OpenButton_Click(object sender, RoutedEventArgs e) {
            await OpenFile();
        }

        private async Task OpenFile() {
            if (Utils.ValidateFilePath(BinaryFilePath, BinaryAutocompleteBox, "binary", this) &&
                Utils.ValidateOptionalFilePath(DebugFilePath, DebugAutocompleteBox, "debug", this)) {
                App.Settings.AddRecentFile(BinaryFilePath);
                App.SaveApplicationSettings();

                var task = loadTask_.CreateTask();
                IsLoadingBinary = true;

                var dissasembler = new BinaryDissasembler(Options);
                var outputFile = await dissasembler.DissasembleAsync(BinaryFilePath, DebugFilePath, progressInfo => {
                    Dispatcher.BeginInvoke((Action)(() => {
                        LoadProgressBar.Maximum = progressInfo.Total;
                        LoadProgressBar.Value = progressInfo.Current;

                        LoadProgressLabel.Text = progressInfo.Stage switch {
                            BinaryDissasemblerStage.Dissasembling => "Dissasembling",
                            BinaryDissasemblerStage.PostProcessing => "Post-processing",
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

                if (outputFile != null) {
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

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            if(IsLoadingBinary) {
                loadTask_.CancelTask();
                return;
            }

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

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

    }
}
