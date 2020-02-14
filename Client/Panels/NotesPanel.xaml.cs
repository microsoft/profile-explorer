// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Core;
using ProtoBuf;

namespace Client {
    [ProtoContract]
    public class NotesPanelState {
        [ProtoMember(1)]
        public string Text;
        [ProtoMember(2)]
        public string SearchText;
        [ProtoMember(3)]
        public bool ShowSectionNotes;

        public NotesPanelState() { }
    }

    public class NotesPanelSettings {
        public bool FilterSearchedTextLines;
    }

    public partial class NotesPanel : ToolPanelControl {
        bool showSectionText_;

        public NotesPanel() {
            InitializeComponent();
        }

        #region IToolPanel
        public override ToolPanelKind PanelKind => ToolPanelKind.Notes;
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
                    var state = StateSerializer.Deserialize<NotesPanelState>(data, document.Function);
                    await TextView.SwitchText(state.Text, document.Function, section);
                    showSectionText_ = state.ShowSectionNotes;
                    FilterComboBox.SelectedIndex = showSectionText_ ? 1 : 0;
                }
                else {
                    await TextView.SwitchText("", document.Function, section);
                    await TextView.SearchText(new Document.SearchInfo());
                }
            }
        }

        private async Task LoadSessionNotes() {
            TextView.SwitchText(Session.SessionState.Info.Notes, null, null);
            await TextView.SearchText(new Document.SearchInfo());
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
            var state = new NotesPanelState();
            state.Text = TextView.Text;
            state.SearchText = TextSearch.Text;
            state.ShowSectionNotes = showSectionText_;

            var data = StateSerializer.Serialize<NotesPanelState>(state, document.Function);
            Session.SavePanelState(data, this, section);
        }
        #endregion

        private void PanelToolbarTray_DuplicateClicked(object sender, DuplicateEventArgs e) {
            Session.DuplicatePanel(this, e.Kind);
        }

        private void ToolBar_Loaded(object sender, System.Windows.RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private async void TextSearch_TextChanged(object sender, TextChangedEventArgs e) {

        }

        private void ExecuteClearTextSearch(object sender, ExecutedRoutedEventArgs e) {
            ((TextBox)e.Parameter).Text = string.Empty;
        }

        private async void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (!IsPanelEnabled || Session.CurrentDocument == null) {
                return;
            }

            var item = e.AddedItems[0] as ComboBoxItem;
            var kindString = item.Tag as string;
            OnSessionSave();

            switch (kindString) {
                case "Document": showSectionText_ = false; break;
                case "Section": showSectionText_ = true; break;
            }

            await SwitchText(Session.CurrentDocumentSection, Session.CurrentDocument);
        }

        private void ComboBox_Loaded(object sender, RoutedEventArgs e) {
            if (sender is ComboBox control) {
                Utils.PatchComboBoxStyle(control);
            }
        }
    }
}

