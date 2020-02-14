// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Client.Utilities;
using Core;
using Core.Analysis;
using Core.IR;
using Core.UTC;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;

// TODO: Clicking on scroll bar not working if there is an IR element under it,
// that one should be ignored if in the scroll bar bounds. GraphPanel does thats

namespace Client {

    public class RemarkTag : ITag {
        public string Name => "Output lines tag";
        public IRElement Parent { get; set; }

        public List<PassRemark> Remarks { get; }

        public RemarkTag() {
            Remarks = new List<PassRemark>();
        }

        public override string ToString() {
            var builder = new StringBuilder();

            foreach (var remark in Remarks) {
                builder.AppendLine(remark.ToString());
            }

            return builder.ToString();
        }
    }

    public class LightIRDocument : TextEditor {
        public enum TextSearchMode {
            Mark,
            Filter
        }

        string initialText_;
        IRTextSection section_;
        FunctionIR function_;
        List<Tuple<int, int>> initialTextLines_;
        ElementHighlighter elementMarker_;
        ElementHighlighter hoverElementMarker_;
        ElementHighlighter searchResultMarker_;
        HighlightingStyle elementStyle_;
        HighlightingStyle hoverElementStyle_;
        HighlightingStyle searchResultStyle_;
        List<IRElement> elements_;
        List<Reference> selectedElementRefs_;
        int selectedElementRefIndex_;
        IRElement prevSelectedElement_;
        IRPreviewToolTip previewTooltip_;
        TextSearchMode searchMode_;

        public ISessionManager Session { get; set; }
        public TextSearchMode SearchMode { get => searchMode_; set => searchMode_ = value; }

        public LightIRDocument() {
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
            SyntaxHighlighting = Utils.LoadSyntaxHighlightingFile(App.GetSyntaxHighlightingFilePath());

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
                    if (selectedElementRefs_ != null &&
                        refElement == prevSelectedElement_) {
                        return;
                    }

                    var refElementDef = refFinder.FindDefinition(refElement);
                    var tooltipElement = refElementDef != null ? refElementDef : refElement;
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
            this.ForceCursor = false;
            UpdateHighlighting();
            HideToolTip();
        }

        private void TextView_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
            var position = e.GetPosition(TextArea.TextView);
            var element = DocumentUtils.FindPointedElement(position, this, elements_);
            hoverElementMarker_.Clear();

            if (element != null) {
                hoverElementMarker_.Add(new HighlightedGroup(element, hoverElementStyle_));
                this.ForceCursor = true;
                this.Cursor = Cursors.Arrow;
            }
            else {
                this.ForceCursor = false;
                selectedElementRefs_ = null;
            }

            UpdateHighlighting();
        }

        private void TextView_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (Session == null || Session.CurrentDocument == null) {
                return;
            }

            HideToolTip();

            var position = e.GetPosition(TextArea.TextView);
            var element = DocumentUtils.FindPointedElement(position, this, elements_);

            if (element != null) {
                ReferenceFinder refFinder = null;
                IRElement refElement = null;

                if (element is InstructionIR instr) {
                    var similarValueFinder = new SimilarValueFinder(Session.CurrentDocument.Function);
                    refElement = similarValueFinder.Find(instr);
                }
                else {
                    refFinder = new ReferenceFinder(Session.CurrentDocument.Function);
                    refElement = refFinder.FindEquivalentValue(element);
                }

                if (refElement != null) {
                    if (refFinder == null) {
                        // Highlight an entire instruction.
                        Session.CurrentDocument.HighlightElement(refElement,
                                                                 HighlighingType.Hovered);
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
                        selectedElementRefIndex_ = 0;
                    }

                    var currentRefElement = selectedElementRefs_[selectedElementRefIndex_];
                    selectedElementRefIndex_++;

                    Session.CurrentDocument.HighlightElement(currentRefElement.Element,
                                                             HighlighingType.Hovered);
                    e.Handled = true;
                    return;
                }
            }

            selectedElementRefs_ = null;
            prevSelectedElement_ = null;
        }

        private async void TextView_TextChanged(object sender, System.EventArgs e) {
            await UpdateElementHighlighting();
        }


        public async Task SwitchText(string text, FunctionIR function, IRTextSection section) {
            initialText_ = text;
            function_ = function;
            section_ = section;
            Text = initialText_;
            IsReadOnly = false;
            EnsureInitialTextLines();
            await UpdateElementHighlighting();
        }

        private (TupleIR, BlockIR) CreateFakeIRElements() {
            var func = new FunctionIR();
            var block = new BlockIR(IRElementId.FromLong(0), 0, func);
            var tuple = new TupleIR(IRElementId.FromLong(1), TupleKind.Other, block);
            return (tuple, block);
        }

        private async Task UpdateElementHighlighting() {
            elementMarker_.Clear();
            hoverElementMarker_.Clear();
            searchResultMarker_.Clear();
            var currentText = Text;

            if (function_ == null || string.IsNullOrEmpty(currentText)) {
                // No function set yet or switching sections.
                UpdateHighlighting();
                return;
            }

            var defElements = new HighlightedGroup(elementStyle_);

            await Task.Run(() => {
                lock (this) {
                    elements_ = ExtractTextOperands(currentText, function_);

                    foreach (var element in elements_) {
                        defElements.Add(element);
                    }
                }
            });

            if (!defElements.IsEmpty()) {
                elementMarker_.Add(defElements);
            }

            UpdateHighlighting();
        }

        private List<IRElement> ExtractTextOperands(string text, FunctionIR function) {
            var elements = new List<IRElement>();
            var remarkProvider = Session.CompilerInfo.RemarkProvider;
            var remarks = remarkProvider.ExtractRemarks(text, function, section_);

            foreach (var remark in remarks) {
                foreach (var element in remark.OutputElements) {
                    elements.Add(element);
                }
            }

            return elements;
        }

        private void UpdateHighlighting() {
            TextArea.TextView.Redraw();
        }

        public async Task<List<TextSearchResult>> SearchText(Document.SearchInfo info) {
            if (string.IsNullOrEmpty(info.SearchedText)) {
                Text = initialText_;
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
                var searchResult = await Task.Run(() => SearchAndFilterTextLines(info));
                Text = searchResult;

                await UpdateElementHighlighting();
                return null;
            }
            else {
                Text = initialText_;
                var searchResults = await Task.Run(() =>
                    TextSearcher.AllIndexesOf(initialText_, info.SearchedText,
                                              startOffset: 0, info.SearchKind));
                HighlightSearchResults(searchResults);
                return searchResults;
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

        private string SearchAndFilterTextLines(Document.SearchInfo info) {
            var builder = new StringBuilder();
            var text = initialText_.AsSpan();

            foreach (var line in initialTextLines_) {
                var lineText = text.Slice(line.Item1, line.Item2).ToString();

                if (TextSearcher.Contains(lineText, info.SearchedText, info.SearchKind)) {
                    builder.AppendLine(lineText);
                }
            }

            return builder.ToString();
        }
    }
}
