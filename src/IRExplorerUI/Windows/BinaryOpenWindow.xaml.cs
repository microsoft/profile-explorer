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
using IRExplorerUI.Compilers;
using IRExplorerUI.Compilers.ASM;
using Microsoft.Win32;

// Add Options struct 
// Save recent exec files with pdb pairs
// Caching that checks for same CRC
// Try to fill PDB path

namespace IRExplorerUI {
    public partial class BinaryOpenWindow : Window, INotifyPropertyChanged {
        private CancelableTaskInstance loadTask_;
        private bool isLoadingBinary_;

        public BinaryOpenWindow(ISession session, bool inDiffMode) {
            InitializeComponent();
            Session = session;
            InDiffMode = inDiffMode;

            DataContext = this;
            loadTask_ = new CancelableTaskInstance(true);
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
        
        public string BinaryFilePath { get; set; }
        public string DiffBinaryFilePath { get; set; }
        public bool InDiffMode { get; set; }

        private async void OpenButton_Click(object sender, RoutedEventArgs e) {
            await OpenFile();
        }

        private async Task OpenFile() {
            BinaryFilePath = Utils.CleanupPath(BinaryFilePath);
            DiffBinaryFilePath = Utils.CleanupPath(DiffBinaryFilePath);

            if (Utils.ValidateFilePath(BinaryFilePath, BinaryAutocompleteBox, "binary", this) &&
                (!InDiffMode || (Utils.ValidateFilePath(DiffBinaryFilePath, DiffBinaryAutocompleteBox, "binary", this)))) {
                if (InDiffMode) {
                    App.Settings.AddRecentComparedFiles(BinaryFilePath, DiffBinaryFilePath);
                }
                else {
                    App.Settings.AddRecentFile(BinaryFilePath);
                }

                App.SaveApplicationSettings();
                DialogResult = true;
                Close();
            }
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

        private async void RecentMenuItem_Click(object sender, RoutedEventArgs e) {
            RecentButton.ContextMenu.IsOpen = false;
            var menuItem = sender as MenuItem;

            if (menuItem.Tag == null) {
                App.Settings.ClearRecentComparedFiles();
            }
            else {
                var pathPair = menuItem.Tag as Tuple<string, string>;
                BinaryFilePath = pathPair.Item1;
                DiffBinaryFilePath = pathPair.Item2;
                await OpenFile();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            BinaryAutocompleteBox.Focus();
        }

        private void BinaryBrowseButton_Click(object sender, RoutedEventArgs e) =>
            Utils.ShowOpenFileDialog(BinaryAutocompleteBox, "Binary Files|*.exe;*.dll;*.sys;|All Files|*.*");

        private void DiffBinaryBrowseButton_Click(object sender, RoutedEventArgs e) =>
           Utils.ShowOpenFileDialog(DiffBinaryAutocompleteBox, "Binary Files|*.exe;*.dll;*.sys;|All Files|*.*");

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        public void NotifyPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
