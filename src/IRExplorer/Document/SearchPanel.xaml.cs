// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IRExplorer.Document {
    public class SearchInfo : INotifyPropertyChanged {
        private int currentResult_;
        private TextSearchKind kind_;
        private int resultCount_;
        private bool searchAllEnabled_;
        private bool searchAll_;
        private string text_;

        public SearchInfo() {
            text_ = string.Empty;
            kind_ = TextSearchKind.Default;
        }

        public TextSearchKind SearchKind {
            get => kind_;
            set => kind_ = value;
        }

        public string SearchedText {
            get => text_;
            set {
                if (value != text_) {
                    text_ = value;
                    OnPropertyChange("SearchedText");
                }
            }
        }

        public bool IsCaseInsensitive {
            get => kind_.HasFlag(TextSearchKind.CaseInsensitive);
            set {
                if (value) {
                    kind_ |= TextSearchKind.CaseInsensitive;
                }
                else {
                    kind_ &= ~TextSearchKind.CaseInsensitive;
                }

                OnPropertyChange("IsCaseInsensitive");
            }
        }

        public bool IsWholeWord {
            get => kind_.HasFlag(TextSearchKind.WholeWord);
            set {
                if (value) {
                    kind_ |= TextSearchKind.WholeWord;
                }
                else {
                    kind_ &= ~TextSearchKind.WholeWord;
                }

                OnPropertyChange("IsWholeWord");
            }
        }

        public bool IsRegex {
            get => kind_.HasFlag(TextSearchKind.Regex);
            set {
                if (value) {
                    kind_ |= TextSearchKind.Regex;
                }
                else {
                    kind_ &= ~TextSearchKind.Regex;
                }

                OnPropertyChange("IsRegex");
            }
        }

        public bool SearchAll {
            get => searchAll_ && searchAllEnabled_;
            set {
                if (value != searchAll_) {
                    searchAll_ = value;
                    OnPropertyChange("SearchAll");
                }
            }
        }

        public bool SearchAllEnabled {
            get => searchAllEnabled_;
            set {
                if (value != searchAllEnabled_) {
                    searchAllEnabled_ = value;
                    OnPropertyChange("SearchAll");
                }
            }
        }

        public int ResultCount {
            get => resultCount_;
            set {
                if (resultCount_ != value) {
                    resultCount_ = value;
                    OnPropertyChange("ResultText");
                }
            }
        }

        public int CurrentResult {
            get => currentResult_;
            set {
                if (currentResult_ != value) {
                    currentResult_ = value;
                    OnPropertyChange("ResultText");
                }
            }
        }

        public string ResultText => $"{(resultCount_ > 0 ? currentResult_ + 1 : 0)} / {resultCount_}";

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }
    }

    public static class SearchCommand {
        public static readonly RoutedUICommand PreviousResult =
            new RoutedUICommand("Untitled", "PreviousResult", typeof(SearchPanel));
        public static readonly RoutedUICommand NextResult =
            new RoutedUICommand("Untitled", "NextResult", typeof(SearchPanel));
        public static readonly RoutedUICommand ClearText =
            new RoutedUICommand("Untitled", "ClearText", typeof(SearchPanel));
    }

    public partial class SearchPanel : UserControl {
        private SearchInfo searchInfo_;

        public SearchPanel() {
            InitializeComponent();
        }

        public SearchInfo SearchInfo => searchInfo_;

        public event EventHandler<SearchInfo> SearchChanged;
        public event EventHandler<SearchInfo> NavigateToNextResult;
        public event EventHandler<SearchInfo> NaviateToPreviousResult;
        public event EventHandler<SearchInfo> CloseSearchPanel;

        public void Show(SearchInfo initialInfo = null, bool searchAll = false) {
            Reset(initialInfo, searchAll);
            Keyboard.Focus(TextSearch);
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
                NaviateToPreviousResult?.Invoke(this, searchInfo_);
            }
        }

        private void NextResultExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (searchInfo_.CurrentResult < searchInfo_.ResultCount - 1) {
                searchInfo_.CurrentResult++;
                NavigateToNextResult?.Invoke(this, searchInfo_);
            }
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
            Keyboard.Focus((TextBox) sender);
        }
    }
}
