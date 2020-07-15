// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRExplorer.Document;
using IRExplorerCore;
using ProtoBuf;

namespace IRExplorer {
    [ProtoContract]
    public class PassOutputPanelState {
        [ProtoMember(1)]
        public string SearchText;
        [ProtoMember(2)]
        public bool ShowAfterOutput;
    }

    public static class PassOutputPanelCommand {
        public static readonly RoutedUICommand ToggleOutput =
            new RoutedUICommand("Untitled", "ToggleOutput", typeof(PassOutputPanel));
        public static readonly RoutedUICommand ToggleSearch =
            new RoutedUICommand("Untitled", "ToggleSearch", typeof(PassOutputPanel));
    }

    public partial class PassOutputPanel : ToolPanelControl, INotifyPropertyChanged {
        private string initialText_;
        private bool searchPanelVisible_;
        private List<TextSearchResult> searchResults_;
        private bool showAfterOutput_;

        public PassOutputPanel() {
            InitializeComponent();
            DataContext = this;
            SearchPanel.SearchChanged += SearchPanel_SearchChanged;
            SearchPanel.CloseSearchPanel += SearchPanel_CloseSearchPanel;
            SearchPanel.NaviateToPreviousResult += SearchPanel_NaviateToPreviousResult;
            SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
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

        private void PanelToolbarTray_DuplicateClicked(object sender, DuplicateEventArgs e) {
            Session.DuplicatePanel(this, e.Kind);
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private void ToggleOutputExecuted(object sender, ExecutedRoutedEventArgs e) {
            FilterComboBox.SelectedIndex = FilterComboBox.SelectedIndex == 0 ? 1 : 0;
        }

        private void ToggleSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
            SearchPanelVisible = !SearchPanelVisible;
        }

        private async void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!IsPanelEnabled || Session.CurrentDocument == null) {
                return;
            }

            var item = e.AddedItems[0] as ComboBoxItem;
            string kindString = item.Tag as string;

            showAfterOutput_ = kindString switch
            {
                "Before" => false,
                "After" => true,
                _ => showAfterOutput_
            };

            await SwitchText(Session.CurrentDocumentSection, Session.CurrentDocument);
        }

        private void ComboBox_Loaded(object sender, RoutedEventArgs e) {
            if (sender is ComboBox control) {
                Utils.PatchComboBoxStyle(control);
            }
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.PassOutput;
        public event PropertyChangedEventHandler PropertyChanged;

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

        public bool WordWrap {
            get => TextView.WordWrap;
            set {
                TextView.WordWrap = value;
                OnPropertyChange("WordWrap");
            }
        }

        public bool FilterSearchResults {
            get => TextView.SearchMode == LightIRDocument.TextSearchMode.Filter;
            set {
                var prevSearchMode = TextView.SearchMode;

                TextView.SearchMode =
                    value ? LightIRDocument.TextSearchMode.Filter : LightIRDocument.TextSearchMode.Mark;

                if (TextView.SearchMode != prevSearchMode) {
                    Dispatcher.InvokeAsync(async () => await SearchText());
                    OnPropertyChange("FilterSearchResults");
                }
            }
        }

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        public override void OnSessionStart() {
            base.OnSessionStart();
            TextView.Session = Session;
        }

        public override async void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            var data = Session.LoadPanelState(this, section);

            if (data != null) {
                var state = StateSerializer.Deserialize<PassOutputPanelState>(data, document.Function);
                showAfterOutput_ = state.ShowAfterOutput;
                FilterComboBox.SelectedIndex = showAfterOutput_ ? 1 : 0;
                await SwitchText(section, document);
            }
            else {
                await SwitchText(section, document);
                await TextView.SearchText(new SearchInfo());
            }
        }

        private async Task SwitchText(IRTextSection section, IRDocument document) {
            if (showAfterOutput_) {
                initialText_ = await Session.GetSectionPassOutputAsync(section.OutputAfter, section);
            }
            else {
                initialText_ = await Session.GetSectionPassOutputAsync(section.OutputBefore, section);
            }

            await TextView.SwitchText(initialText_, document.Function, section);
            await SearchText();
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            SaveState(section, document);
            ResetOutputPanel();
        }

        private void ResetOutputPanel() {
            SearchPanelVisible = false;
            initialText_ = null;
            searchResults_ = null;
            TextView.UnloadDocument();
        }

        public override void OnSessionSave() {
            var document = Session.FindAssociatedDocument(this);

            if (document != null) {
                SaveState(document.Section, document);
            }
        }

        private void SaveState(IRTextSection section, IRDocument document) {
            var state = new PassOutputPanelState();
            state.ShowAfterOutput = showAfterOutput_;
            var data = StateSerializer.Serialize(state, document.Function);
            Session.SavePanelState(data, this, section);
        }

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            ResetOutputPanel();
        }

        #endregion
    }
}
