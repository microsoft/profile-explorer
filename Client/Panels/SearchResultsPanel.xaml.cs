// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Core;

namespace Client {
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
    }

    public class SearchResultInfo {
        private static readonly FontFamily PreviewFont = new FontFamily("Consolas");

        public TextSearchResult Result;

        public SearchResultInfo(int index, TextSearchResult result,
                                IRTextSection section, string sectionText,
                                ICompilerIRInfoProvider compilerInfo) {
            Index = index;
            Result = result;
            Section = section;
            SectionText = sectionText;
            CompilerInfo = compilerInfo;
        }

        public int Index { get; set; }
        public IRTextSection Section { get; set; }
        public string SectionText { get; set; }
        public ICompilerIRInfoProvider CompilerInfo { get; set; }

        private TextBlock preview_;

        public TextBlock Preview {
            get {
                if (preview_ != null) {
                    return preview_;
                }

                preview_ = new TextBlock();
                preview_.FontFamily = PreviewFont;
                preview_.Foreground = Brushes.Black;
                preview_.Margin = new Thickness(0, 2, 0, 0);
                var (startOffset, endOffset) = ExtractTextLine();

                // Append text before search result.
                if (startOffset < Result.Offset) {
                    preview_.Inlines.Add(SectionText.Substring(startOffset, Result.Offset - startOffset));
                }

                // Append search result.
                preview_.Inlines.Add(new Run(SectionText.Substring(Result.Offset, Result.Length)) {
                    FontWeight = FontWeights.Bold,
                    Background = Brushes.Khaki
                });

                // Append text after search result.
                int afterResultOffset = Result.Offset + Result.Length;

                if (endOffset > afterResultOffset) {
                    preview_.Inlines.Add(SectionText.Substring(afterResultOffset, endOffset - afterResultOffset));
                }

                return preview_;
            }
        }

        public string SectionName {
            get {
                var name = CompilerInfo.NameProvider.GetSectionName(Section);
                return $"({Section.Number}) {name}";
            }
        }

        private Brush textColor_;
        public Brush TextColor => textColor_;

        private bool isMarked_;
        public bool IsMarked {
            get {
                if (isMarked_) return true;

                if (CompilerInfo.StyleProvider.IsMarkedSection(Section, out var markedName)) {
                    isMarked_ = true;
                    textColor_ = markedName.TextColor;
                }

                return isMarked_;
            }
        }

        private bool IsNewLine(char value) {
            return value == '\n' || value == '\r';
        }

        private (int, int) ExtractTextLine() {
            int startOffset = Result.Offset;
            int endOffset = Result.Offset + Result.Length;

            while (startOffset > 0) {
                if (IsNewLine(SectionText[startOffset])) {
                    startOffset++; // Don't include the newline itself.
                    break;
                }

                startOffset--;
            }

            while (endOffset < SectionText.Length) {
                if (IsNewLine(SectionText[endOffset])) {
                    endOffset--;
                    break;
                }

                endOffset++;
            }

            return (startOffset, endOffset);
        }
    }

    public partial class SearchResultsPanel : ToolPanelControl {
        private Document.SearchInfo searchInfo_;
        private List<SearchResultInfo> searchResults_;
        private Dictionary<IRTextSection, SectionSearchResult> searchResultsMap_;

        public SearchResultsPanel() {
            InitializeComponent();
        }

        private void ToolBar_Loaded(object sender, System.Windows.RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private async void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            var searchResult = ((ListViewItem)sender).DataContext as SearchResultInfo;
            await JumpToSearchResult(searchResult);
        }

        private async Task JumpToSearchResult(SearchResultInfo resultInfo) {
            var documentHost = Session.FindAssociatedDocumentHost(this);
            var document = documentHost.TextView;

            if (document.Section != resultInfo.Section) {
                var searchResults = searchResultsMap_[resultInfo.Section];
                await documentHost.SwitchSearchResultsAsync(searchResults, resultInfo.Section, searchInfo_);
            }

            documentHost.JumpToSearchResult(resultInfo.Result, resultInfo.Index - 1);
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
                await JumpToSelectedSearchResult();
                e.Handled = true;
            }
        }

        private async void JumpToPreviousExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (ResultList.SelectedIndex > 0) {
                ResultList.SelectedIndex--;
                await JumpToSelectedSearchResult();
                e.Handled = true;
            }
        }

        private async void JumpToNextSectionExecuted(object sender, ExecutedRoutedEventArgs e) {
            int index = ResultList.SelectedIndex;

            if (index < ResultList.Items.Count - 1) {
                IRTextSection startSection = searchResults_[index].Section;

                for (++index; index < ResultList.Items.Count; index++) {
                    IRTextSection section = searchResults_[index].Section;

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
                IRTextSection startSection = searchResults_[index].Section;

                for (--index; index >= 0; index--) {
                    IRTextSection section = searchResults_[index].Section;

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

        #region IToolPanel
        public override ToolPanelKind PanelKind => ToolPanelKind.SearchResults;

        public override void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            //InitializeForDocument(document);
            //bookmarks_ = new ObservableCollectionRefresh<Bookmark>();
            //BookmarkList.ItemsSource = bookmarks_;
            //IsPanelEnabled = document_ != null;
        }

        public override void OnActivatePanel() {

        }

        public void UpdateSearchResults(List<SectionSearchResult> results,
                                        Document.SearchInfo searchInfo) {
            searchResults_ = new List<SearchResultInfo>(8192);
            searchResultsMap_ = new Dictionary<IRTextSection, SectionSearchResult>(results.Count);
            searchInfo_ = searchInfo;

            foreach (var sectionResult in results) {
                searchResultsMap_[sectionResult.Section] = sectionResult;
                var sectionText = sectionResult.SectionText;
                int index = 1;

                foreach (var result in sectionResult.Results) {
                    var resultInfo = new SearchResultInfo(index++, result, sectionResult.Section,
                                                          sectionText, Session.CompilerInfo);
                    searchResults_.Add(resultInfo);
                }
            }

            ResultList.ItemsSource = new ListCollectionView(searchResults_);
            ResultNumberText.Text = searchResults_.Count.ToString();
            SearchedText.Text = searchInfo.SearchedText;

            if (ResultList.Items.Count > 0) {
                ResultList.ScrollIntoView(ResultList.Items[0]);
            }
        }
        #endregion
    }
}
