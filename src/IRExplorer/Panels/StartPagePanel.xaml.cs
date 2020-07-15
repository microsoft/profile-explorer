using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace IRExplorer.Panels {
    /// <summary>
    /// Interaction logic for StartPagePanel.xaml
    /// </summary>
    public partial class StartPagePanel : UserControl {
        public StartPagePanel() {
            InitializeComponent();
        }

        public event EventHandler<string> OpenRecentDocument;
        public event EventHandler<Tuple<string, string>> OpenRecentDiffDocuments;

        public void ReloadFileList() {
            RecentFilesListBox.ItemsSource = new ListCollectionView(App.Settings.RecentFiles);
            RecentFilesListBox.Visibility = App.Settings.RecentFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RecentDiffFilesListBox.ItemsSource = new ListCollectionView(App.Settings.RecentComparedFiles);
            RecentDiffFilesListBox.Visibility = App.Settings.RecentComparedFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RecentFilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            InvokeOpenRecentDocument();
            e.Handled = true;
        }

        private void RecentFilesListBox_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter) {
                InvokeOpenRecentDocument();
                e.Handled = true;
            }
        }

        private void RecentDiffFilesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            InvokeOpenRecentDiffDocuments();
            e.Handled = true;
        }


        private void RecentDiffFilesListBox_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter) {
                InvokeOpenRecentDiffDocuments();
                e.Handled = true;
            }
        }

        private void InvokeOpenRecentDocument() {
            if (RecentFilesListBox.SelectedItem != null) {
                OpenRecentDocument?.Invoke(this, (string)RecentFilesListBox.SelectedItem);
            }
        }

        private void InvokeOpenRecentDiffDocuments() {
            if (RecentDiffFilesListBox.SelectedItem != null) {
                OpenRecentDiffDocuments?.Invoke(this, (Tuple<string, string>)RecentDiffFilesListBox.SelectedItem);
            }
        }

        private void TextBlock_MouseDown(object sender, MouseButtonEventArgs e) {
            App.InstallExtension();
        }

        private void TextBlock_MouseDown_1(object sender, MouseButtonEventArgs e) {
            App.OpenDocumentation();
        }
    }
}
