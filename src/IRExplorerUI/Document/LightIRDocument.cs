// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using IRExplorerUI.Document;
using IRExplorerUI.Utilities;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerCore.Utilities;

// TODO: Clicking on scroll bar not working if there is an IR element under it,
// that one should be ignored if in the scroll bar bounds. GraphPanel does thats

namespace IRExplorerUI {
    public class RemarkTag : ITag {
        public RemarkTag() {
            Remarks = new List<Remark>();
        }

        public List<Remark> Remarks { get; }
        public string Name => "Remark tag";
        public TaggedObject Owner { get; set; }

        public override string ToString() {
            var builder = new StringBuilder();
            builder.AppendLine($"remarks count: {Remarks.Count}");

            foreach (var remark in Remarks) {
                builder.Append($"  o {remark}".Indent(4));
            }

            return builder.ToString();
        }
    }

    public class LightIRDocument : TextEditor {
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
        private IRPreviewToolTip previewTooltip_;
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

        public LightIRDocument() {
            lockObject_ = new object();
            elements_ = new List<IRElement>();
            elementStyle_ = new HighlightingStyle(Utils.ColorFromString("#FFFCDC"));
            hoverElementStyle_ = new HighlightingStyle(Utils.ColorFromString("#FFF487"));
            searchResultStyle_ = new HighlightingStyle(Colors.Khaki);
            elementMarker_ = new ElementHighlighter(HighlighingType.Marked);
            hoverElementMarker_ = new ElementHighlighter(HighlighingType.Marked);
            searchResultMarker_ = new ElementHighlighter(HighlighingType.Marked);
            TextArea.TextView.BackgroundRenderers.Add(elementMarker_);
            TextArea.TextView.BackgroundRenderers.Add(searchResultMarker_);
            TextArea.TextView.BackgroundRenderers.Add(hoverElementMarker_);
            TextChanged += TextView_TextChanged;
            PreviewMouseLeftButtonDown += TextView_PreviewMouseLeftButtonDown;
            PreviewMouseHover += TextView_PreviewMouseHover;
            MouseLeave += TextView_MouseLeave;
            PreviewMouseMove += TextView_PreviewMouseMove;

            // Don't use rounded corners for selection rectangles.
            TextArea.SelectionCornerRadius = 0;
            TextArea.SelectionBorder = null;
            Options.EnableEmailHyperlinks = false;
            Options.EnableHyperlinks = false;
        }

        public ISession Session { get; set; }
        public IRTextSection Section => section_;
        public FunctionIR Function => function_;
        public IRDocument AssociatedDocument => associatedDocument_;

        public TextSearchMode SearchMode {
            get => searchMode_;
            set => searchMode_ = value;
        }

        internal void JumpToSearchResult(TextSearchResult result) {
            int line = Document.GetLineByOffset(result.Offset).LineNumber;
            ScrollToLine(line);
        }

        private void TextView_PreviewMouseHover(object sender, MouseEventArgs e) {
            HideToolTip();
            var position = e.GetPosition(TextArea.TextView);
            var element = DocumentUtils.FindPointedElement(position, this, elements_);

            if (element != null) {
                var refFinder = new ReferenceFinder(Session.CurrentDocument.Function);
                var refElement = refFinder.FindEquivalentValue(element);

                if (refElement != null) {
                    // Don't show tooltip when user switches between references.
                    if (selectedElementRefs_ != null && refElement == prevSelectedElement_) {
                        return;
                    }

                    var refElementDef = refFinder.FindDefinition(refElement);
                    var tooltipElement = refElementDef ?? refElement;
                    previewTooltip_ = new IRPreviewToolTip(600, 100, Session.CurrentDocument, tooltipElement);
                    previewTooltip_.Show();
                }
            }
        }

        private void HideToolTip() {
            if (previewTooltip_ != null) {
                previewTooltip_.Hide();
                previewTooltip_ = null;
            }
        }

        private void TextView_MouseLeave(object sender, MouseEventArgs e) {
            hoverElementMarker_.Clear();
            ForceCursor = false;
            UpdateHighlighting();
            HideToolTip();
        }

        private void TextView_PreviewMouseMove(object sender, MouseEventArgs e) {
            var position = e.GetPosition(TextArea.TextView);
            var element = DocumentUtils.FindPointedElement(position, this, elements_);
            hoverElementMarker_.Clear();

            if (element != null) {
                hoverElementMarker_.Add(new HighlightedGroup(element, hoverElementStyle_));
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
            else if (Session?.CurrentDocument == null ||
                     Session?.CurrentDocument.Section == section_) {
                return Session.CurrentDocument;
            }

            return null;
        }

        private void TextView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            HideToolTip();
            var position = e.GetPosition(TextArea.TextView);
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
                        selectedElementRefs_ = refFinder.FindAllDefUsesOrReferences(refElement);
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

        public void UnloadDocument() {
            Text = "";
            section_ = null;
            function_ = null;
            initialText_ = null;
        }

        private async void TextView_TextChanged(object sender, EventArgs e) {
            await UpdateElementHighlighting();
        }

        public async Task SwitchText(string text, FunctionIR function, IRTextSection section,
                                     IRDocument associatedDocument) {
            if (!syntaxHighlightingLoaded_) {
                SyntaxHighlighting = Utils.LoadSyntaxHighlightingFile(App.GetSyntaxHighlightingFilePath());
                syntaxHighlightingLoaded_ = true;
            }

            initialText_ = text;
            initialTextChanged_ = false;
            function_ = function;
            section_ = section;
            associatedDocument_ = associatedDocument;
            Text = initialText_;
            IsReadOnly = false;

            EnsureInitialTextLines();
            await UpdateElementHighlighting();
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

            elementMarker_.Clear();
            hoverElementMarker_.Clear();
            searchResultMarker_.Clear();
            UpdateHighlighting();

            // When unloading a document, no point to start a new task.
            string currentText = initialTextChanged_ ? Text : initialText_;

            if (string.IsNullOrEmpty(currentText)) {
                return;
            }

            var defElements = new HighlightedGroup(elementStyle_);
            updateHighlightingTask_ = new CancelableTask();

            await Task.Run(() => {
                lock (lockObject_) {
                    if (updateHighlightingTask_.IsCanceled) {
                        // Task got canceled in the meantime.
                        updateHighlightingTask_.Completed();
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
            var options = new RemarkProviderOptions() {
                FindOperandRemarks = false,
                IgnoreOverlappingOperandRemarks = true
            };

            var remarks = remarkProvider.ExtractRemarks(text, function, section_, options);

            if (cancelableTask.IsCanceled) {
                cancelableTask.Completed();
                return elements;
            }

            foreach (var remark in remarks) {
                foreach (var element in remark.OutputElements) {
                    elements.Add(element);
                }
            }

            cancelableTask.Completed();
            return elements;
        }

        private void UpdateHighlighting() {
            TextArea.TextView.Redraw();
        }

        public async Task<List<TextSearchResult>> SearchText(SearchInfo info) {
            if (string.IsNullOrEmpty(info.SearchedText)) {
                RestoreInitialText();
                IsReadOnly = false;
                initialTextLines_ = null;
                return null;
            }

            if (info.SearchedText.Length < 2) {
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
            else {
                RestoreInitialText();

                var searchResults =
                    await Task.Run(() => TextSearcher.AllIndexesOf(initialText_, info.SearchedText, 0, info.SearchKind));

                HighlightSearchResults(searchResults);
                return searchResults;
            }
        }

        private void RestoreInitialText() {
            if (initialTextChanged_) {
                Text = initialText_;
                initialTextChanged_ = false;
            }
        }

        private void HighlightSearchResults(List<TextSearchResult> searchResults) {
            var group = new HighlightedGroup(searchResultStyle_);

            foreach (var result in searchResults) {
                int line = Document.GetLineByOffset(result.Offset).LineNumber;
                var location = new TextLocation(result.Offset, line, 0);
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
}
