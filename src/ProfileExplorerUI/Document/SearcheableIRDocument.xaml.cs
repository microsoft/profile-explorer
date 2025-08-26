﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;

namespace ProfileExplorer.UI.Document;

public static class SearcheableIRDocumentCommand {
  public static readonly RoutedUICommand ToggleSearch = new("Untitled", "ToggleSearch", typeof(SearcheableIRDocument));
}

public partial class SearcheableIRDocument : UserControl, INotifyPropertyChanged {
  private bool searchPanelVisible_;
  private List<TextSearchResult> searchResults_;

  public SearcheableIRDocument() {
    InitializeComponent();
    DataContext = this;
    SearchPanel.SearchChanged += SearchPanel_SearchChanged;
    SearchPanel.CloseSearchPanel += SearchPanel_CloseSearchPanel;
    SearchPanel.NavigateToPreviousResult += SearchPanel_NaviateToPreviousResult;
    SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
  }

  public bool SearchPanelVisible {
    get => searchPanelVisible_;
    set {
      if (value != searchPanelVisible_) {
        searchPanelVisible_ = value;

        if (searchPanelVisible_) {
          SearchPanel.Visibility = Visibility.Visible;
          SearchPanel.Show();
        }
        else {
          SearchPanel.Reset();
          SearchPanel.Visibility = Visibility.Collapsed;
        }

        OnPropertyChange("SearchPanelVisible");
      }
    }
  }

  public IUISession Session {
    get => TextView.Session;
    set => TextView.Session = value;
  }

  public bool UseAutoComplete {
    get => SearchPanel.UseAutoComplete;
    set => SearchPanel.UseAutoComplete = value;
  }

  public bool FilterSearchResults {
    get => TextView.SearchMode == LightIRDocument.TextSearchMode.Filter;
    set {
      var prevSearchMode = TextView.SearchMode;

      TextView.SearchMode = value ? LightIRDocument.TextSearchMode.Filter :
        LightIRDocument.TextSearchMode.Mark;

      if (TextView.SearchMode != prevSearchMode) {
        Dispatcher.InvokeAsync(async () => await SearchText());
        OnPropertyChange("FilterSearchResults");
      }
    }
  }

  public event PropertyChangedEventHandler PropertyChanged;

  public void OnPropertyChange(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }

  public async Task SetText(string text, IHighlightingDefinition syntaxHighlighting = null) {
    await TextView.SwitchText(text);
    TextView.SyntaxHighlighting = syntaxHighlighting;
  }

  public async Task SetText(string text, FunctionIR function, IRTextSection section,
                            IRDocument associatedDocument, IUISession session) {
    TextView.Session = session;
    await TextView.SwitchText(text, function, section, associatedDocument);
  }

  public void SelectText(int offset, int length, int line) {
    TextView.Select(offset, length);
    TextView.ScrollToLine(line);
  }

  public void ScrollToEnd() {
    TextView.ScrollToEnd();
  }

  private async Task SearchText(SearchInfo info = null) {
    if (info == null) {
      if (searchPanelVisible_) {
        info = SearchPanel.SearchInfo;
      }
      else {
        //? TODO: Should rather be an assert
#if DEBUG
        MessageBox.Show("SearchText without searchPanelVisible_, attach debugger");
        Utils.WaitForDebugger();
#endif
        return;
      }
    }

    searchResults_ = await TextView.SearchText(info);

    if (searchResults_ != null && searchResults_.Count > 0) {
      info.ResultCount = searchResults_.Count;
      TextView.JumpToSearchResult(searchResults_[0]);
    }
  }

  private void SearchPanel_NaviateToPreviousResult(object sender, SearchInfo e) {
    if (searchResults_ == null) {
      return;
    }

    TextView.JumpToSearchResult(searchResults_[e.CurrentResult]);
  }

  private void SearchPanel_NavigateToNextResult(object sender, SearchInfo e) {
    if (searchResults_ == null) {
      return;
    }

    TextView.JumpToSearchResult(searchResults_[e.CurrentResult]);
  }

  private void SearchPanel_CloseSearchPanel(object sender, SearchInfo e) {
    SearchPanelVisible = false;
  }

  private async void SearchPanel_SearchChanged(object sender, SearchInfo e) {
    await SearchText(e);
  }

  private void ToggleSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
    SearchPanelVisible = !SearchPanelVisible;
  }
}