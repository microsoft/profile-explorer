// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerCore;

namespace IRExplorer {
    public static class BookmarkCommand {
        public static readonly RoutedUICommand JumpToBookmark =
            new RoutedUICommand("Untitled", "JumpToBookmark", typeof(BookmarksPanel));
        public static readonly RoutedUICommand RemoveBookmark =
            new RoutedUICommand("Untitled", "RemoveBookmark", typeof(BookmarksPanel));
        public static readonly RoutedUICommand RemoveAllBookmarks =
            new RoutedUICommand("Untitled", "RemoveAllBookmarks", typeof(BookmarksPanel));
        public static readonly RoutedUICommand MarkBookmark =
            new RoutedUICommand("Untitled", "MarkBookmark", typeof(BookmarksPanel));
        public static readonly RoutedUICommand UnmarkBookmark =
            new RoutedUICommand("Untitled", "UnmarkBookmark", typeof(BookmarksPanel));
    }

    public partial class BookmarksPanel : ToolPanelControl {
        private ObservableCollectionRefresh<Bookmark> bookmarks_;
        private IRDocument document_;
        private bool focusedOnce_;

        private IRPreviewToolTip previewTooltip_;

        // public event EventHandler OpenSection;

        public BookmarksPanel() {
            InitializeComponent();
            ResetBookmarks();
        }

        public ObservableCollectionRefresh<Bookmark> Bookmarks => bookmarks_;

        public void InitializeForDocument(IRDocument document) {
            document_ = document;
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e) {
            var bookmark = ((TextBox)sender).DataContext as Bookmark;
            document_.BookmarkInfoChanged(bookmark);
        }

        private void HideToolTip() {
            if (previewTooltip_ != null) {
                previewTooltip_.Hide();
                previewTooltip_ = null;
            }
        }

        private void ListViewItem_MouseEnter(object sender, MouseEventArgs e) {
            HideToolTip();
            var listItem = sender as ListViewItem;
            var bookmark = listItem.DataContext as Bookmark;
            previewTooltip_ = new IRPreviewToolTip(600, 100, document_, bookmark.Element);
            listItem.ToolTip = previewTooltip_;
        }

        private void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
            var textBox = e.OriginalSource as TextBox;
            textBox?.SelectAll();
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (sender is TextBox textbox && !textbox.IsKeyboardFocusWithin) {
                if (e.OriginalSource.GetType().Name == "TextBoxView") {
                    e.Handled = true;
                    textbox.Focus();
                }
            }
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e) {
            var bookmark = ((CheckBox)sender).DataContext as Bookmark;
            document_.BookmarkInfoChanged(bookmark);
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e) {
            var bookmark = ((CheckBox)sender).DataContext as Bookmark;
            document_.BookmarkInfoChanged(bookmark);
        }

        private void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            var bookmark = ((ListViewItem)sender).DataContext as Bookmark;
            document_.JumpToBookmark(bookmark);
            bookmarks_.Refresh();
        }

        private void JumpToBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            var bookmark = e.Parameter as Bookmark;
            document_.JumpToBookmark(bookmark);
            bookmarks_.Refresh();
        }

        private void RemoveBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            var bookmark = e.Parameter as Bookmark;
            document_.RemoveBookmark(bookmark);
            bookmarks_.Refresh();
        }

        private void RemoveAllBookmarksExecuted(object sender, ExecutedRoutedEventArgs e) {
            document_.RemoveAllBookmarks();
            bookmarks_.Refresh();
        }

        private void MarkBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            SetSelectedBookmarkStyle(Utils.GetSelectedColorStyle(e.Parameter as ColorEventArgs,
                                                                 Pens.GetPen(Colors.Silver)));
        }

        private void UnmarkBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            SetSelectedBookmarkStyle(null);
        }

        private void SetSelectedBookmarkStyle(HighlightingStyle style) {
            if (!(BookmarkList.SelectedItem is Bookmark bookmark)) {
                return;
            }

            bookmark.Style = style;
            document_.BookmarkInfoChanged(bookmark);
            bookmarks_.Refresh();
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Bookmarks;

        public override void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            InitializeForDocument(document);
            IsPanelEnabled = document_ != null;
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            ResetBookmarks();
        }

        private void ResetBookmarks() {
            bookmarks_ = new ObservableCollectionRefresh<Bookmark>();
            BookmarkList.ItemsSource = bookmarks_;
        }

        public override void OnActivatePanel() {
            // Hack to prevent DropDown in toolbar to get focus 
            // the first time the panel is made visible.
            if (!focusedOnce_) {
                BookmarkList.Focus();
                focusedOnce_ = true;
            }
        }

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            ResetBookmarks();
        }

        #endregion
    }
}
