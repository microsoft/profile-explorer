using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Highlighting;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI.Document {
    /// <summary>
    /// Interaction logic for SearcheableIRDocument.xaml
    /// </summary>
    /// 
    public static class SearcheableIRDocumentCommand {
        public static readonly RoutedUICommand ToggleSearch =
            new RoutedUICommand("Untitled", "ToggleSearch", typeof(SearcheableIRDocument));
    }

    public partial class SearcheableIRDocument : UserControl, INotifyPropertyChanged {
        private bool searchPanelVisible_;
        private List<TextSearchResult> searchResults_;

        public ISession Session {
            get => TextView.Session;
            set => TextView.Session = value;
        }

        public string Text {
            get => TextView.Text;
            set => TextView.Text = value;
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

        public SearcheableIRDocument() {
            InitializeComponent();
            DataContext = this;
            SearchPanel.SearchChanged += SearchPanel_SearchChanged;
            SearchPanel.CloseSearchPanel += SearchPanel_CloseSearchPanel;
            SearchPanel.NavigateToPreviousResult += SearchPanel_NaviateToPreviousResult;
            SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
        }

        public bool UseAutoComplete {
            get => SearchPanel.UseAutoComplete;
            set => SearchPanel.UseAutoComplete = value;
        }

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        public event PropertyChangedEventHandler PropertyChanged;

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

        public IHighlightingDefinition SyntaxHighlighting {
            get => TextView.SyntaxHighlighting;
            set => TextView.SyntaxHighlighting = value;
        }

        public async Task SearchText(SearchInfo info = null) {
            if (info == null) {
                if (searchPanelVisible_) {
                    info = SearchPanel.SearchInfo;
                }
                else {
                    //? TODO: Should rather be an assert
                    MessageBox.Show("SearchText without searchPanelVisible_, attach debugger");
                    Utils.WaitForDebugger();
                    return;
                }
            }

            searchResults_ = await TextView.SearchText(info);

            if (searchResults_ != null && searchResults_.Count > 0) {
                info.ResultCount = searchResults_.Count;
                TextView.JumpToSearchResult(searchResults_[0]);
            }
        }

        public async Task ResetTextSearch() {
            await SearchText(new SearchInfo());
        }

        public async Task SetText(string text, FunctionIR function, IRTextSection section,
                                  IRDocument associatedDocument) {
            await TextView.SwitchText(text, function, section, associatedDocument);
        }

        public void SelectText(int offset, int length, int line) {
            TextView.Select(offset, length);
            TextView.ScrollToLine(line);
        }

        public void UnloadDocument() {
            TextView.UnloadDocument();
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
}
