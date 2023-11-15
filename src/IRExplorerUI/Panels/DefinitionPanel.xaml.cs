// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Document;

namespace IRExplorerUI;

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

  private void SwitchSelectedElement(IRElement element, IRDocument document) {
    if (HasPinnedContent) {
      return;
    }

    if (!(element is OperandIR op)) {
      return;
    }

    if (op.IsLabelAddress) {
      SwitchDefinitionElement(op, op.BlockLabelValue);
      return;
    }

    var refFinder = DocumentUtils.CreateReferenceFinder(Document.Function, Session,
                                                        App.Settings.DocumentSettings);
    var defOp = refFinder.FindSingleDefinition(element);

    if (defOp != null && defOp != op) {
      SwitchDefinitionElement(op, defOp);
      return;
    }

    SymbolName.Text = "";
  }

  private void SwitchDefinitionElement(OperandIR op, IRElement defOp) {
    //? TODO: Go through the NameProvider
    SymbolName.Text = op.GetText(Document.Text).ToString();
    TextView.MarkElementWithDefaultStyle(defOp);
    TextView.BringElementIntoView(defOp);
    definedOperand_ = op;
  }

  public override void OnDocumentSectionLoaded(IRTextSection section, IRDocument document) {
    if (TextView.Section == section) {
      return;
    }

    TextView.InitializeFromDocument(document, false);
    Document = document;

    if (Session.LoadPanelState(this, section, document) is DefinitionPanelState savedState) {
#if DEBUG
      bool found = false;

      foreach (var op in document.Function.AllElements) {
        if (op.TextLocation == savedState.DefinedOperand.TextLocation) {
          found = true;
          break;
        }
      }

      if (!found) {
        Trace.WriteLine(
          $"=> ERROR for {savedState.DefinedOperand}, offset {savedState.DefinedOperand.TextLocation.Offset}");
        Trace.WriteLine($"     doc length: {TextView.Text.Length}");
        MessageBox.Show("Definition panel offset error, attach debugger");
        Utils.WaitForDebugger();
      }
#endif

      SwitchSelectedElement(savedState.DefinedOperand, document);

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
    if (TextView.Section != section) {
      return;
    }

    if (definedOperand_ != null) {
      var savedState = new DefinitionPanelState();
      savedState.DefinedOperand = definedOperand_;
      savedState.VerticalOffset = TextView.VerticalOffset;
      savedState.HorizontalOffset = TextView.HorizontalOffset;
      savedState.CaretOffset = TextView.CaretOffset;
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
