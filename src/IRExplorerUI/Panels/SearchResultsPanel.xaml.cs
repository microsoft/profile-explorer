// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerUI.Document;
using IRExplorerCore;
using System.ComponentModel;

namespace IRExplorerUI {
    public static class SearchResultsCommand {
        public static readonly RoutedUICommand JumpToNext =
            new RoutedUICommand("Untitled", "JumpToNext", typeof(SearchResultsPanel));
        public static readonly RoutedUICommand JumpToPrevious =
            new RoutedUICommand("Untitled", "JumpToPrevious", typeof(SearchResultsPanel));
        public static readonly RoutedUICommand JumpToNextSection =
            new RoutedUICommand("Untitled", "JumpToNextSection", typeof(SearchResultsPanel));
        public static readonly RoutedUICommand JumpToPreviousSection =
            new RoutedUICommand("Untitled", "JumpToPreviousSection", typeof(SearchResultsPanel));
        public static readonly RoutedUICommand JumpToSelected =
            new RoutedUICommand("Untitled", "JumpToSelected", typeof(SearchResultsPanel));
        public static readonly RoutedUICommand OpenInNewTab =
            new RoutedUICommand("Open section in new tab", "OpenInNewTab", typeof(SearchResultsPanel));
        public static readonly RoutedUICommand OpenLeft =
            new RoutedUICommand("Open section in new tab", "OpenLeft", typeof(SearchResultsPanel));
        public static readonly RoutedUICommand OpenRight =
            new RoutedUICommand("Open section in new tab", "OpenRight", typeof(SearchResultsPanel));
    }

    public class SearchResultInfo {
        public enum SearchResultKind {
            SectionResult,
            BeforeOutputResult,
            AfterOutputResult
        }

        public delegate string SectionTextDelegate(SearchResultKind resultKind, IRTextSection section);

        private static readonly FontFamily PreviewFont = new FontFamily("Consolas");
        private bool isMarked_;
        private TextBlock preview_;
        private Brush textColor_;
        private SectionTextDelegate getSectionText_;

        public SearchResultInfo(SearchResultKind resultkind, int index, TextSearchResult result,
                                IRTextSection section, ISession session,
                                SectionTextDelegate getSectionText) {
            ResultKind = resultkind;
            Index = index;
            Result = result;
            Section = section;
            Session = session;
            getSectionText_ = getSectionText;
        }

        public SearchResultKind ResultKind { get; set; }
        public int Index { get; set; }
        public TextSearchResult Result { get; set; }
        public IRTextSection Section { get; set; }
        public ISession Session { get; set; }

        public TextBlock Preview {
            get {
                if (preview_ != null) {
                    return preview_;
                }

                preview_ = new TextBlock();
                preview_.FontFamily = PreviewFont;
                preview_.Foreground = Brushes.Black;
                preview_.Margin = new Thickness(0, 2, 0, 0);

                // Load text on-demand and extract the line with the result.
                var sectionText = getSectionText_(ResultKind, Section);
                (int startOffset, int endOffset) = ExtractTextLine(sectionText);

                // Append text before search result.
                if (startOffset < Result.Offset) {
                    preview_.Inlines.Add(sectionText.Substring(startOffset, Result.Offset - startOffset));
                }

                // Append search result.
                preview_.Inlines.Add(new Run(sectionText.Substring(Result.Offset, Result.Length)) {
                    FontWeight = FontWeights.Bold,
                    Background = Brushes.Khaki
                });

                // Append text after search result.
                int afterResultOffset = Result.Offset + Result.Length;

                if (endOffset > afterResultOffset) {
                    preview_.Inlines.Add(sectionText.Substring(afterResultOffset, endOffset - afterResultOffset));
                }

                return preview_;
            }
        }

        public string SectionName {
            get {
                string name = Session.CompilerInfo.NameProvider.GetSectionName(Section);
                return $"({Section.Number}) {name}";
            }
        }

        public Brush TextColor => textColor_;

        public bool IsMarked {
            get {
                if (isMarked_) {
                    return true;
                }

                if (Session.CompilerInfo.SectionStyleProvider.IsMarkedSection(Section, out var markedName)) {
                    isMarked_ = true;
                    textColor_ = ColorBrushes.GetBrush(markedName.TextColor);
                }

                return isMarked_;
            }
        }

        private bool IsNewLine(char value) {
            return value == '\n' || value == '\r';
        }

        private (int, int) ExtractTextLine(string sectionText) {
            // Extract the whole line containing the search result.
            int startOffset = Result.Offset;
            int endOffset = Result.Offset + Result.Length;

            while (startOffset > 0) {
                if (IsNewLine(sectionText[startOffset])) {
                    startOffset++; // Don't include the newline itself.
                    break;
                }

                startOffset--;
            }

            while (endOffset < sectionText.Length) {
                if (IsNewLine(sectionText[endOffset])) {
                    endOffset--;
                    break;
                }

                endOffset++;
            }

            return (startOffset, endOffset);
        }
    }

    public partial class SearchResultsPanel : ToolPanelControl, INotifyPropertyChanged {
        private SearchInfo searchInfo_;
        private List<SearchResultInfo> searchResults_;
        private Dictionary<IRTextSection, SectionSearchResult> searchResultsMap_;
        private IRTextSection previousSection_;
        private string previousSectionText_;
        private string previousSectionBeforeOutput_;
        private string previousSectionAfterOutput_;
        private bool hideToolbarTray_;
        private bool hideSearchedText_;
        private string optionalText_;

        public event EventHandler<OpenSectionEventArgs> OpenSection;
        public event PropertyChangedEventHandler PropertyChanged;

        public SearchResultsPanel() {
            InitializeComponent();
            DataContext = this;
        }

        public bool HideToolbarTray {
            get => hideToolbarTray_;
            set {
                if (hideToolbarTray_ != value) {
                    hideToolbarTray_ = value;
                    NotifyPropertyChanged(nameof(HideToolbarTray));
                }
            }
        }

        public bool HideSearchedText {
            get => hideSearchedText_;
            set {
                if (hideSearchedText_ != value) {
                    hideSearchedText_ = value;
                    NotifyPropertyChanged(nameof(HideSearchedText));
                }
            }
        }

        public string OptionalText {
            get => optionalText_;
            set {
                if (optionalText_ != value) {
                    optionalText_ = value;
                    NotifyPropertyChanged(nameof(OptionalText));
                }
            }
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private async void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            var searchResult = ((ListViewItem)sender).DataContext as SearchResultInfo;
            await JumpToSearchResult(searchResult);
        }

        bool switching;

        private async Task JumpToSearchResult(SearchResultInfo result) {
            //? TODO: If the document is in the middle of switching a section
            //? from the previous jump, this must wait for it to complete, otherwise
            //? the document text can get out of sync and assert.
            if (switching) {
                throw new Exception("Switching...");

            }

            switching = true;
            var documentHost = Session.FindAssociatedDocumentHost(this);

            if (documentHost == null) {
                // DOcument may have been closed in the meantime.
                var args = new OpenSectionEventArgs(result.Section, OpenSectionKind.NewTabDockLeft);
                await Session.SwitchDocumentSectionAsync(args, args.TargetDocument?.TextView);
                documentHost = Session.FindAssociatedDocumentHost(this);
            }

            if (!documentHost.HasSameSearchResultSection(result.Section)) {
                var searchResults = searchResultsMap_[result.Section];
                await documentHost.SwitchSearchResultsAsync(searchResults, result.Section, searchInfo_);
            }

            documentHost.JumpToSearchResult(result.Result, result.Index - 1);
            switching = false;
        }

        private async Task JumpToSelectedSearchResult() {
            await JumpToSearchResult(ResultList.Items[ResultList.SelectedIndex] as SearchResultInfo);
        }

        private async void JumpToSelectedExecuted(object sender, ExecutedRoutedEventArgs e) {
            var result = e.Parameter as SearchResultInfo;
            await JumpToSearchResult(result);
            e.Handled = true;
        }

        private async void JumpToNextExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (ResultList.SelectedIndex < ResultList.Items.Count - 1) {
                ResultList.SelectedIndex++;
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                await JumpToSelectedSearchResult();
                e.Handled = true;
            }
        }

        private async void JumpToPreviousExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (ResultList.SelectedIndex > 0) {
                ResultList.SelectedIndex--;
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                await JumpToSelectedSearchResult();
                e.Handled = true;
            }
        }

        private async void JumpToNextSectionExecuted(object sender, ExecutedRoutedEventArgs e) {
            int index = ResultList.SelectedIndex;

            if (index < 0) {
                index = 0;
            }

            if (index < ResultList.Items.Count - 1) {
                var startSection = searchResults_[index].Section;

                for (++index; index < ResultList.Items.Count; index++) {
                    var section = searchResults_[index].Section;

                    if (section != startSection) {
                        ResultList.SelectedItem = searchResults_[index];
                        ResultList.ScrollIntoView(ResultList.SelectedItem);
                        await JumpToSelectedSearchResult();
                        e.Handled = true;
                        break;
                    }
                }
            }
        }

        private async void JumpToPreviousSectionExecuted(object sender, ExecutedRoutedEventArgs e) {
            int index = ResultList.SelectedIndex;

            if (index > 0) {
                var startSection = searchResults_[index].Section;

                for (--index; index >= 0; index--) {
                    var section = searchResults_[index].Section;

                    if (section != startSection) {
                        ResultList.SelectedItem = searchResults_[index];
                        ResultList.ScrollIntoView(ResultList.SelectedItem);
                        await JumpToSelectedSearchResult();
                        e.Handled = true;
                        break;
                    }
                }
            }
        }

        private async void OpenInNewTabExecuted(object sender, ExecutedRoutedEventArgs e) {
            var result = e.Parameter as SearchResultInfo;

            if (result != null) {
                OpenSection?.Invoke(this, new OpenSectionEventArgs(result.Section, OpenSectionKind.NewTab));
                await JumpToSearchResult(result);
            }
        }

        private async void OpenLeftExecuted(object sender, ExecutedRoutedEventArgs e) {
            var result = e.Parameter as SearchResultInfo;

            if (result != null) {
                OpenSection?.Invoke(this, new OpenSectionEventArgs(result.Section, OpenSectionKind.NewTabDockLeft));
                await JumpToSearchResult(result);
            }
        }

        private async void OpenRightExecuted(object sender, ExecutedRoutedEventArgs e) {
            var result = e.Parameter as SearchResultInfo;

            if (result != null) {
                OpenSection?.Invoke(this, new OpenSectionEventArgs(result.Section, OpenSectionKind.NewTabDockRight));
                await JumpToSearchResult(result);
            }
        }

        public void NotifyPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.SearchResults;

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            base.OnDocumentSectionUnloaded(section, document);
            ResetSectionTextCache();
        }

        public void UpdateSearchResults(List<SectionSearchResult> results, SearchInfo searchInfo) {
            searchResults_ = new List<SearchResultInfo>(8192);
            searchResultsMap_ = new Dictionary<IRTextSection, SectionSearchResult>(results.Count);
            searchInfo_ = searchInfo;

            foreach (var sectionResult in results) {
                searchResultsMap_[sectionResult.Section] = sectionResult;
                int index = 1;

                foreach (var result in sectionResult.Results) {
                    AddSearchResult(sectionResult, index, result,
                                    SearchResultInfo.SearchResultKind.SectionResult);
                    index++;
                }

                if (sectionResult.BeforeOutputResults != null) {
                    foreach (var result in sectionResult.BeforeOutputResults) {
                        AddSearchResult(sectionResult, index, result,
                                        SearchResultInfo.SearchResultKind.BeforeOutputResult);
                        index++;
                    }
                }

                if (sectionResult.AfterOutputResults != null) {
                    foreach (var result in sectionResult.AfterOutputResults) {
                        AddSearchResult(sectionResult, index, result,
                                        SearchResultInfo.SearchResultKind.AfterOutputResult);
                        index++;
                    }
                }
            }

            UpdateResultList(searchInfo.SearchedText, searchResults_);
            ResetSectionTextCache();
        }

        private void AddSearchResult(SectionSearchResult sectionResult, int index, TextSearchResult result,
                                     SearchResultInfo.SearchResultKind resultKind) {
            var resultInfo = new SearchResultInfo(resultKind, index++, result,
                                                  sectionResult.Section, Session, GetSectionText);
            searchResults_.Add(resultInfo);
        }

        private string GetSectionText(SearchResultInfo.SearchResultKind resultKind, IRTextSection section) {
            switch (resultKind) {
                case SearchResultInfo.SearchResultKind.SectionResult: {
                        if (previousSection_ == section &&
                            previousSectionText_ != null) {
                            return previousSectionText_;
                        }

                        var text = Session.GetSectionTextAsync(section).Result;
                        previousSection_ = section;
                        previousSectionText_ = text;
                        return text;
                    }
                case SearchResultInfo.SearchResultKind.BeforeOutputResult: {
                        if (previousSection_ == section &&
                            previousSectionBeforeOutput_ != null) {
                            return previousSectionBeforeOutput_;
                        }

                        var text = Session.GetSectionOutputTextAsync(section.OutputBefore, section).Result;
                        previousSection_ = section;
                        previousSectionBeforeOutput_ = text;
                        return text;
                    }
                case SearchResultInfo.SearchResultKind.AfterOutputResult: {
                        if (previousSection_ == section &&
                            previousSectionAfterOutput_ != null) {
                            return previousSectionAfterOutput_;
                        }

                        var text = Session.GetSectionOutputTextAsync(section.OutputAfter, section).Result;
                        previousSection_ = section;
                        previousSectionAfterOutput_ = text;
                        return text;
                    }
            }

            return string.Empty;
        }

        private void ResetSectionTextCache() {
            previousSection_ = null;
            previousSectionText_ = null;
            previousSectionBeforeOutput_ = null;
            previousSectionAfterOutput_ = null;
        }

        public void ClearSearchResults() {
            UpdateResultList("", new List<SearchResultInfo>());
        }

        private void UpdateResultList(string searchedText, List<SearchResultInfo> searchResults) {
            searchResults_ = searchResults;
            ResultList.ItemsSource = new ListCollectionView(searchResults_);
            ResultNumberText.Text = searchResults_.Count.ToString();
            SearchedText.Text = searchedText;

            if (ResultList.Items.Count > 0) {
                ResultList.ScrollIntoView(ResultList.Items[0]);
            }
        }

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            ClearSearchResults();
        }

        #endregion
    }
}
