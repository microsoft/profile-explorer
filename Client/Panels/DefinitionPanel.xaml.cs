// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using Core;
using Core.Analysis;
using Core.IR;

namespace Client {
    public class DefinitionPanelState {
        public IRElement DefinedOperand;
        public double VerticalOffset;
        public double HorizontalOffset;
        public int CaretOffset;
        public bool HasPinnedContent;
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

            var savedState = Session.LoadPanelState(this, section) as DefinitionPanelState;

            if (savedState != null) {
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

                definedOperand_ = null;
                TextView.UnloadDocument();
                SymbolName.Text = "";
            }

            TextView.UnloadDocument();
        }

        public override void ClonePanel(IToolPanel sourcePanel) {
            var defPanel = sourcePanel as DefinitionPanel;
            TextView.InitializeFromDocument(defPanel.TextView);
        }
        #endregion

        private void PanelToolbarTray_DuplicateClicked(object sender, DuplicateEventArgs e) {
            Session.DuplicatePanel(this, e.Kind);
        }

        private void PanelToolbarTray_PinnedChanged(object sender, PinEventArgs e) {
            HasPinnedContent = e.IsPinned;
        }
    }
}
