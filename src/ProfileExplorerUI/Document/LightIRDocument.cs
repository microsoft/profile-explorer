// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ProfileExplorerCore2;
using ProfileExplorerCore2.Analysis;
using ProfileExplorerCore2.IR;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Document;
using ProfileExplorerCore2.Utilities;

// TODO: Clicking on scroll bar not working if there is an IR element under it,
// that one should be ignored if in the scroll bar bounds. GraphPanel does thats

namespace ProfileExplorer.UI;

public sealed class LightIRDocument : TextEditor {
  public enum TextSearchMode {
    Mark,
    Filter
  }

  private ElementHighlighter elementMarker_;
  private List<IRElement> elements_;
  private HighlightingStyle elementStyle_;
  private FunctionIR function_;
  private ElementHighlighter hoverElementMarker_;
  private HighlightingStyle hoverElementStyle_;
  private object lockObject_;
  private string initialText_;
  private bool initialTextChanged_;
  private List<Tuple<int, int>> initialTextLines_;
  private IRElement prevSelectedElement_;
  private TextSearchMode searchMode_;
  private ElementHighlighter searchResultMarker_;
  private HighlightingStyle searchResultStyle_;
  private IRTextSection section_;
  private IRDocument associatedDocument_;
  private int selectedElementRefIndex_;
  private List<Reference> selectedElementRefs_;
  private bool syntaxHighlightingLoaded_;
  private CancelableTask updateHighlightingTask_;
  private DiffLineHighlighter diffHighlighter_;
  private IRDocumentPopupInstance previewPopup_;
  private IUISession session_;

  public LightIRDocument() {
    lockObject_ = new object();
    elements_ = new List<IRElement>();
    elementStyle_ = new HighlightingStyle(Utils.ColorFromString("#FFFCDC"));
    hoverElementStyle_ = new HighlightingStyle(Utils.ColorFromString("#FFF487"));
    searchResultStyle_ = new HighlightingStyle(Colors.Khaki); //? TODO: Customize
    elementMarker_ = new ElementHighlighter(HighlighingType.Marked);
    hoverElementMarker_ = new ElementHighlighter(HighlighingType.Marked);
    searchResultMarker_ = new ElementHighlighter(HighlighingType.Marked);
    diffHighlighter_ = new DiffLineHighlighter();
    TextArea.TextView.BackgroundRenderers.Add(diffHighlighter_);
    TextArea.TextView.BackgroundRenderers.Add(elementMarker_);
    TextArea.TextView.BackgroundRenderers.Add(searchResultMarker_);
    TextArea.TextView.BackgroundRenderers.Add(hoverElementMarker_);
    TextChanged += TextView_TextChanged;
    PreviewMouseLeftButtonDown += TextView_PreviewMouseLeftButtonDown;
    MouseLeave += TextView_MouseLeave;
    PreviewMouseMove += TextView_PreviewMouseMove;

    // Don't use rounded corners for selection rectangles.
    TextArea.SelectionCornerRadius = 0;
    TextArea.SelectionBorder = null;
    Options.EnableEmailHyperlinks = false;
    Options.EnableHyperlinks = false;
  }

  public IUISession Session {
    get => session_;
    set {
      session_ = value;
      SetupPreviewPopup();
    }
  }

  public IRTextSection Section => section_;
  public FunctionIR Function => function_;
  public IRDocument AssociatedDocument => associatedDocument_;

  public TextSearchMode SearchMode {
    get => searchMode_;
    set => searchMode_ = value;
  }

  public void UnloadDocument() {
    initialText_ = null;
    section_ = null;
    function_ = null;
    Text = "";
  }

  public async Task SwitchText(string text, FunctionIR function, IRTextSection section,
                               IRDocument associatedDocument) {
    function_ = function;
    section_ = section;
    associatedDocument_ = associatedDocument;
    await SwitchText(text);
  }

  public async Task SwitchText(string text) {
    Text = text;
    await SwitchTextImpl(text);
  }

  public async Task SwitchDocument(TextDocument document, string text) {
    // Take ownership of the document and replace current text.
    document.SetOwnerThread(Thread.CurrentThread);
    Document = document;
    await SwitchTextImpl(text);
  }

  public void EnableIRSyntaxHighlighting() {
    if (!syntaxHighlightingLoaded_) {
      SyntaxHighlighting = Utils.LoadSyntaxHighlightingFile(App.GetSyntaxHighlightingFilePath());
      syntaxHighlightingLoaded_ = true;
    }
  }

  public void ResetTextSearch() {
    RestoreInitialText();
    IsReadOnly = false;
    UpdateHighlighting();
  }

  public async Task<List<TextSearchResult>> SearchText(SearchInfo info) {
    searchResultMarker_.Clear();

    if (!info.HasSearchedText) {
      ResetTextSearch();
      return null;
    }

    if (info.SearchedText.Length < 2) {
      UpdateHighlighting();
      return null;
    }

    // Disable text editing while search is used.
    IsReadOnly = true;

    if (searchMode_ == TextSearchMode.Filter) {
      EnsureInitialTextLines();
      string searchResult = await Task.Run(() => SearchAndFilterTextLines(info));
      Text = searchResult;
      initialTextChanged_ = true;
      await UpdateElementHighlighting();
      return null;
    }

    RestoreInitialText();

    var searchResults =
      await Task.Run(() => TextSearcher.AllIndexesOf(initialText_, info.SearchedText, 0, info.SearchKind));

    HighlightSearchResults(searchResults);
    return searchResults;
  }

  public void AddDiffTextSegments(List<DiffTextSegment> segments) {
    diffHighlighter_.Clear();
    diffHighlighter_.Add(segments);
    UpdateHighlighting();
  }

  public void RemoveDiffTextSegments() {
    diffHighlighter_.Clear();
    UpdateHighlighting();
  }

  internal void JumpToSearchResult(TextSearchResult result) {
    int line = Document.GetLineByOffset(result.Offset).LineNumber;
    ScrollToLine(line);
  }

  private void SetupPreviewPopup() {
    if (previewPopup_ != null) {
      previewPopup_.UnregisterHoverEvents();
      previewPopup_ = null;
    }

    previewPopup_ =
      new IRDocumentPopupInstance(App.Settings.GetElementPreviewPopupSettings(ToolPanelKind.Other), Session);
    previewPopup_.SetupHoverEvents(this, HoverPreview.HoverDuration, () => {
      if (Session.CurrentDocument == null) {
        return null;
      }

      var position = Mouse.GetPosition(TextArea.TextView);
      var element = DocumentUtils.FindPointedElement(position, this, elements_);

      if (element != null) {
        var refFinder = new ReferenceFinder(Session.CurrentDocument.Function);
        var refElement = refFinder.FindEquivalentValue(element);

        if (refElement != null) {
          // Don't show tooltip when user switches between references.
          if (selectedElementRefs_ != null && refElement == prevSelectedElement_) {
            return null;
          }

          var refElementDef = refFinder.FindSingleDefinition(refElement);
          var tooltipElement = refElementDef ?? refElement;
          return PreviewPopupArgs.ForDocument(Session.CurrentDocument, tooltipElement, this,
                                              $"Preview");
        }
      }

      return null;
    });
  }

  private void TextView_MouseLeave(object sender, MouseEventArgs e) {
    hoverElementMarker_.Clear();
    ForceCursor = false;
    UpdateHighlighting();
  }

  private void TextView_PreviewMouseMove(object sender, MouseEventArgs e) {
    var position = e.GetPosition(TextArea.TextView);
    var element = DocumentUtils.FindPointedElement(position, this, elements_);
    hoverElementMarker_.Clear();

    if (element != null) {
      hoverElementMarker_.Add(new HighlightedElementGroup(element, hoverElementStyle_));
      ForceCursor = true;
      Cursor = Cursors.Arrow;
    }
    else {
      ForceCursor = false;
      selectedElementRefs_ = null;
    }

    UpdateHighlighting();
  }

  private IRDocument FindTargetDocument() {
    if (associatedDocument_ != null &&
        associatedDocument_.Section == section_) {
      return associatedDocument_;
    }

    if (Session?.CurrentDocument == null ||
        Session?.CurrentDocument.Section == section_) {
      return Session.CurrentDocument;
    }

    return null;
  }

  private void TextView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    previewPopup_?.HidePreviewPopup(true);
    var position = e.GetPosition(TextArea.TextView);

    // Ignore click outside the text view.
    if (position.X >= TextArea.TextView.ActualWidth ||
        position.Y >= TextArea.TextView.ActualHeight) {
      return;
    }

    var element = DocumentUtils.FindPointedElement(position, this, elements_);

    if (element != null) {
      var targetDocument = FindTargetDocument();
      ReferenceFinder refFinder = null;
      IRElement refElement = null;

      if (element is InstructionIR instr) {
        var similarValueFinder = new SimilarValueFinder(function_);
        refElement = similarValueFinder.Find(instr);
      }
      else {
        refFinder = new ReferenceFinder(function_);
        refElement = refFinder.FindEquivalentValue(element);
      }

      if (refElement != null) {
        if (refFinder == null) {
          // Highlight an entire instruction.
          targetDocument.HighlightElement(refElement, HighlighingType.Hovered);
          selectedElementRefs_ = null;
          e.Handled = true;
          return;
        }

        // Cycle between all references of the operand.
        if (refElement != prevSelectedElement_) {
          selectedElementRefs_ = refFinder.FindAllSSAUsesOrReferences(refElement);
          selectedElementRefIndex_ = 0;
          prevSelectedElement_ = refElement;
        }

        if (selectedElementRefs_ == null) {
          return;
        }

        if (selectedElementRefIndex_ == selectedElementRefs_.Count) {
          selectedElementRefIndex_ = 0; // Cycle back to first ref.
        }

        var currentRefElement = selectedElementRefs_[selectedElementRefIndex_++];
        targetDocument.HighlightElement(currentRefElement.Element, HighlighingType.Hovered);
        e.Handled = true;
        return;
      }
    }

    selectedElementRefs_ = null;
    prevSelectedElement_ = null;
  }

  private async void TextView_TextChanged(object sender, EventArgs e) {
    await UpdateElementHighlighting();
  }

  private async Task SwitchTextImpl(string text) {
    initialText_ = text;
    initialTextChanged_ = false;
    EnableIRSyntaxHighlighting();
    EnsureInitialTextLines();

    if (function_ != null) {
      await UpdateElementHighlighting();
    }
  }

  private async Task UpdateElementHighlighting() {
    // If there is another task running, cancel it and wait for it to complete
    // before starting a new task, this can happen when quickly changing sections.
    CancelableTask currentUpdateTask = null;

    lock (lockObject_) {
      if (updateHighlightingTask_ != null) {
        updateHighlightingTask_.Cancel();
        currentUpdateTask = updateHighlightingTask_;
      }
    }

    if (currentUpdateTask != null) {
      await currentUpdateTask.WaitToCompleteAsync();
    }

    elements_.Clear();
    elementMarker_.Clear();
    hoverElementMarker_.Clear();
    searchResultMarker_.Clear();
    UpdateHighlighting();

    // When unloading a document, no point to start a new task.
    string currentText = initialTextChanged_ ? Text : initialText_;

    if (string.IsNullOrEmpty(currentText)) {
      return;
    }

    var defElements = new HighlightedElementGroup(elementStyle_);
    updateHighlightingTask_ = new CancelableTask();

    await Task.Run(() => {
      lock (lockObject_) {
        if (updateHighlightingTask_.IsCanceled) {
          // Task got canceled in the meantime.
          updateHighlightingTask_.Complete();
          return;
        }

        if (function_ == null || string.IsNullOrEmpty(currentText)) {
          // No function set yet or switching sections.
          return;
        }

        elements_ = ExtractTextOperands(currentText, function_, updateHighlightingTask_);

        foreach (var element in elements_) {
          defElements.Add(element);
        }
      }
    });

    elementMarker_.Add(defElements);
    UpdateHighlighting();
  }

  private List<IRElement> ExtractTextOperands(string text, FunctionIR function, CancelableTask cancelableTask) {
    var elements = new List<IRElement>();
    var remarkProvider = Session.CompilerInfo.RemarkProvider;
    var options = new RemarkProviderOptions {
      FindOperandRemarks = false,
      IgnoreOverlappingOperandRemarks = true
    };

    var remarks = remarkProvider.ExtractRemarks(text, function, section_,
                                                options, cancelableTask);

    if (cancelableTask.IsCanceled) {
      cancelableTask.Complete();
      return elements;
    }

    foreach (var remark in remarks) {
      foreach (var element in remark.OutputElements) {
        elements.Add(element);
      }
    }

    cancelableTask.Complete();
    return elements;
  }

  private void UpdateHighlighting() {
    TextArea.TextView.Redraw();
  }

  private void RestoreInitialText() {
    if (initialTextChanged_) {
      Text = initialText_;
      initialTextChanged_ = false;
    }

    initialTextLines_ = null;
  }

  private void HighlightSearchResults(List<TextSearchResult> searchResults) {
    var group = new HighlightedElementGroup(searchResultStyle_);

    foreach (var result in searchResults) {
      int line = Document.GetLineByOffset(result.Offset).LineNumber;
      var location = new ProfileExplorerCore2.TextLocation(result.Offset, line, 0);
      var element = new IRElement(location, result.Length);
      group.Add(element);
    }

    if (!group.IsEmpty()) {
      searchResultMarker_.Add(group);
      UpdateHighlighting();
    }
  }

  private void EnsureInitialTextLines() {
    if (initialTextLines_ == null) {
      initialText_ = Text;
      initialTextLines_ = new List<Tuple<int, int>>(Document.LineCount);

      foreach (var line in Document.Lines) {
        initialTextLines_.Add(new Tuple<int, int>(line.Offset, line.Length));
      }
    }
  }

  private string SearchAndFilterTextLines(SearchInfo info) {
    var builder = new StringBuilder();
    var text = initialText_.AsSpan();

    foreach (var line in initialTextLines_) {
      string lineText = text.Slice(line.Item1, line.Item2).ToString();

      if (TextSearcher.Contains(lineText, info.SearchedText, info.SearchKind)) {
        builder.AppendLine(lineText);
      }
    }

    return builder.ToString();
  }
}