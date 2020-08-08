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

        //? TODO: Not restored
        public IRElement DefinedOperand;
        public bool HasPinnedContent;
        public double HorizontalOffset;
        public double VerticalOffset;
    }

    public partial class DefinitionPanel : ToolPanelControl {
        private IRElement definedOperand_;
        private IRDocument document_;

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
            if (HasPinnedContent) {
                return;
            }

            if (!(e.Element is OperandIR op)) {
                return;
            }

            document_ = e.Document;
            TextView.InitializeFromDocument(document_);

            if (op.IsLabelAddress) {
                SwitchDefinitionElement(op, op.BlockLabelValue);
                return;
            }

            // Try to use SSA info first.
            var ssaDefOp = ReferenceFinder.GetSSADefinition(op);

            if (ssaDefOp != null) {
                if (ssaDefOp != op) {
                    SwitchDefinitionElement(op, ssaDefOp);
                    return;
                }
            }

            // Fall back to symbols with a single definition.
            var refFinder = new ReferenceFinder(document_.Function);
            var defOp = refFinder.FindDefinition(e.Element);

            if (defOp != null && defOp != op) {
                SwitchDefinitionElement(op, defOp);
                return;
            }

            SymbolName.Text = "";
        }

        private void SwitchDefinitionElement(OperandIR op, IRElement defOp) {
            SymbolName.Text = op.GetText(document_.Text).ToString();
            TextView.MarkElementWithDefaultStyle(defOp);
            definedOperand_ = op;
        }

        public override void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
            TextView.InitializeFromDocument(document);

            if (Session.LoadPanelState(this, section) is DefinitionPanelState savedState) {
                OnElementSelected(new IRElementEventArgs { Element = savedState.DefinedOperand });
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
                savedState.VerticalOffset = TextView.VerticalOffset;
                savedState.HorizontalOffset = TextView.HorizontalOffset;
                savedState.CaretOffset = TextView.CaretOffset;
                savedState.HasPinnedContent = HasPinnedContent;
                Session.SavePanelState(savedState, this, section);
                ResetDefinedOperand();
            }

            TextView.UnloadDocument();
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
