// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerUI.Controls;

namespace IRExplorerUI;

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
  private IRDocumentPopupInstance previewPopup_;

  public BookmarksPanel() {
    InitializeComponent();
    ResetBookmarks();
    SetupPreviewPopup();
  }

  public ObservableCollectionRefresh<Bookmark> Bookmarks => bookmarks_;

  public void InitializeForDocument(IRDocument document) {
    Document = document;
  }

  private void TextBox_TextChanged(object sender, TextChangedEventArgs e) {
    var bookmark = ((TextBox)sender).DataContext as Bookmark;
    Document.BookmarkInfoChanged(bookmark);
  }

  private void SetupPreviewPopup() {
    if (previewPopup_ != null) {
      previewPopup_.UnregisterHoverEvents();
      previewPopup_ = null;
    }

    previewPopup_ =
      new IRDocumentPopupInstance(App.Settings.GetElementPreviewPopupSettings(ToolPanelKind.Bookmarks), Session);
    previewPopup_.SetupHoverEvents(BookmarkList, HoverPreview.HoverDuration, () => {
      var hoveredItem = Utils.FindPointedListViewItem(BookmarkList);

      if (hoveredItem?.DataContext is Bookmark bookmark) {
        return PreviewPopupArgs.ForDocument(Document, bookmark.Element, BookmarkList,
                                            $"Bookmark {bookmark.Text}");
      }

      return null;
    });
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
    Document.BookmarkInfoChanged(bookmark);
  }

  private void CheckBox_Unchecked(object sender, RoutedEventArgs e) {
    var bookmark = ((CheckBox)sender).DataContext as Bookmark;
    Document.BookmarkInfoChanged(bookmark);
  }

  private void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
    var bookmark = ((ListViewItem)sender).DataContext as Bookmark;
    Document.JumpToBookmark(bookmark);
    bookmarks_.Refresh();
  }

  private void JumpToBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
    var bookmark = e.Parameter as Bookmark;
    Document.JumpToBookmark(bookmark);
    bookmarks_.Refresh();
  }

  private void RemoveBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
    var bookmark = e.Parameter as Bookmark;
    Document.RemoveBookmark(bookmark);
    bookmarks_.Refresh();
  }

  private void RemoveAllBookmarksExecuted(object sender, ExecutedRoutedEventArgs e) {
    Document.RemoveAllBookmarks();
    bookmarks_.Refresh();
  }

  private void MarkBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
    SetSelectedBookmarkStyle(Utils.GetSelectedColorStyle(e.Parameter as SelectedColorEventArgs,
                                                         ColorPens.GetPen(Colors.Silver)));
  }

  private void UnmarkBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
    SetSelectedBookmarkStyle(null);
  }

  private void SetSelectedBookmarkStyle(HighlightingStyle style) {
    if (!(BookmarkList.SelectedItem is Bookmark bookmark)) {
      return;
    }

    bookmark.Style = style;
    Document.BookmarkInfoChanged(bookmark);
    bookmarks_.Refresh();
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

        #region IToolPanel

  public override ToolPanelKind PanelKind => ToolPanelKind.Bookmarks;

  public override async Task OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
    InitializeForDocument(document);
    IsPanelEnabled = Document != null;
  }

  public override async Task OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
    ResetBookmarks();
  }

  private void ResetBookmarks() {
    bookmarks_ = new ObservableCollectionRefresh<Bookmark>();
    BookmarkList.ItemsSource = bookmarks_;
  }

  public override void OnSessionEnd() {
    base.OnSessionEnd();
    ResetBookmarks();
  }

        #endregion
}
