// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRExplorer.Document;
using IRExplorerCore;
using ProtoBuf;

namespace IRExplorer {
    [ProtoContract]
    public class RemarksPanelState {
        [ProtoMember(2)]
        public string SearchText;
        [ProtoMember(3)]
        public bool ShowSectionNotes;
        [ProtoMember(1)]
        public string Text;
    }

    public class RemarksPanelSettings {
        public bool FilterSearchedTextLines;
    }

    public partial class RemarksPanel : ToolPanelControl {
        private bool showSectionText_;

        public RemarksPanel() {
            InitializeComponent();
        }

        private void PanelToolbarTray_DuplicateClicked(object sender, DuplicateEventArgs e) {
            Session.DuplicatePanel(this, e.Kind);
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private async void TextSearch_TextChanged(object sender, TextChangedEventArgs e) { }

        private void ExecuteClearTextSearch(object sender, ExecutedRoutedEventArgs e) {
            ((TextBox) e.Parameter).Text = string.Empty;
        }

        private async void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!IsPanelEnabled || Session.CurrentDocument == null) {
                return;
            }

            var item = e.AddedItems[0] as ComboBoxItem;
            string kindString = item.Tag as string;
            OnSessionSave();

            showSectionText_ = kindString switch {
                "Document" => false,
                "Section"  => true,
                _          => showSectionText_
            };

            await SwitchText(Session.CurrentDocumentSection, Session.CurrentDocument);
        }

        private void ComboBox_Loaded(object sender, RoutedEventArgs e) {
            if (sender is ComboBox control) {
                Utils.PatchComboBoxStyle(control);
            }
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Remarks;
        public override bool SavesStateToFile => true;

        public override async void OnSessionStart() {
            base.OnSessionStart();
            TextView.Session = Session;
            await LoadSessionNotes();
        }

        public override async void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            await SwitchText(section, document);
        }

        private async Task SwitchText(IRTextSection section, IRDocument document) {
            if (!showSectionText_) {
                await LoadSessionNotes();
            }
            else {
                var data = Session.LoadPanelState(this, section);

                if (data != null) {
                    var state = StateSerializer.Deserialize<RemarksPanelState>(data, document.Function);
                    await TextView.SwitchText(state.Text, document.Function, section);
                    showSectionText_ = state.ShowSectionNotes;
                    FilterComboBox.SelectedIndex = showSectionText_ ? 1 : 0;
                }
                else {
                    await TextView.SwitchText("", document.Function, section);
                    await TextView.SearchText(new SearchInfo());
                }
            }
        }

        private async Task LoadSessionNotes() {
            await TextView.SwitchText(Session.SessionState.Info.Notes, null, null);
            await TextView.SearchText(new SearchInfo());
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            if (!showSectionText_) {
                SaveSessionNotes();
            }
            else {
                SaveState(section, document);
            }
        }

        private void SaveSessionNotes() {
            Session.SessionState.Info.Notes = TextView.Text;
        }

        public override void OnSessionSave() {
            if (!showSectionText_) {
                Session.SessionState.Info.Notes = TextView.Text;
            }
            else {
                var document = Session.FindAssociatedDocument(this);

                if (document != null) {
                    SaveState(document.Section, document);
                }
            }
        }

        private void SaveState(IRTextSection section, IRDocument document) {
            var state = new RemarksPanelState();
            state.Text = TextView.Text;
            state.SearchText = TextSearch.Text;
            state.ShowSectionNotes = showSectionText_;
            var data = StateSerializer.Serialize(state, document.Function);
            Session.SavePanelState(data, this, section);
        }

        #endregion
    }
}
