// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRExplorerUI.Document;

public static class SearchCommand {
  public static readonly RoutedUICommand PreviousResult =
    new RoutedUICommand("Untitled", "PreviousResult", typeof(SearchPanel));
  public static readonly RoutedUICommand NextResult =
    new RoutedUICommand("Untitled", "NextResult", typeof(SearchPanel));
  public static readonly RoutedUICommand ClearText =
    new RoutedUICommand("Untitled", "ClearText", typeof(SearchPanel));
  public static readonly RoutedUICommand ToggleCaseSensitive =
    new RoutedUICommand("Untitled", "ToggleCaseSensitive", typeof(SearchPanel));
  public static readonly RoutedUICommand ToggleWholeWord =
    new RoutedUICommand("Untitled", "ToggleWholeWord", typeof(SearchPanel));
  public static readonly RoutedUICommand ToggleRegex =
    new RoutedUICommand("Untitled", "ToggleRegex", typeof(SearchPanel));
  public static readonly RoutedUICommand ToggleSearchAll =
    new RoutedUICommand("Untitled", "ToggleSearchAll", typeof(SearchPanel));
}

public partial class SearchPanel : UserControl {
  private SearchInfo searchInfo_;
  private bool selectTextOnFocus_;

  public SearchPanel() {
    InitializeComponent();
    UseAutoComplete = true;

    // The AutoCompleteBox has no SelectAll method, get the underlying TextBox
    // to do that when it gets focus.
    var textBox = Utils.FindChild<TextBox>(TextSearch);

    if (textBox != null) {
      textBox.GotFocus += TextBox_GotFocus;
    }
  }

  public event EventHandler<SearchInfo> SearchChanged;
  public event EventHandler<SearchInfo> NavigateToNextResult;
  public event EventHandler<SearchInfo> NavigateToPreviousResult;
  public event EventHandler<SearchInfo> CloseSearchPanel;
  public SearchInfo SearchInfo => searchInfo_;
  public bool UseAutoComplete { get; set; }

  public void Show(SearchInfo initialInfo = null, bool searchAll = false,
                   bool selectTextOnFocus = false) {
    Reset(initialInfo, searchAll);
    selectTextOnFocus_ = selectTextOnFocus;
    Keyboard.Focus(TextSearch);
  }

  public void Hide() {
    if (UseAutoComplete) {
      string text = TextSearch.Text;

      if (text.Trim().Length > 0) {
        App.Settings.AddRecentTextSearch(text);
      }
    }
  }

  public void Reset(SearchInfo initialInfo = null, bool searchAll = false) {
    if (searchInfo_ != null) {
      searchInfo_.PropertyChanged -= SearchInfo__PropertyChanged;

      if (initialInfo == null) {
        var temp = new SearchInfo();
        temp.SearchAll = searchInfo_.SearchAll;
        temp.SearchKind = searchInfo_.SearchKind;
        searchInfo_ = temp;
      }
    }

    if (initialInfo != null) {
      searchInfo_ = initialInfo;
    }
    else if (searchInfo_ == null) {
      searchInfo_ = new SearchInfo();
      searchInfo_.SearchAll = searchAll;
    }

    DataContext = searchInfo_;
    searchInfo_.PropertyChanged += SearchInfo__PropertyChanged;

    if (initialInfo == null || initialInfo.ResultCount == 0) {
      SearchChanged?.Invoke(this, searchInfo_);
    }
  }

  private void TextBox_GotFocus(object sender, RoutedEventArgs e) {
    if (selectTextOnFocus_) {
      ((TextBox)sender).SelectAll();
      selectTextOnFocus_ = false;
    }
  }

  private void SearchInfo__PropertyChanged(object sender, PropertyChangedEventArgs e) {
    if (e.PropertyName != "ResultText") {
      searchInfo_.CurrentResult = 0;
      searchInfo_.ResultCount = 0;
      SearchChanged?.Invoke(this, searchInfo_);
    }
  }

  private void PreviousResultExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (searchInfo_.CurrentResult > 0) {
      searchInfo_.CurrentResult--;
      NavigateToPreviousResult?.Invoke(this, searchInfo_);
    }
  }

  private void NextResultExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (searchInfo_.CurrentResult < searchInfo_.ResultCount - 1) {
      searchInfo_.CurrentResult++;
      NavigateToNextResult?.Invoke(this, searchInfo_);
    }
  }

  private void PreviousResultCanExecute(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = NavigateToPreviousResult != null;
  }

  private void NextResultCanExecute(object sender, CanExecuteRoutedEventArgs e) {
    e.CanExecute = NavigateToNextResult != null;
  }

  private void ClearTextExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (TextSearch.Text == "") {
      CloseSearchPanel?.Invoke(this, searchInfo_);
    }
    else {
      ClearSearchedText();
    }
  }

  private void ClearSearchedText() {
    TextSearch.Text = "";
    Reset();
  }

  private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
    Utils.PatchToolbarStyle(sender as ToolBar);
  }

  private void TextSearch_Loaded(object sender, RoutedEventArgs e) {
    Keyboard.Focus((AutoCompleteBox)sender);
  }

  private void TextSearch_Populating(object sender, PopulatingEventArgs e) {
    if (UseAutoComplete) {
      var box = (AutoCompleteBox)sender;
      box.ItemsSource = null;
      box.ItemsSource = App.Settings.RecentTextSearches;
      box.PopulateComplete();
    }
  }

  private void ToggleCaseSensitiveExecuted(object sender, ExecutedRoutedEventArgs e) {
    searchInfo_.IsCaseInsensitive = !searchInfo_.IsCaseInsensitive;
  }

  private void ToggleWholeWordExecuted(object sender, ExecutedRoutedEventArgs e) {
    searchInfo_.IsWholeWord = !searchInfo_.IsWholeWord;
  }

  private void ToggleRegexExecuted(object sender, ExecutedRoutedEventArgs e) {
    searchInfo_.IsRegex = !searchInfo_.IsRegex;
  }

  private void ToggleSearchAllExecuted(object sender, ExecutedRoutedEventArgs e) {
    searchInfo_.SearchAll = !searchInfo_.SearchAll;
  }
}
