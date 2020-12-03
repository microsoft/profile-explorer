// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public class DefinitionPanelState {
        public int CaretOffset;
        public IRElement DefinedOperand;
        public bool HasPinnedContent;
        public double HorizontalOffset;
        public double VerticalOffset;
    }

    public partial class DefinitionPanel : ToolPanelControl {
        private IRElement definedOperand_;

        public DefinitionPanel() {
            InitializeComponent();
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private void PanelToolbarTray_DuplicateClicked(object sender, DuplicateEventArgs e) {
            Session.DuplicatePanel(this, e.Kind);
        }

        private void PanelToolbarTray_PinnedChanged(object sender, PinEventArgs e) {
            HasPinnedContent = e.IsPinned;
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Definition;
        public override HandledEventKind HandledEvents => HandledEventKind.ElementSelection;

        public override bool HasPinnedContent {
            get => FixedToolbar.IsPinned;
            set => FixedToolbar.IsPinned = value;
        }

        public override void OnElementSelected(IRElementEventArgs e) {
            SwitchSelectedElement(e.Element, e.Document);
        }

        public void SwitchSelectedElement(IRElement element, IRDocument document) {
            if (HasPinnedContent) {
                return;
            }

            if (!(element is OperandIR op)) {
                return;
            }

            if(Document != null && Document != document) {
                MessageBox.Show("Not same doc");
                Utils.WaitForDebugger();
            }

            if (op.IsLabelAddress) {
                SwitchDefinitionElement(op, op.BlockLabelValue);
                return;
            }

            var refFinder = new ReferenceFinder(Document.Function);
            var defOp = refFinder.FindSingleDefinition(element);

            if (defOp != null && defOp != op) {
                SwitchDefinitionElement(op, defOp);
                return;
            }

            SymbolName.Text = "";
        }

        private void SwitchDefinitionElement(OperandIR op, IRElement defOp) {
            SymbolName.Text = op.GetText(Document.Text).ToString();
            TextView.MarkElementWithDefaultStyle(defOp);
            definedOperand_ = op;
        }

        public override void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            TextView.InitializeFromDocument(document);
            Document = document;

            if (Session.LoadPanelState(this, section, Document) is DefinitionPanelState savedState) {
                SwitchSelectedElement(savedState.DefinedOperand, document);

                if (savedState.CaretOffset > TextView.Text.Length) {
                    MessageBox.Show("Invalid offset in definition window text, attach debugger");
                    Utils.WaitForDebugger();
                }

                TextView.SetCaretAtOffset(savedState.CaretOffset);
                TextView.ScrollToHorizontalOffset(savedState.HorizontalOffset);
                TextView.ScrollToVerticalOffset(savedState.VerticalOffset);
                HasPinnedContent = savedState.HasPinnedContent;
            }
            else {
                TextView.SetCaretAtOffset(0);
                TextView.ScrollToHorizontalOffset(0);
                TextView.ScrollToVerticalOffset(0);
                HasPinnedContent = false;
            }
        }

        public override void OnDocumentSectionUnloaded(IRTextSection section, IRDocument document) {
            if (definedOperand_ != null) {
                var savedState = new DefinitionPanelState();
                savedState.DefinedOperand = definedOperand_;
                savedState.VerticalOffset = TextView.VerticalOffset;
                savedState.HorizontalOffset = TextView.HorizontalOffset;
                savedState.CaretOffset = TextView.CaretOffset;

                if (savedState.CaretOffset > TextView.Text.Length) {
                    MessageBox.Show("Invalid offset in state, attach debugger");
                    Utils.WaitForDebugger();
                }

                if (section != document.Section) {
                    MessageBox.Show("Invalid section in state, attach debugger");
                    Utils.WaitForDebugger();
                }

                savedState.HasPinnedContent = HasPinnedContent;
                Session.SavePanelState(savedState, this, section, Document);
                ResetDefinedOperand();
            }

            TextView.UnloadDocument();
            Document = null;
        }

        private void ResetDefinedOperand() {
            definedOperand_ = null;
            SymbolName.Text = "";
        }

        public override void OnSessionEnd() {
            base.OnSessionEnd();
            ResetDefinedOperand();
            TextView.UnloadDocument();
        }

        public override void ClonePanel(IToolPanel sourcePanel) {
            var defPanel = sourcePanel as DefinitionPanel;
            TextView.InitializeFromDocument(defPanel.TextView);
        }

        #endregion
    }
}
