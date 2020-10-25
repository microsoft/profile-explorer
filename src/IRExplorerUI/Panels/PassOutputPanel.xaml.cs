// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRExplorerUI.Document;
using IRExplorerCore;
using ProtoBuf;
using IRExplorerCore.IR;

namespace IRExplorerUI {
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
            SearchPanel.NavigateToPreviousResult += SearchPanel_NaviateToPreviousResult;
            SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
        }

        public bool ShowAfterOutput {
            get => showAfterOutput_;
            set {
                if (showAfterOutput_ != value) {
                    showAfterOutput_ = value;
                    OnPropertyChange(nameof(ShowAfterOutput));
                    OnPropertyChange(nameof(ShowBeforeOutput));
                }
            }
        }

        public bool ShowBeforeOutput {
            get => !showAfterOutput_;
            set {
                if (showAfterOutput_ == value) {
                    showAfterOutput_ = !value;
                    OnPropertyChange(nameof(ShowAfterOutput));
                    OnPropertyChange(nameof(ShowBeforeOutput));
                }
            }
        }

        public string SectionName => TextView.Section != null ?
            Session.CompilerInfo.NameProvider.GetSectionName(TextView.Section) : "";

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

        private async void ToggleOutputExecuted(object sender, ExecutedRoutedEventArgs e) {
            ShowAfterOutput = !ShowAfterOutput;
            await SwitchText(TextView.Section, TextView.Function, TextView.AssociatedDocument);
        }

        private void ToggleSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
            SearchPanelVisible = !SearchPanelVisible;
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
        }

        public override ISession Session {
            get => TextView.Session;
            set => TextView.Session = value;
        }

        public override bool HasPinnedContent {
            get => FixedToolbar.IsPinned;
            set => FixedToolbar.IsPinned = value;
        }

        public override async void ClonePanel(IToolPanel basePanel) {
            var otherPanel = (PassOutputPanel)basePanel;
            await SwitchText(otherPanel.TextView.Section, otherPanel.TextView.Function,
                             otherPanel.TextView.AssociatedDocument);
        }

        public override async void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            if (HasPinnedContent) {
                return;
            }

            var data = Session.LoadPanelState(this, section);

            if (data != null) {
                var state = StateSerializer.Deserialize<PassOutputPanelState>(data, document.Function);
                await SwitchText(section, document.Function, document);
                ShowAfterOutput = state.ShowAfterOutput;
            }
            else {
                await SwitchText(section, document.Function, document);
                await TextView.SearchText(new SearchInfo());
            }
        }

        private async Task SwitchText(IRTextSection section, FunctionIR function, IRDocument associatedDocument) {
            if (ShowAfterOutput) {
                initialText_ = await Session.GetSectionOutputTextAsync(section.OutputAfter, section);
            }
            else {
                initialText_ = await Session.GetSectionOutputTextAsync(section.OutputBefore, section);
            }

            await TextView.SwitchText(initialText_, function, section, associatedDocument);
            await SearchText();
            OnPropertyChange(nameof(SectionName));
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            if (HasPinnedContent) {
                return;
            }

            SaveState(section, document);
            ResetOutputPanel();
        }

        private void ResetOutputPanel() {
            SearchPanelVisible = false;
            initialText_ = null;
            searchResults_ = null;
            TextView.UnloadDocument();
            OnPropertyChange(nameof(SectionName));
        }

        public override void OnSessionSave() {
            var document = Session.FindAssociatedDocument(this);

            if (document != null) {
                SaveState(document.Section, document);
            }
        }

        private void SaveState(IRTextSection section, IRDocument document) {
            var state = new PassOutputPanelState();
            state.ShowAfterOutput = ShowAfterOutput;
            var data = StateSerializer.Serialize(state, document.Function);
            Session.SavePanelState(data, this, section);
        }

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            ResetOutputPanel();
        }

        #endregion

        private void FixedToolbar_BindMenuItemSelected(object sender, BindMenuItem e) {
            Session.BindToDocument(this, e);
        }

        private void FixedToolbar_BindMenuOpen(object sender, BindMenuItemsArgs e) {
            Session.PopulateBindMenu(this, e);
        }

        private void FixedToolbar_SettingsClicked(object sender, System.EventArgs e) {
            MessageBox.Show("TODO");
        }

        private async void AfterButton_Click(object sender, RoutedEventArgs e) {
            ShowAfterOutput = true;
            await SwitchText(TextView.Section, TextView.Function, TextView.AssociatedDocument);
        }

        private async void BeforeButton_Click(object sender, RoutedEventArgs e) {
            ShowBeforeOutput = true;
            await SwitchText(TextView.Section, TextView.Function, TextView.AssociatedDocument);
        }
    }
}
