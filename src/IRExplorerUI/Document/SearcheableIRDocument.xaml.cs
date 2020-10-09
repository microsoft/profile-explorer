using System;
using System.Collections.Generic;
using System.ComponentModel;
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
            SearchPanel.NaviateToPreviousResult += SearchPanel_NaviateToPreviousResult;
            SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
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

        private async Task SearchText(SearchInfo info = null) {
            if (info == null) {
                if (searchPanelVisible_) {
                    info = SearchPanel.SearchInfo;
                }
                else {
                    return;
                }
            }

            searchResults_ = await TextView.SearchText(info);

            if (searchResults_ != null && searchResults_.Count > 0) {
                info.ResultCount = searchResults_.Count;
                TextView.JumpToSearchResult(searchResults_[0]);
            }
        }

        public void SetText(string text) {
            TextView.Text = text;
        }

        public async Task SetText(string text, FunctionIR function, IRTextSection section,
                                 IRDocument associatedDocument, ISession session) {
            TextView.Session = session;
            await TextView.SwitchText(text, function, section, associatedDocument);
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
