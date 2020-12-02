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
using IRExplorerUI.Diff;
using System.Threading;
using System.Diagnostics;

namespace IRExplorerUI {
    [ProtoContract]
    public class PassOutputPanelState {
        [ProtoMember(1)]
        public SearchInfo SearchInfo;
        [ProtoMember(2)]
        public bool ShowAfterOutput;
        [ProtoMember(3)]
        public bool DiffModeEnabled;
        [ProtoMember(4)]
        public int CaretOffset;
        [ProtoMember(5)]
        public double HorizontalOffset;
        [ProtoMember(6)]
        public double VerticalOffset;
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
        private bool diffModeEnabled_;
        private bool diffModeButtonEnabled_;

        public PassOutputPanel() {
            InitializeComponent();
            DataContext = this;
            SearchPanel.SearchChanged += SearchPanel_SearchChanged;
            SearchPanel.CloseSearchPanel += SearchPanel_CloseSearchPanel;
            SearchPanel.NavigateToPreviousResult += SearchPanel_NaviateToPreviousResult;
            SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
        }

        public event PropertyChangedEventHandler PropertyChanged;

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

        public IRTextSection Section => TextView.Section;

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

                    OnPropertyChange(nameof(SearchPanelVisible));
                }
            }
        }

        public bool WordWrap {
            get => TextView.WordWrap;
            set {
                TextView.WordWrap = value;
                OnPropertyChange(nameof(WordWrap));
            }
        }

        public bool FilterSearchResults {
            get => TextView.SearchMode == LightIRDocument.TextSearchMode.Filter;
            set {
                var prevSearchMode = TextView.SearchMode;
                TextView.SearchMode = value ? LightIRDocument.TextSearchMode.Filter : 
                                              LightIRDocument.TextSearchMode.Mark;
                if (TextView.SearchMode != prevSearchMode) {
                    Dispatcher.InvokeAsync(async () => await SearchText());
                    OnPropertyChange(nameof(FilterSearchResults));
                }
            }
        }

        public bool FilterSearchResultsButtonEnabled => !DiffModeEnabled;

        bool DiffModeEnabled {
            get => diffModeEnabled_;
            set {
                if (value != diffModeEnabled_) {
                    diffModeEnabled_ = value;
                    OnPropertyChange(nameof(DiffModeEnabled));
                    OnPropertyChange(nameof(FilterSearchResultsButtonEnabled));
                }
            }
        }

        bool DiffModeButtonEnabled {
            get => diffModeButtonEnabled_;
            set {
                if(value != diffModeButtonEnabled_) {
                    diffModeButtonEnabled_ = value;
                    OnPropertyChange(nameof(DiffModeButtonEnabled));
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
            ResetTextSearch();
        }

        private async void SearchPanel_SearchChanged(object sender, SearchInfo e) {
            await SearchText(e);
        }

        private void ResetTextSearch() {
            if(searchPanelVisible_) {
                SearchPanelVisible = false;
                TextView.ResetTextSearch();
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

            Document = document;
            DiffModeButtonEnabled = Session.IsInTwoDocumentsDiffMode;
            var data = Session.LoadPanelState(this, section, Document);

            if (data != null) {
                var state = StateSerializer.Deserialize<PassOutputPanelState>(data, document.Function);
                await SwitchText(section, document.Function, document);
                ShowAfterOutput = state.ShowAfterOutput;

                if(state.SearchInfo != null) {
                    // Show search panel and redo the search.
                    SearchPanelVisible = true;
                    SearchPanel.Reset(state.SearchInfo);
                    await SearchText(state.SearchInfo);
                }

                if(state.DiffModeEnabled) {
                    await EnableDiffMode();
                }

                // Restore position in document.
                TextView.TextArea.Caret.Offset = state.CaretOffset;
                TextView.ScrollToVerticalOffset(state.VerticalOffset);
                TextView.ScrollToHorizontalOffset(state.HorizontalOffset);
            }
            else {
                await SwitchText(section, document.Function, document);
                await TextView.SearchText(new SearchInfo());
            }
        }

        private void SaveState(IRTextSection section, IRDocument document) {
            var state = new PassOutputPanelState();
            state.ShowAfterOutput = ShowAfterOutput;
            state.DiffModeEnabled = DiffModeEnabled;
            state.SearchInfo = searchPanelVisible_ ? SearchPanel.SearchInfo : null;
            state.CaretOffset = TextView.TextArea.Caret.Offset;
            state.VerticalOffset = TextView.VerticalOffset;
            state.HorizontalOffset = TextView.HorizontalOffset;
            var data = StateSerializer.Serialize(state, document.Function);
            Session.SavePanelState(data, this, section, Document);
        }

        private IRPassOutput SelectSectionOutput(IRTextSection section) {
            return ShowAfterOutput ? section.OutputAfter : section.OutputBefore;
        }

        private async Task SwitchText(IRTextSection section, FunctionIR function, IRDocument associatedDocument) {
            var output = SelectSectionOutput(section);
            initialText_ = await Session.GetSectionOutputTextAsync(output, section);

            await TextView.SwitchText(initialText_, function, section, associatedDocument);
            await SearchText();
            OnPropertyChange(nameof(SectionName)); // Force update.
        }

        public override async void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            if (HasPinnedContent) {
                return;
            }

            SaveState(section, document);
            await ResetOutputPanel();
            Document = null;
        }

        private async Task ResetOutputPanel() {
            SearchPanelVisible = false;
            initialText_ = null;
            searchResults_ = null;
            await DisableDiffMode();
            TextView.UnloadDocument();
            OnPropertyChange(nameof(SectionName)); // Force update.
        }

        public override void OnSessionSave() {
            var document = Session.FindAssociatedDocument(this);

            if (document != null) {
                SaveState(document.Section, document);
            }
        }

        public override async void OnSessionEnd() {
            base.OnSessionEnd();
            await ResetOutputPanel();
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

        private async Task EnableDiffMode() {
            if (!Session.IsInTwoDocumentsDiffMode) {
                return;
            }

            if (DiffModeEnabled) {
                return;
            }

            // Load the output text of the other diffed section.
            string text = initialText_;
            string otherText;

            if (Section == Session.DiffModeInfo.LeftSection) {
                otherText = await GetSectionOutputText(Session.DiffModeInfo.RightSection);
            }
            else {
                otherText = await GetSectionOutputText(Session.DiffModeInfo.LeftSection);
            }

            var diffBuilder = new DocumentDiffBuilder(App.Settings.DiffSettings);
            var diff = await Task.Run(() => diffBuilder.ComputeDiffs(otherText, text));

            var diffStats = new DiffStatistics();
            var diffFilter = Session.CompilerInfo.CreateDiffOutputFilter();
            diffFilter.Initialize(App.Settings.DiffSettings, Session.CompilerInfo.IR);
            var diffUpdater = new DocumentDiffUpdater(diffFilter, App.Settings.DiffSettings, Session.CompilerInfo);
            var diffResult = await Task.Run(() => diffUpdater.MarkDiffs(otherText, text, diff.NewText, diff.OldText,
                                                                        true, diffStats, true));
            ResetTextSearch(); // Reset current search, if there is any.

            // Replace the current text document.
            diffResult.DiffDocument.SetOwnerThread(Thread.CurrentThread);
            await TextView.SwitchDocument(diffResult.DiffDocument, diffResult.DiffText);
            TextView.AddDiffTextSegments(diffResult.DiffSegments);
            
            FilterSearchResults = false; // Not compatible.
            DiffModeEnabled = true;
        }

        private async Task<string> GetSectionOutputText(IRTextSection section) {
            var output = SelectSectionOutput(section);
            return await Session.GetSectionOutputTextAsync(output, section);
        }

        private async void DiffToggleButton_Checked(object sender, RoutedEventArgs e) {
            await EnableDiffMode();
        }

        private async void DiffToggleButton_Unchecked(object sender, RoutedEventArgs e) {
            await DisableDiffMode();
        }

        private async Task DisableDiffMode() {
            if(!DiffModeEnabled) {
                return;
            }

            TextView.RemoveDiffTextSegments();
            await TextView.SwitchText(initialText_);
            DiffModeEnabled = false;
        }
    }
}
