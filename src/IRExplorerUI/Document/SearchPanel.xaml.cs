// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IRExplorerUI.Document {
    public class SearchInfo : INotifyPropertyChanged {
        private int currentResult_;
        private TextSearchKind kind_;
        private int resultCount_;
        private bool searchAllEnabled_;
        private bool searchAll_;
        private string searchedText_;
        private bool showSearchAllButton_;
        private bool showNavigationSection_;
        private Color borderColor_;

        public SearchInfo() {
            searchedText_ = string.Empty;
            kind_ = TextSearchKind.CaseInsensitive;
            showSearchAllButton_ = true;
            searchAllEnabled_ = true;
            showNavigationSection_ = true;
        }

        public TextSearchKind SearchKind {
            get => kind_;
            set => kind_ = value;
        }

        public string SearchedText {
            get => searchedText_;
            set {
                if (value != searchedText_) {
                    searchedText_ = value;
                    OnPropertyChange(nameof(SearchedText));
                }
            }
        }

        public bool HasSearchedText => !string.IsNullOrEmpty(searchedText_);

        public bool IsCaseInsensitive {
            get => kind_.HasFlag(TextSearchKind.CaseInsensitive);
            set {
                if (SetKindFlag(TextSearchKind.CaseInsensitive, value)) {
                    OnPropertyChange(nameof(IsCaseInsensitive));
                }
            }
        }

        public bool IsWholeWord {
            get => kind_.HasFlag(TextSearchKind.WholeWord);
            set {
                if (SetKindFlag(TextSearchKind.WholeWord, value)) {
                    OnPropertyChange(nameof(IsWholeWord));
                }
            }
        }

        public bool IsRegex {
            get => kind_.HasFlag(TextSearchKind.Regex);
            set {
                if (SetKindFlag(TextSearchKind.Regex, value)) {
                    OnPropertyChange(nameof(IsRegex));
                }
            }
        }

        public bool SearchAll {
            get => searchAll_ && searchAllEnabled_;
            set {
                if (value != searchAll_) {
                    searchAll_ = value;
                    OnPropertyChange(nameof(SearchAll));
                }
            }
        }

        public bool SearchAllEnabled {
            get => searchAllEnabled_;
            set {
                if (value != searchAllEnabled_) {
                    searchAllEnabled_ = value;
                    OnPropertyChange(nameof(SearchAllEnabled));
                }
            }
        }

        public int ResultCount {
            get => resultCount_;
            set {
                if (resultCount_ != value) {
                    resultCount_ = value;
                    OnPropertyChange(nameof(ResultText));
                }
            }
        }

        public int CurrentResult {
            get => currentResult_;
            set {
                if (currentResult_ != value) {
                    currentResult_ = value;
                    OnPropertyChange(nameof(ResultText));
                }
            }
        }

        public string ResultText => $"{(resultCount_ > 0 ? currentResult_ + 1 : 0)} / {resultCount_}";

        public bool ShowSearchAllButton {
            get => showSearchAllButton_;
            set {
                if (value != showSearchAllButton_) {
                    showSearchAllButton_ = value;
                    OnPropertyChange(nameof(ShowSearchAllButton));
                }
            }
        }

        public bool ShowNavigationnSection {
            get => showNavigationSection_;
            set {
                if (value != showNavigationSection_) {
                    showNavigationSection_ = value;
                    OnPropertyChange(nameof(ShowNavigationnSection));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        private bool SetKindFlag(TextSearchKind flag, bool value) {
            if (value && !kind_.HasFlag(flag)) {
                kind_ |= flag;
                return true;
            }
            else if (!value && kind_.HasFlag(flag)) {
                kind_ &= ~flag;
                return true;
            }

            return false;
        }
    }

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

            if(textBox != null) {
                textBox.GotFocus += TextBox_GotFocus;
            }
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e) {
            if(selectTextOnFocus_) {
                ((TextBox)sender).SelectAll();
                selectTextOnFocus_ = false;
            }
        }

        public SearchInfo SearchInfo => searchInfo_;
        public bool UseAutoComplete { get; set; }

        public event EventHandler<SearchInfo> SearchChanged;
        public event EventHandler<SearchInfo> NavigateToNextResult;
        public event EventHandler<SearchInfo> NavigateToPreviousResult;
        public event EventHandler<SearchInfo> CloseSearchPanel;

        public void Show(SearchInfo initialInfo = null, bool searchAll = false,
                         bool selectTextOnFocus = false) {
            Reset(initialInfo, searchAll);
            selectTextOnFocus_ = selectTextOnFocus;
            Keyboard.Focus(TextSearch);
        }

        public void Hide() {
            if (UseAutoComplete) {
                var text = TextSearch.Text;

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
}
