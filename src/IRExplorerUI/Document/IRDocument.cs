// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerUI.Document;
using IRExplorerUI.Query;
using IRExplorerUI.Utilities;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.Graph;
using IRExplorerCore.IR;

namespace IRExplorerUI {
    public enum BringIntoViewStyle {
        Default,
        FirstLine
    }

    public class IRElementEventArgs : EventArgs {
        public IRElement Element { get; set; }
        public bool MirrorAction { get; set; }
        public IRDocument Document { get; set; }
    }

    public enum HighlightingEventAction {
        ReplaceHighlighting,
        AppendHighlighting,
        RemoveHighlighting
    }

    public class IRHighlightingEventArgs : EventArgs {
        public HighlightingEventAction Action { get; set; }
        public IRElement Element { get; set; }
        public HighlightedGroup Group { get; set; }
        public bool MirrorAction { get; set; }
        public HighlighingType Type { get; set; }
    }

    public class IRElementMarkedEventArgs : EventArgs {
        public IRElement Element { get; set; }
        public HighlightingStyle Style { get; set; }
    }

    public class SelectedBookmarkInfo {
        public Bookmark Bookmark { get; set; }
        public int SelectedIndex { get; set; }
        public int TotalBookmarks { get; set; }
    }

    public class IRDocument : TextEditor, INotifyPropertyChanged {
        private const float ParentStyleLightAdjustment = 1.20f;

        private static DocumentActionKind[] AutomationActions = {
            DocumentActionKind.SelectElement,
            DocumentActionKind.MarkElement,
            DocumentActionKind.ShowReferences,
            DocumentActionKind.GoToDefinition
        };
        private Stack<ReversibleDocumentAction> actionUndoStack_;
        private int automationActionIndex_;
        private IRElement automationPrevElement_;

        private int automationSelectedIndex_;
        private List<IRElement> blockElements_;
        private BlockBackgroundHighlighter blockHighlighter_;
        private BookmarkManager bookmarks_;
        private BlockIR currentBlock_;
        private HighlightedGroup currentSearchResultGroup_;
        private PairHighlightingStyle definitionStyle_;

        private DiffLineHighlighter diffHighlighter_;
        private List<DiffTextSegment> diffSegments_;
        private bool disableCaretEvent_;
        private ScrollBar docVerticalScrollbar_;
        private bool duringDiffModeSetup_;

        private bool duringSectionLoading_;
        private bool eventSetupDone_;
        private HighlightingStyleCollection expressionOperandStyle_;
        private HighlightingStyleCollection expressionStyle_;
        private FoldingManager folding_;

        private FunctionIR function_;
        private MarkerMarginVersionInfo highlighterVersion_;
        private MarkerBarElement hoveredBarElement_;
        private IRElement hoveredElement_;

        private ElementHighlighter hoverHighlighter_;
        private bool ignoreNextBarHover_;
        private bool ignoreNextCaretEvent_;
        private bool ignoreNextHoverEvent_;

        private IRElement ignoreNextPreviewElement_;
        private bool ignoreNextScrollEvent_;
        private CurrentLineHighlighter lineHighlighter_;
        private DocumentMargin margin_;
        private ElementHighlighter markedHighlighter_;
        private OverlayRenderer overlayRenderer_;

        private HighlightingStyleCyclingCollection markerChildStyle_;

        private Canvas markerMargin_;
        private List<MarkerBarElement> markerMargingElements_;
        private HighlightingStyleCyclingCollection markerParentStyle_;
        private IRPreviewToolTip nodeToolTip_;
        private List<IRElement> operandElements_;
        private RemarkHighlighter remarkHighlighter_;
        private DelayedAction removeHoveredAction_;
        private Dictionary<TextSearchResult, IRElement> searchResultMap_;
        private HighlightedGroup searchResultsGroup_;
        private IRTextSection section_;
        private HighlightingStyle selectedBlockStyle_;
        private HashSet<IRElement> selectedElements_;
        private ElementHighlighter selectedHighlighter_;
        private HighlightingStyle selectedStyle_;

        private DocumentSettings settings_;
        private PairHighlightingStyle ssaDefinitionStyle_;

        private PairHighlightingStyle ssaUserStyle_;
        private List<IRElement> tupleElements_;

        public IRDocument() {
            // Setup element tracking data structures.
            selectedElements_ = new HashSet<IRElement>();
            bookmarks_ = new BookmarkManager();
            actionUndoStack_ = new Stack<ReversibleDocumentAction>();
            highlighterVersion_ = new MarkerMarginVersionInfo();

            // Setup styles and colors.
            definitionStyle_ = new PairHighlightingStyle {
                ParentStyle = new HighlightingStyle(Color.FromRgb(255, 215, 191)),
                ChildStyle =
                    new HighlightingStyle(Color.FromRgb(255, 197, 163), Pens.GetBoldPen(Colors.Black))
            };

            expressionOperandStyle_ = HighlightingStyles.StyleSet;
            expressionStyle_ = HighlightingStyles.LightStyleSet;
            markerChildStyle_ = new HighlightingStyleCyclingCollection(HighlightingStyles.StyleSet);
            markerParentStyle_ = new HighlightingStyleCyclingCollection(HighlightingStyles.LightStyleSet);
            SetupProperties();
            SetupStableRenderers();
            SetupCommands();
        }

        public List<BlockIR> Blocks => function_.Blocks;
        public BookmarkManager BookmarkManager => bookmarks_;
        public ISessionManager Session { get; set; }
        public FunctionIR Function => function_;
        public IRTextSection Section => section_;

        public bool DiffModeEnabled { get; set; }
        public bool DuringSectionLoading => duringSectionLoading_;

        public DocumentSettings Settings {
            get => settings_;
            set {
                settings_ = value;
                ReloadSettings();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public bool NextAutomationAction() {
            if (automationSelectedIndex_ >= operandElements_.Count) {
                return false;
            }

            if (automationPrevElement_ == null || automationActionIndex_ == AutomationActions.Length) {
                automationPrevElement_ = operandElements_[automationSelectedIndex_++];
                automationActionIndex_ = 0;
            }

            if (automationPrevElement_ != null) {
                if (automationActionIndex_ < AutomationActions.Length) {
                    var action = AutomationActions[automationActionIndex_++];

                    switch (action) {
                        case DocumentActionKind.SelectElement:
                        case DocumentActionKind.ShowReferences:
                        case DocumentActionKind.GoToDefinition: {
                                ExecuteDocumentAction(new DocumentAction(action, automationPrevElement_));

                                if (action == DocumentActionKind.SelectElement) {
                                    BringElementIntoView(automationPrevElement_);
                                }

                                break;
                            }
                        case DocumentActionKind.MarkElement: {
                                ExecuteDocumentAction(new DocumentAction(action, automationPrevElement_,
                                                                         PickPairMarkerStyle()));

                                break;
                            }
                    }

                    return true;
                }
            }

            return true;
        }

        private void SetupStyles() {
            var borderPen = Pens.GetBoldPen(settings_.BorderColor);
            selectedStyle_ ??= new HighlightingStyle();
            selectedStyle_.BackColor = ColorBrushes.GetBrush(settings_.SelectedValueColor);
            selectedStyle_.Border = borderPen;
            selectedBlockStyle_ ??= new HighlightingStyle();
            selectedBlockStyle_.BackColor = ColorBrushes.GetBrush(Colors.Transparent);

            selectedBlockStyle_.Border =
                Pens.GetPen(ColorUtils.AdjustLight(settings_.SelectedValueColor, 0.75f), 2);

            ssaUserStyle_ ??= new PairHighlightingStyle();

            ssaUserStyle_.ParentStyle.BackColor =
                ColorBrushes.GetBrush(
                    ColorUtils.AdjustLight(settings_.UseValueColor, ParentStyleLightAdjustment));

            ssaUserStyle_.ChildStyle.BackColor = ColorBrushes.GetBrush(settings_.UseValueColor);
            ssaUserStyle_.ChildStyle.Border = borderPen;
            ssaDefinitionStyle_ ??= new PairHighlightingStyle();

            ssaDefinitionStyle_.ParentStyle.BackColor = ColorBrushes.GetBrush(
                ColorUtils.AdjustLight(settings_.DefinitionValueColor, ParentStyleLightAdjustment));

            ssaDefinitionStyle_.ChildStyle.BackColor = ColorBrushes.GetBrush(settings_.DefinitionValueColor);
            ssaDefinitionStyle_.ChildStyle.Border = borderPen;
        }

        public event EventHandler<DocumentAction> ActionPerformed;
        public event EventHandler<IRElementEventArgs> BlockSelected;
        public event EventHandler<Bookmark> BookmarkAdded;
        public event EventHandler<Bookmark> BookmarkChanged;
        public event EventHandler BookmarkListCleared;
        public event EventHandler<Bookmark> BookmarkRemoved;
        public event EventHandler<SelectedBookmarkInfo> BookmarkSelected;
        public event EventHandler<IRHighlightingEventArgs> ElementHighlighting;
        public event EventHandler<IRElementEventArgs> ElementSelected;
        public event EventHandler<IRElementEventArgs> ElementUnselected;

        public void BookmarkInfoChanged(Bookmark bookmark) {
            UpdateHighlighting();
        }

        private void ReloadSettings() {
            Background = ColorBrushes.GetBrush(settings_.BackgroundColor);
            Foreground = ColorBrushes.GetBrush(settings_.TextColor);
            FontFamily = new FontFamily(settings_.FontName);
            FontSize = settings_.FontSize;
            SetupRenderers();
            SetupStyles();
            SetupBlockFolding();
            SetupEvents();
            SyntaxHighlighting = Utils.LoadSyntaxHighlightingFile(App.GetSyntaxHighlightingFilePath());
            UpdateHighlighting();
        }

        private void MirrorAction(DocumentActionKind actionKind, IRElement element,
                                  object optionalData = null) {
            if (Utils.IsAltModifierActive()) {
                var action = new DocumentAction(actionKind, element, optionalData);
                ActionPerformed?.Invoke(this, action);
            }
        }

        private void RecordReversibleAction(DocumentActionKind actionKind, IRElement element,
                                            object optionalData = null) {
            ReversibleDocumentAction action = null;

            switch (actionKind) {
                case DocumentActionKind.MarkElement: {
                        action = new ReversibleDocumentAction(
                            new DocumentAction(actionKind, element, optionalData),
                            action => ClearMarkedElement(action.Element));

                        break;
                    }
                case DocumentActionKind.MarkUses:
                case DocumentActionKind.MarkReferences: {
                        action = new ReversibleDocumentAction(
                            new DocumentAction(actionKind, element, optionalData), action => {
                                ClearMarkedElement(action.Element);
                                ClearMarkedElement(action.Element.ParentTuple);
                                var refList = action.OptionalData as List<Reference>;

                                foreach (var reference in refList) {
                                    ClearMarkedElement(reference.Element);
                                    ClearMarkedElement(reference.Element.ParentTuple);
                                }

                                SelectAndActivateElement(action.Element);
                            });

                        break;
                    }
                case DocumentActionKind.GoToDefinition: {
                        action = new ReversibleDocumentAction(
                            new DocumentAction(actionKind, element, optionalData),
                            action => { SelectAndActivateElement(action.Element); });

                        break;
                    }
            }

            if (action != null) {
                Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Record undo action {action}");
                actionUndoStack_.Push(action);
            }
        }

        private void SelectAndActivateElement(IRElement element) {
            SelectElement(element);
            SetCaretAtElement(element);
            BringElementIntoView(element);
        }

        private bool UndoReversibleAction() {
            if (actionUndoStack_.Count == 0) {
                return false;
            }

            var undoAction = actionUndoStack_.Pop();
            undoAction.Undo();
            return true;
        }

        public void ExecuteDocumentAction(DocumentAction action) {
            Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Execute {action}");

            switch (action.ActionKind) {
                case DocumentActionKind.SelectElement: {
                        if (action.Element != null) {
                            SelectElement(action.Element);
                        }

                        break;
                    }
                case DocumentActionKind.MarkElement: {
                        if (action.Element != null) {
                            MarkElement(action.Element, action.OptionalData as PairHighlightingStyle);
                        }

                        break;
                    }
                case DocumentActionKind.MarkExpression: {
                        if (action.Element != null) {
                            var highlighter = action.OptionalData is MarkActionData data && data.IsTemporary
                                ? selectedHighlighter_
                                : markedHighlighter_;

                            if (action.Element is InstructionIR instr) {
                                if (instr.Destinations.Count > 0) {
                                    HandleElement(instr.Destinations[0], highlighter,
                                                  markExpression: true, markReferences: false);
                                }
                            }
                            else {
                                HandleElement(action.Element, highlighter,
                                              markExpression: true, markReferences: false);
                            }
                        }

                        break;
                    }
                case DocumentActionKind.ShowExpressionGraph: {
                        if (action.Element != null) {
                            ShowExpressionGraph(action.Element);
                        }

                        break;
                    }
                case DocumentActionKind.MarkBlock: {
                        if (action.Element != null) {
                            MarkBlock(action.Element, action.OptionalData as HighlightingStyle);
                        }

                        break;
                    }
                case DocumentActionKind.GoToDefinition: {
                        if (action.Element != null) {
                            GoToElementDefinition(action.Element);
                        }

                        break;
                    }
                case DocumentActionKind.ShowReferences: {
                        if (action.Element is OperandIR op) {
                            ShowReferences(op);
                        }
                        else if (action.Element is InstructionIR instr) {
                            // For an instruction, look for the references of the dest. operand.
                            if (instr.Destinations.Count > 0) {
                                ShowReferences(instr.Destinations[0]);
                            }
                        }

                        break;
                    }
                case DocumentActionKind.MarkReferences: {
                        if (action.Element is OperandIR op) {
                            MarkReferences(op, markedHighlighter_);
                        }
                        else if (action.Element is InstructionIR instr) {
                            // For an instruction, look for the references of the dest. operand.
                            if (instr.Destinations.Count > 0) {
                                MarkReferences(instr.Destinations[0], markedHighlighter_);
                            }
                        }

                        break;
                    }
                case DocumentActionKind.ShowUses: {
                        if (action.Element is OperandIR op) {
                            ShowUses(op);
                        }
                        else if (action.Element is InstructionIR instr) {
                            // For an instruction, look for the uses of the dest. operand.
                            if (instr.Destinations.Count > 0) {
                                ShowUses(instr.Destinations[0]);
                            }
                        }

                        break;
                    }
                case DocumentActionKind.MarkUses: {
                        if (action.Element is OperandIR op) {
                            MarkUses(op, action.OptionalData as PairHighlightingStyle);
                        }
                        else if (action.Element is InstructionIR instr) {
                            // For an instruction, look for the uses of the dest. operand.
                            if (instr.Destinations.Count > 0) {
                                MarkUses(instr.Destinations[0], action.OptionalData as PairHighlightingStyle);
                            }
                        }

                        break;
                    }
                case DocumentActionKind.ClearMarker: {
                        if (action.Element != null) {
                            ClearMarkedElement(action.Element);
                        }

                        break;
                    }
                case DocumentActionKind.ClearAllMarkers: {
                        ClearAllMarkers();
                        break;
                    }
                case DocumentActionKind.ClearBlockMarkers: {
                        ClearBlockMarkers();
                        break;
                    }
                case DocumentActionKind.ClearInstructionMarkers: {
                        ClearInstructionMarkers();
                        break;
                    }
                case DocumentActionKind.ClearTemporaryMarkers: {
                        ClearTemporaryHighlighting();
                        UpdateHighlighting();
                        break;
                    }
                case DocumentActionKind.UndoAction: {
                        UndoReversibleAction();
                        break;
                    }
            }

            UpdateHighlighting();
            UpdateMargin();
        }

        public void BringTextOffsetIntoView(int offset) {
            ignoreNextHoverEvent_ = true;
            ignoreNextCaretEvent_ = true;
            int line = Document.GetLineByOffset(offset).LineNumber;

            if (line > 2) {
                line -= 2;
            }

            ScrollToLine(line);
        }

        public void BringElementIntoView(IRElement op,
                                         BringIntoViewStyle style = BringIntoViewStyle.Default) {
            ignoreNextHoverEvent_ = true;
            ignoreNextCaretEvent_ = true;
            int line = Document.GetLineByOffset(op.TextLocation.Offset).LineNumber;

            if (style == BringIntoViewStyle.Default) {
                if (!IsElementOutsideView(op)) {
                    UpdateHighlighting();
                    return;
                }

                ScrollToLine(line);
            }
            else if (style == BringIntoViewStyle.FirstLine) {
                double y = TextArea.TextView.GetVisualTopByDocumentLine(line);
                ScrollToVerticalOffset(y);
            }

            UpdateHighlighting();
        }

        public void ClearMarkedElement(IRElement element) {
            markedHighlighter_.Remove(element);
            ClearTemporaryHighlighting();
            UpdateHighlighting();
            RaiseElementRemoveHighlightingEvent(element);
        }

        public void ClearMarkedElementAt(Point point) {
            var element = FindPointedElement(point, out _);

            if (element != null) {
                ClearMarkedElement(element);
            }
        }

        public void ClearMarkedElementAt(int offset) {
            var element = FindElementAtOffset(offset);

            if (element != null) {
                ClearMarkedElement(element);
            }
        }

        public void GoToBlock(BlockIR block) {
            SelectElement(block);
            BringElementIntoView(block);
            currentBlock_ = block;
        }

        public BlockIR GoToNextBlock() {
            int index = function_.Blocks.IndexOf(currentBlock_);

            if (index + 1 < function_.Blocks.Count) {
                GoToBlock(function_.Blocks[index + 1]);
            }

            return currentBlock_;
        }

        public BlockIR GoToPredecessorBlock() {
            return currentBlock_;
        }

        public BlockIR GoToPreviousBlock() {
            int index = function_.Blocks.IndexOf(currentBlock_);

            if (index == -1) {
                return currentBlock_;
            }
            else if (index > 0 && function_.Blocks.Count > 0) {
                GoToBlock(function_.Blocks[index - 1]);
            }

            return currentBlock_;
        }

        private ElementHighlighter GetHighlighter(HighlighingType type) {
            return type switch
            {
                HighlighingType.Hovered => hoverHighlighter_,
                HighlighingType.Selected => selectedHighlighter_,
                HighlighingType.Marked => markedHighlighter_,
                _ => throw new Exception("Unknown type")
            };
        }

        public void HighlightElement(IRElement element, HighlighingType type) {
            HighlightSingleElement(element, GetHighlighter(type));
        }

        public void HighlightElementsOnLine(int lineNumber) {
            ClearTemporaryHighlighting();
            var group = new HighlightedGroup(selectedStyle_);
            IRElement firstTuple = null;

            foreach (var block in function_.Blocks) {
                foreach (var tuple in block.Tuples) {
                    var sourceTag = tuple.GetTag<SourceLocationTag>();

                    if (sourceTag != null && sourceTag.Line == lineNumber) {
                        group.Add(tuple);

                        if (firstTuple == null) {
                            firstTuple = tuple;
                        }
                    }
                }
            }

            if (!group.IsEmpty()) {
                selectedHighlighter_.Add(group);
                BringElementIntoView(firstTuple);
            }

            UpdateHighlighting();
        }

        public void MarkElementWithDefaultStyle(IRElement element) {
            HighlightSingleElement(element, markedHighlighter_);
        }

        public void InitializeBasedOnDocument(string text, IRDocument doc) {
            InitializeFromDocument(doc, text);
        }

        public void UnloadDocument() {
            section_ = null;
            function_ = null;
            Text = "";
            hoverHighlighter_?.Clear();
            selectedHighlighter_?.Clear();
            markedHighlighter_?.Clear();
            diffHighlighter_?.Clear();
            bookmarks_?.Clear();
            margin_?.Reset();
        }

        public void InitializeFromDocument(IRDocument doc, string text = null) {
            if (section_ == doc.section_) {
                return;
            }

            UnloadDocument();
            Settings = doc.Settings;
            section_ = doc.section_;
            function_ = doc.function_;
            blockElements_ = doc.blockElements_;
            tupleElements_ = doc.tupleElements_;
            operandElements_ = doc.operandElements_;
            hoverHighlighter_.CopyFrom(doc.hoverHighlighter_);
            selectedHighlighter_.CopyFrom(doc.selectedHighlighter_);
            markedHighlighter_.CopyFrom(doc.markedHighlighter_);
            bookmarks_.CopyFrom(doc.bookmarks_);
            margin_.CopyFrom(doc.margin_);
            ignoreNextCaretEvent_ = true;

            if (text != null) {
                Document.Text = text;
            }
            else {
                Document.Text = doc.Document.Text;
            }
        }

        public bool JumpToBookmark(Bookmark bookmark) {
            if (bookmark != null) {
                margin_.SelectBookmark(bookmark);
                BringElementIntoView(bookmark.Element);
                RaiseBookmarkSelectedEvent(bookmark);
                return true;
            }

            return false;
        }

        public async Task LoadSavedSection(ParsedSection parsedSection, IRDocumentState savedState) {
            Trace.TraceInformation(
                $"Document {ObjectTracker.Track(this)}: Load saved section {parsedSection}");

            selectedElements_ = savedState.selectedElements_?.ToHashSet(item => (IRElement)item) ??
                                new HashSet<IRElement>();

            bookmarks_.LoadState(savedState.bookmarks_, Function);
            hoverHighlighter_.LoadState(savedState.hoverHighlighter_, Function);
            selectedHighlighter_.LoadState(savedState.selectedHighlighter_, Function);
            markedHighlighter_.LoadState(savedState.markedHighlighter_, Function);
            margin_.LoadState(savedState.margin_);
            bookmarks_.Bookmarks.ForEach(item => RaiseBookmarkAddedEvent(item));
            SetCaretAtOffset(savedState.caretOffset_);
            await LateLoadSectionSetup(parsedSection);
        }

        public async Task LoadSection(ParsedSection parsedSection) {
            Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Load section {parsedSection}");
            SetCaretAtOffset(0);
            await LateLoadSectionSetup(parsedSection);
        }

        //? TODO: This is a more efficient way of marking the loop blocks
        //? by using a single group that covers all blocks in a loop
        //private void MarkLoopNest(Loop loop, HashSet<BlockIR> handledBlocks) {
        //    foreach(var nestedLoop in loop.NestedLoops) {
        //        MarkLoopNest(nestedLoop, handledBlocks);
        //    }

        //    var gr = new GraphRenderer(null); //? TODO: Remove
        //    HighlightedGroup group = null;

        //    foreach (var block in loop.Blocks) {
        //        if(handledBlocks.Contains(block)) {
        //            continue; // Block already marked as part of a nested loop.
        //        }

        //        handledBlocks.Add(block);

        //        if(group == null) {
        //            group = new HighlightedGroup(gr.GetDefaultBlockStyle(block));
        //        }

        //        group.Elements.Add(block);
        //    }

        //    if (group != null) {
        //        margin_.AddBlock(group);
        //    }
        //}

        public void MarkBlock(IRElement element, HighlightingStyle style, bool raiseEvent = true) {
            Trace.TraceInformation(
                $"Document {ObjectTracker.Track(this)}: Mark block {((BlockIR)element).Number}");

            var group = new HighlightedGroup(element, style);
            margin_.AddBlock(group);
            RecordReversibleAction(DocumentActionKind.MarkElement, element);
            UpdateMargin();
            UpdateHighlighting();

            if (raiseEvent) {
                RaiseElementHighlightingEvent(element, group, markedHighlighter_.Type,
                                              HighlightingEventAction.ReplaceHighlighting);
            }
        }

        public void MarkElement(IRElement element, Color selectedColor) {
            var style = new HighlightingStyle(selectedColor, null);

            var pairStyle = new PairHighlightingStyle {
                ChildStyle = style,
                ParentStyle = style
            };

            MarkElement(element, pairStyle);
        }

        public void MarkElement(IRElement element, HighlightingStyle style, bool raiseEvent = true) {
            Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Mark element {element}");
            RecordReversibleAction(DocumentActionKind.MarkElement, element);
            ClearTemporaryHighlighting();
            var group = new HighlightedGroup(element, style);
            markedHighlighter_.Remove(element);
            markedHighlighter_.Add(group);
            UpdateHighlighting();

            if (raiseEvent) {
                RaiseElementHighlightingEvent(element, group, markedHighlighter_.Type,
                                              HighlightingEventAction.AppendHighlighting);
            }
        }

        public void MarkElementAt(Point point, Color selectedColor) {
            var element = FindPointedElement(point, out _);

            if (element != null) {
                MarkElement(element, selectedColor);
            }
        }

        public void MarkElementAt(int offset, Color selectedColor) {
            var element = FindElementAtOffset(offset);

            if (element != null) {
                MarkElement(element, selectedColor);
            }
        }

        private IRElement CreateDummyElement(int offset, int length) {
            int line = Document.GetLineByOffset(offset).LineNumber;
            var location = new TextLocation(offset, line, 0);
            return new IRElement(location, length);
        }

        public void MarkTextRange(int offset, int length, Color color) {
            var style = new HighlightingStyle(color, Pens.GetPen(Colors.DarkGray));
            var element = CreateDummyElement(offset, length);
            var group = new HighlightedGroup(element, style);
            markedHighlighter_.Add(group);
            UpdateHighlighting();
        }

        public void MarkSearchResults(List<TextSearchResult> results, Color color) {
            if (searchResultsGroup_ != null) {
                markedHighlighter_.Remove(searchResultsGroup_);
                searchResultsGroup_ = null;

                if (currentSearchResultGroup_ != null) {
                    markedHighlighter_.Remove(currentSearchResultGroup_);
                    currentSearchResultGroup_ = null;
                }
            }

            if (results.Count == 0) {
                UpdateHighlighting();
                return;
            }

            var style = new HighlightingStyle(color, Pens.GetPen(Colors.DarkGray));
            searchResultMap_ = new Dictionary<TextSearchResult, IRElement>();
            searchResultsGroup_ = new HighlightedGroup(style);

            foreach (var result in results) {
                var element = CreateDummyElement(result.Offset, result.Length);
                searchResultsGroup_.Add(element);
                searchResultMap_[result] = element;
            }

            markedHighlighter_.Add(searchResultsGroup_, false);
            ClearTemporaryHighlighting();
            UpdateHighlighting();
        }

        public void JumpToSearchResult(TextSearchResult result, Color color) {
            if (currentSearchResultGroup_ != null) {
                markedHighlighter_.Remove(currentSearchResultGroup_);
                searchResultsGroup_.Add(currentSearchResultGroup_.Elements[0]);
            }

            var style = new HighlightingStyle(color, Pens.GetPen(Colors.Black));
            currentSearchResultGroup_ = new HighlightedGroup(style);
            var element = searchResultMap_[result];
            searchResultsGroup_.Remove(element);
            currentSearchResultGroup_.Add(element);
            markedHighlighter_.Add(currentSearchResultGroup_, false);
            BringElementIntoView(element);
            ClearTemporaryHighlighting();
            UpdateHighlighting();
        }

        public void RemoveAllBookmarks() {
            bookmarks_.Clear();
            margin_.ClearBookmarks();
            UpdateMargin();
            UpdateHighlighting();
            RaiseBookmarkListClearedEvent();
        }

        public void RemoveBookmark(Bookmark bookmark) {
            bookmarks_.RemoveBookmark(bookmark);
            margin_.RemoveBookmark(bookmark);
            UpdateMargin();
            UpdateHighlighting();
            RaiseBookmarkRemovedEvent(bookmark);
        }

        public IRDocumentState SaveState() {
            var savedState = new IRDocumentState();
            savedState.selectedElements_ = selectedElements_.ToList(item => new IRElementReference(item));
            savedState.bookmarks_ = bookmarks_.SaveState(Function);
            savedState.hoverHighlighter_ = hoverHighlighter_.SaveState(Function);
            savedState.selectedHighlighter_ = selectedHighlighter_.SaveState(Function);
            savedState.markedHighlighter_ = markedHighlighter_.SaveState(Function);
            savedState.margin_ = margin_.SaveState();
            savedState.caretOffset_ = TextArea.Caret.Offset;
            return savedState;
        }

        public void SelectElement(IRElement element, bool raiseEvent = true, bool fromUICommand = false,
                                  int textOffset = -1) {
            Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Select element {element}");
            ClearTemporaryHighlighting();

            if (element != null) {
                // Don't highlight a block unless the header is selected.
                if (element is BlockIR && fromUICommand && textOffset != -1) {
                    int line = Document.GetLineByOffset(textOffset).LineNumber;
                    int blockStartLine = element.TextLocation.Line;

                    if (line - 1 != blockStartLine) {
                        UpdateHighlighting();
                        return;
                    }
                }

                bool markExpression = fromUICommand && Utils.IsControlModifierActive();
                bool markReferences = fromUICommand && Utils.IsShiftModifierActive();
                HandleElement(element, selectedHighlighter_, markExpression, markReferences);
                AddSelectedElement(element, raiseEvent);

                if (fromUICommand) {
                    ignoreNextCaretEvent_ = true;
                    MirrorAction(DocumentActionKind.SelectElement, element);
                }
            }
            else if (raiseEvent) {
                // Notify of no element being selected.
                RaiseElementUnselectedEvent();

                RaiseElementHighlightingEvent(null, null, HighlighingType.Selected,
                                              HighlightingEventAction.ReplaceHighlighting);
            }

            UpdateHighlighting();
        }

        public void UnselectElements() {
            ClearSelectedElements();
            UpdateHighlighting();
        }

        public void SelectElementAt(Point point) {
            var element = FindPointedElement(point, out _);

            if (element != null) {
                SelectElement(element);
            }
        }

        public void UnmarkBlock(IRElement element, HighlighingType type) {
            margin_.RemoveBlock(element);
            UpdateMargin();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e) {
            if (margin_.SelectedBookmark != null) {
                e.Handled = EditBookmarkText(margin_.SelectedBookmark, e.Key);
                return;
            }

            switch (e.Key) {
                case Key.Return: {
                        if (Utils.IsShiftModifierActive()) {
                            PeekDefinitionExecuted(this, null);
                        }
                        else if (Utils.IsControlModifierActive()) {
                            GoToDefinitionSkipCopiesExecuted(this, null);
                        }
                        else {
                            GoToDefinitionExecuted(this, null);
                        }

                        e.Handled = true;
                        break;
                    }
                case Key.Escape: {
                        HideTooltip();
                        e.Handled = true;
                        break;
                    }
                case Key.M: {
                        if (Utils.IsControlModifierActive()) {
                            if (Utils.IsShiftModifierActive()) {
                                MarkBlockExecuted(this, null);
                            }
                            else {
                                MarkExecuted(this, null);
                            }
                        }

                        e.Handled = true;
                        break;
                    }
                case Key.D: {
                        if (Utils.IsControlModifierActive()) {
                            if (Utils.IsShiftModifierActive()) {
                                MarkDefinitionBlockExecuted(this, null);
                            }
                            else {
                                MarkDefinitionExecuted(this, null);
                            }
                        }

                        e.Handled = true;
                        break;
                    }
                case Key.U: {
                        if (Utils.IsControlModifierActive()) {
                            if (Utils.IsShiftModifierActive()) {
                                MarkUsesExecuted(this, null);
                            }
                            else {
                                ShowUsesExecuted(this, null);
                            }
                        }

                        e.Handled = true;
                        break;
                    }
                case Key.Delete: {
                        if (Utils.IsControlModifierActive()) {
                            if (Utils.IsShiftModifierActive()) {
                                ClearAllMarkersExecuted(this, null);
                            }
                            else {
                                ClearMarkerExecuted(this, null);
                            }
                        }

                        e.Handled = true;
                        break;
                    }
                case Key.B: {
                        if (Utils.IsControlModifierActive()) {
                            if (Utils.IsShiftModifierActive()) {
                                RemoveBookmarkExecuted(this, null);
                            }
                            else {
                                AddBookmarkExecuted(this, null);
                            }
                        }

                        e.Handled = true;
                        break;
                    }
                case Key.R: {
                        if (Utils.IsControlModifierActive()) {
                            if (Utils.IsShiftModifierActive()) {
                                MarkReferencesExecuted(this, null);
                            }
                            else {
                                ShowReferencesExecuted(this, null);
                            }
                        }

                        e.Handled = true;
                        break;
                    }
                case Key.F2: {
                        if (Utils.IsShiftModifierActive()) {
                            PreviousBookmarkExecuted(this, null);
                        }
                        else {
                            NextBookmarkExecuted(this, null);
                        }

                        e.Handled = true;
                        break;
                    }
                case Key.Down: {
                        if (Utils.IsControlModifierActive()) {
                            GoToNextBlock();
                            e.Handled = true;
                        }

                        break;
                    }
                case Key.Up: {
                        if (Utils.IsControlModifierActive()) {
                            GoToPreviousBlock();
                            e.Handled = true;
                        }

                        break;
                    }
                case Key.Z: {
                        if (Utils.IsControlModifierActive()) {
                            UndoReversibleAction();

                            if (Utils.IsAltModifierActive()) {
                                MirrorAction(DocumentActionKind.UndoAction, null);
                            }

                            e.Handled = true;
                        }

                        break;
                    }
                case Key.E: {
                        ShowExpressionGraphExecuted(this, null);
                        e.Handled = true;
                        break;
                    }
            }

            base.OnPreviewKeyDown(e);
        }

        public void PanelContentLoaded(IToolPanel panel) {
            if (panel.PanelKind == ToolPanelKind.FlowGraph ||
                panel.PanelKind == ToolPanelKind.DominatorTree ||
                panel.PanelKind == ToolPanelKind.PostDominatorTree) {
                // Mark the nodes in the graph panel.
                foreach (var blockGroup in margin_.BlockGroups) {
                    if (!blockGroup.SavesStateToFile) {
                        continue;
                    }

                    foreach (var element in blockGroup.Group.Elements) {
                        RaiseElementHighlightingEvent(element, blockGroup.Group, markedHighlighter_.Type,
                                                      HighlightingEventAction.ReplaceHighlighting);
                    }
                }
            }
        }

        private void AddBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            var selectedElement = GetSelectedElement();

            if (selectedElement != null) {
                var bookmark = bookmarks_.AddBookmark(selectedElement);
                margin_.AddBookmark(bookmark);
                margin_.SelectBookmark(bookmark);
                UpdateMargin();
                UpdateHighlighting();
                RaiseBookmarkAddedEvent(bookmark);
            }
        }

        private void AddCommand(RoutedUICommand command, ExecutedRoutedEventHandler handler,
                                CanExecuteRoutedEventHandler canExecuteHandler = null) {
            var binding = new CommandBinding(command);
            binding.Executed += handler;

            if (canExecuteHandler != null) {
                binding.CanExecute += canExecuteHandler;
            }

            CommandBindings.Add(binding);
        }

        private void AddSelectedElement(IRElement element, bool raiseEvent) {
            selectedElements_.Add(element);
            var previousBlock = currentBlock_;
            currentBlock_ = element.ParentBlock;

            if (raiseEvent) {
                RaiseElementSelectedEvent(element);

                if (currentBlock_ != previousBlock) {
                    RaiseBlockSelectedEvent(currentBlock_);
                }
            }
        }

        private void B_MouseLeave(object sender, MouseEventArgs e) {
            if (hoveredBarElement_ != null) {
                hoveredBarElement_.Style.Border = null;
                hoveredBarElement_ = null;
                RenderMarkerBar();
            }

            HideTemporaryUI();
        }

        private void B_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            HideTemporaryUI();
            var position = e.GetPosition(markerMargin_);
            var barElement = FindMarkerBarlElement(position);

            if (barElement != null && barElement.HandlesInput) {
                ignoreNextPreviewElement_ = barElement.Element;
                ignoreNextBarHover_ = true;
                ignoreNextScrollEvent_ = true;
                BringElementIntoView(barElement.Element);
                e.Handled = true;
            }
        }

        private void B_PreviewMouseMove(object sender, MouseEventArgs e) {
            if (ignoreNextBarHover_) {
                ignoreNextBarHover_ = false;
                return;
            }

            bool needsRendering = false;

            if (hoveredBarElement_ != null) {
                hoveredBarElement_.Style.Border = null;
                hoveredBarElement_ = null;
                needsRendering = true;
            }

            var position = e.GetPosition(markerMargin_);
            var barElement = FindMarkerBarlElement(position);

            if (barElement != null && barElement.HandlesInput) {
                barElement.Style.Border = Pens.GetBoldPen(Colors.Black);
                hoveredBarElement_ = barElement;
                needsRendering = true;
                ShowTooltip(barElement.Element, true);
            }
            else {
                HideTemporaryUI();
            }

            if (needsRendering) {
                RenderMarkerBar(true);
            }
        }

        private void Caret_PositionChanged(object sender, EventArgs e) {
            if (disableCaretEvent_) {
                return;
            }

            if (ignoreNextCaretEvent_) {
                ignoreNextCaretEvent_ = false;
                return;
            }

            Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Change caret to {CaretOffset}");
            var element = FindElementAtOffset(TextArea.Caret.Offset);
            SelectElement(element, true, true, TextArea.Caret.Offset);
        }

        private void ClearAllMarkersExecuted(object sender, ExecutedRoutedEventArgs e) {
            ClearAllMarkers();
        }

        private void ClearAllMarkers() {
            ClearBlockMarkers();
            ClearInstructionMarkers();
        }

        private void ClearBlockMarkersExecuted(object sender, ExecutedRoutedEventArgs e) {
            ClearBlockMarkers();
            MirrorAction(DocumentActionKind.ClearBlockMarkers, null);
        }

        private void ClearBlockMarkers() {
            // Don't respond to block removal events, it changes the list being iterated
            // and all blocks will be removed after that anyway.
            margin_.DisableBlockRemoval = true;
            margin_.ForEachBlockElement(element => { RaiseElementRemoveHighlightingEvent(element); });
            margin_.DisableBlockRemoval = false;
            margin_.ClearMarkers();
            UpdateMargin();
        }

        private void ClearInstructionMarkersExecuted(object sender, ExecutedRoutedEventArgs e) {
            ClearInstructionMarkers();
            MirrorAction(DocumentActionKind.ClearBlockMarkers, null);
        }

        private void ClearInstructionMarkers() {
            markedHighlighter_.ForEachElement(element => { RaiseElementRemoveHighlightingEvent(element); });
            markedHighlighter_.Clear();
            UpdateHighlighting();
        }

        private void ClearMarkerExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (selectedElements_.Count == 1) {
                ClearMarkedElement(GetSelectedElement());
            }
        }

        private void ClearSelectedElements() {
            selectedHighlighter_.Clear();
            selectedElements_.Clear();
        }

        private void ClearTemporaryHighlighting() {
            hoverHighlighter_.Clear();
            ClearSelectedElements();
        }

        private void CreateRightMarkerMargin() {
            if (markerMargin_ != null) {
                return; // Margin already set up.
            }

            // Find the right scrollbar and place the canvas under it,
            // then make it semi-transparent so that the canvas is visible.
            var scrollViewer = Utils.FindChild<ScrollViewer>(this);

            if (scrollViewer != null) {
                docVerticalScrollbar_ = Utils.FindChild<ScrollBar>(scrollViewer);

                if (docVerticalScrollbar_ != null) {
                    docVerticalScrollbar_.Opacity = 0.6;
                    docVerticalScrollbar_.PreviewMouseMove += B_PreviewMouseMove;
                    docVerticalScrollbar_.PreviewMouseLeftButtonDown += B_PreviewMouseLeftButtonDown;
                    docVerticalScrollbar_.MouseLeave += B_MouseLeave;
                    var parent = VisualTreeHelper.GetParent(docVerticalScrollbar_) as Grid;
                    markerMargin_ = new Canvas();
                    Panel.SetZIndex(markerMargin_, 1);
                    Panel.SetZIndex(docVerticalScrollbar_, 2);
                    Grid.SetColumn(markerMargin_, Grid.GetColumn(docVerticalScrollbar_));
                    markerMargin_.Background = Brushes.White;
                    parent.Children.Add(markerMargin_);
                }
            }
        }

        public void EarlyLoadSectionSetup(ParsedSection parsedSection) {
            Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Start setup for {parsedSection}");
            duringSectionLoading_ = true;
            section_ = parsedSection.Section;
            function_ = parsedSection.Function;
            ignoreNextCaretEvent_ = true;

            Document.Text = parsedSection.Text;

            bookmarks_.Clear();
            hoverHighlighter_.Clear();
            selectedHighlighter_.Clear();
            markedHighlighter_.Clear();
            blockHighlighter_.Clear();
            diffHighlighter_?.Clear();
            remarkHighlighter_.Clear();
            margin_.Reset();
        }

        private bool EditBookmarkText(Bookmark bookmark, Key key) {
            // Because a TextBox is not used for the bookmarks,
            // editing the optionala text must be handled manually.
            var keyInfo = Utils.KeyToChar(key);

            if (keyInfo.IsLetter) {
                // Append a new letter.
                string keyString = keyInfo.Letter.ToString();
                string text = bookmark.Text;

                if (text == null) {
                    text = keyString;
                }
                else {
                    text += keyString;
                }

                bookmark.Text = text;
                margin_.SelectedBookmarkChanged();
                RaiseBookmarkChangedEvent(bookmark);
            }
            else if (key == Key.Back) {
                // Remove last letter.
                string text = bookmark.Text;

                if (!string.IsNullOrEmpty(text)) {
                    text = text.Substring(0, text.Length - 1);
                    bookmark.Text = text;
                    margin_.SelectedBookmarkChanged();
                    RaiseBookmarkChangedEvent(bookmark);
                }
            }
            else if (key == Key.Return) {
                if (Utils.IsKeyboardModifierActive()) {
                    bookmark.IsPinned = true;
                }

                margin_.UnselectBookmark();
                margin_.SelectedBookmarkChanged();
                RaiseBookmarkChangedEvent(bookmark);
            }
            else if (key == Key.Delete) {
                // Delete all text.
                bookmark.Text = "";
                margin_.SelectedBookmarkChanged();
                RaiseBookmarkChangedEvent(bookmark);
            }
            else if (key == Key.F2) {
                if (Utils.IsShiftModifierActive()) {
                    PreviousBookmarkExecuted(this, null);
                }
                else {
                    NextBookmarkExecuted(this, null);
                }

                return true;
            }
            else if (keyInfo.IsShift && keyInfo.IsControl) {
                if (keyInfo.Letter == 'B') {
                    RemoveBookmark(bookmark);
                }
            }
            else if (!Utils.IsKeyboardModifierActive()) {
                margin_.UnselectBookmark();
                margin_.SelectedBookmarkChanged();
                return false;
            }

            return true;
        }

        private IRElement FindElementAtOffset(int offset) {
            // Exit if the element lists are still being computed.
            if (duringSectionLoading_) {
                return null;
            }

            IRElement element;

            if (DocumentUtils.FindElement(offset, operandElements_, out element) ||
                DocumentUtils.FindElement(offset, tupleElements_, out element) ||
                DocumentUtils.FindElement(offset, blockElements_, out element)) {
                return element;
            }

            return null;
        }

        private MarkerBarElement FindMarkerBarlElement(Point position) {
            if (markerMargingElements_ != null) {
                //? TODO: This is inefficient, at least sort by Y and do binary search
                foreach (var barElement in markerMargingElements_) {
                    if (barElement.Element == null) {
                        continue;
                    }

                    if (barElement.Visual.Contains(position)) {
                        return barElement;
                    }
                    else if (barElement.Visual.Top > position.Y) {
                        break;
                    }
                }
            }

            return null;
        }

        private IRElement FindPointedElement(Point position, out int textOffset) {
            int offset = DocumentUtils.GetOffsetFromMousePosition(position, this, out _);

            if (offset != -1) {
                textOffset = offset;
                return FindElementAtOffset(offset);
            }

            textOffset = -1;
            return null;
        }

        public IRElement GetElementAt(Point position) {
            int offset = DocumentUtils.GetOffsetFromMousePosition(position, this, out _);
            return offset != -1 ? FindElementAtOffset(offset) : null;
        }

        private void FirstBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            JumpToBookmark(bookmarks_.JumpToFirstBookmark());
        }

        private HighlightingStyle GetMarkerStyleForCommand(ExecutedRoutedEventArgs e) {
            if (e != null && e.Parameter is ColorEventArgs colorArgs) {
                return new HighlightingStyle(colorArgs.SelectedColor);
            }

            return PickMarkerStyle();
        }

        private PairHighlightingStyle GetPairMarkerStyleForCommand(ExecutedRoutedEventArgs e) {
            if (e != null && e.Parameter is ColorEventArgs colorArgs) {
                var style = new HighlightingStyle(colorArgs.SelectedColor);

                return new PairHighlightingStyle {
                    ChildStyle = new HighlightingStyle(colorArgs.SelectedColor),
                    ParentStyle =
                        new HighlightingStyle(Utils.ChangeColorLuminisity(colorArgs.SelectedColor, 1.4))
                };
            }

            return PickPairMarkerStyle();
        }

        private PairHighlightingStyle GetReferenceStyle(Reference reference) {
            //? TODO: Make single instance styles
            return reference.Kind switch
            {
                ReferenceKind.Address => new PairHighlightingStyle {
                    ChildStyle = new HighlightingStyle("#FF9090", Pens.GetBoldPen(Colors.DarkRed)),
                    ParentStyle = new HighlightingStyle("#FFC9C9")
                },
                ReferenceKind.Load => new PairHighlightingStyle {
                    ChildStyle = new HighlightingStyle("#BDBAEC", Pens.GetPen(Colors.DarkBlue)),
                    ParentStyle = new HighlightingStyle("#D9D8F4")
                },
                ReferenceKind.Store => ssaUserStyle_,
                ReferenceKind.SSA => new PairHighlightingStyle {
                    ChildStyle = new HighlightingStyle("#BAD6EC", Pens.GetPen(Colors.DarkBlue)),
                    ParentStyle = new HighlightingStyle("#D8E9F4")
                },
                _ => throw new InvalidOperationException("Unknown ReferenceKind")
            };
        }

        public IRElement TryGetSelectedElement() {
            return selectedElements_.Count > 0 ? GetSelectedElement() : null;
        }

        private IRElement GetSelectedElement() {
            var selectedEnum = selectedElements_.GetEnumerator();
            selectedEnum.MoveNext();
            return selectedEnum.Current;
        }

        private OperandIR GetSelectedElementDefinition() {
            var selectedOp = GetSelectedElement();

            if (selectedOp is OperandIR op) {
                var defOp = ReferenceFinder.GetSSADefinition(op) as OperandIR;

                if (defOp != null) {
                    return defOp;
                }

                // For an indirection, look for the base value.
                if (op.IsIndirection) {
                    op = op.IndirectionBaseValue;
                }

                //? TODO: Could use block reachability to trim the set of stores marked
                //? Reachability can be integrated directly into ReferenceFinder,
                //? refs.FindAllReachableStores(op, block)
                var storeList = new ReferenceFinder(function_).FindAllStores(op);

                if (storeList.Count == 1) {
                    return storeList[0].Element as OperandIR;
                }
            }

            return null;
        }

        private void UndoActionExecuted(object sender, ExecutedRoutedEventArgs e) {
            UndoReversibleAction();
            e.Handled = true;
        }

        private void UndoActionCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = actionUndoStack_.Count > 0;
            e.Handled = true;
        }

        private void GoToDefinitionExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (selectedElements_.Count == 1) {
                var element = GetSelectedElement();
                GoToElementDefinition(element);
                MirrorAction(DocumentActionKind.GoToDefinition, element);
            }
        }

        private void GoToDefinitionSkipCopiesExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (selectedElements_.Count == 1) {
                var element = GetSelectedElement();
                GoToElementDefinition(element, true);
                MirrorAction(DocumentActionKind.GoToDefinition, element);
            }
        }

        private IRElement SkipAllCopies(IRElement op) {
            //? TODO: This should be in some IR utility class.
            var defInstr = op.ParentInstruction;

            while (defInstr != null) {
                var sourceOp = Session.CompilerInfo.IR.SkipCopyInstruction(defInstr);

                if (sourceOp != null) {
                    op = sourceOp;
                    defInstr = sourceOp.ParentInstruction;
                }
                else {
                    break;
                }
            }

            return op;
        }

        private bool GoToElementDefinition(IRElement element, bool skipCopies = false) {
            RecordReversibleAction(DocumentActionKind.GoToDefinition, element);
            ClearTemporaryHighlighting();

            if (element == null) {
                return false;
            }

            if (element is OperandIR op) {
                // If it's a destination operand and has a single user, jump to it.
                if (op.Role == OperandRole.Destination) {
                    var useOp = ReferenceFinder.GetSingleUse(op);

                    if (useOp != null) {
                        HighlightSingleElement(useOp, selectedHighlighter_);
                        SetCaretAtElement(useOp);
                        return true;
                    }
                }

                // Try to find a definition for the source operand.
                var defOp = ReferenceFinder.GetSSADefinition(op);

                if (defOp != null) {
                    if (skipCopies) {
                        defOp = SkipAllCopies(defOp);
                    }

                    HighlightSingleElement(defOp, selectedHighlighter_);
                    SetCaretAtElement(defOp);
                    return true;
                }
                else if (op.IsLabelAddress) {
                    // For labels go to the target block.
                    var targetOp = op.BlockLabelValue;
                    HighlightSingleElement(targetOp, selectedHighlighter_);
                    SetCaretAtElement(targetOp);
                    return true;
                }
                else {
                    // Try to use reference info to find an unique definition.
                    //? TODO: Definition set can be refined using reaching defs DFA.
                    defOp = new ReferenceFinder(function_).FindDefinition(element);

                    if (defOp != null) {
                        HighlightSingleElement(defOp, selectedHighlighter_);
                        SetCaretAtElement(defOp);
                        return true;
                    }
                }
            }
            else if (element is InstructionIR instr) {
                // For single-source instructions, go to the source definition.
                if (instr.Sources.Count == 1) {
                    GoToElementDefinition(instr.Sources[0]);
                }
            }

            return false;
        }

        private void HandleElement(IRElement element, ElementHighlighter highlighter,
                                   bool markExpression, bool markReferences) {
            var action = HighlightingEventAction.ReplaceHighlighting;
            bool highlighted = false;

            if (element is OperandIR op) {
                highlighted = HandleOperandElement(op, highlighter, markExpression,
                                                   markReferences, action);
            }
            else if (element is InstructionIR instr) {
                highlighted = HandleInstructionElement(instr, highlighter, ref action);
            }

            if (!highlighted) {
                HandleOtherElement(element, highlighter, action);
            }
        }

        private bool HandleInstructionElement(InstructionIR instr, ElementHighlighter highlighter,
                                              ref HighlightingEventAction action) {
            if (!settings_.HighlightInstructionOperands) {
                return false;
            }

            // Highlight each source operand. The instruction itself
            // is also highlighted below, in append mode.
            action = HighlightingEventAction.AppendHighlighting;
            RaiseRemoveHighlightingEvent(highlighter.Type);
            HandleOtherElement(instr, highlighter, ssaDefinitionStyle_.ParentStyle, action);

            foreach (var sourceOp in instr.Sources) {
                if (sourceOp.IsLabelAddress) {
                    HighlightBlockLabel(sourceOp, highlighter, ssaUserStyle_, action);
                }
                else {
                    HighlightDefinition(sourceOp, highlighter, ssaDefinitionStyle_, action,
                                           false);
                }
            }

            return true;
        }

        private bool HandleOperandElement(OperandIR op, ElementHighlighter highlighter,
                                          bool markExpression, bool markReferences,
                                          HighlightingEventAction action) {
            if (op.Role == OperandRole.Source) {
                if (markExpression) {
                    // Mark an entire SSA def-use expression DAG.
                    HighlightSSAExpression(op, highlighter, expressionOperandStyle_, expressionStyle_);
                    return true;
                }

                // Further handling of sources is done below.
            }
            else if ((op.Role == OperandRole.Destination || op.Role == OperandRole.Parameter) &&
                     settings_.HighlightDestinationUses) {
                // First look for an SSA definition and its uses,
                // if not found  highlight every load of the same symbol.
                var useList = ReferenceFinder.FindSSAUses(op);
                bool handled = false;

                if (useList.Count == 0) {
                    //? TODO: Could use block reachability to trim the set of loads marked
                    useList = new ReferenceFinder(function_).FindAllLoads(op);
                }
                else if (markExpression) {
                    // Collect the transitive set of users, marking instructions
                    // that depend on the value of this destination operand.
                    ExpandIteratedUseList(useList);
                }

                if (useList != null && useList.Count > 0) {
                    HighlightUsers(op, useList, highlighter, ssaUserStyle_, action);
                    handled = true;
                }

                // If the operand is an indirection, also try to mark 
                // the definition of the base address value below.
                if (!op.IsIndirection) {
                    return handled;
                }
            }

            if (settings_.HighlightSourceDefinition) {
                if (op.IsLabelAddress) {
                    return HighlightBlockLabel(op, highlighter, ssaUserStyle_, action);
                }

                if (markReferences) {
                    MarkReferences(op, highlighter);
                    return true;
                }

                return HighlightDefinition(op, highlighter, ssaDefinitionStyle_, action);
            }

            return false;
        }

        private void ExpandIteratedUseList(List<Reference> useList) {
            var handledElements = new HashSet<IRElement>();

            foreach (var use in useList) {
                handledElements.Add(use.Element);
            }

            ExpandIteratedUseList(useList, handledElements, 0);
        }

        private void ExpandIteratedUseList(List<Reference> useList,
                                           HashSet<IRElement> handledElements, int level) {
            //? TODO: Max level should be configurable
            if (level > 8) {
                return;
            }

            var newUseLists = new List<List<Reference>>();

            foreach (var use in useList) {
                if (use.Element is OperandIR op) {
                    var useInstr = op.ParentInstruction;

                    if (useInstr != null) {
                        foreach (var iteratedUse in ReferenceFinder.FindSSAUses(useInstr)) {
                            if (!handledElements.Add(iteratedUse.Element)) {
                                continue; /// Use already visited during recursion.
                            }

                            // Recursively iterate over and collect uses
                            var iteratedUseList = new List<Reference>();
                            iteratedUseList.Add(iteratedUse);

                            ExpandIteratedUseList(iteratedUseList, handledElements, level + 1);
                            newUseLists.Add(iteratedUseList);
                        }
                    }
                }
            }

            // Merge the children lists into the input list.
            // This is fairly inefficient, but not an issue with the max. level used.
            foreach (var list in newUseLists) {
                useList.AddRange(list);
            }
        }

        private void HandleOtherElement(IRElement element, ElementHighlighter highlighter,
                                        HighlightingEventAction action) {
            var style = element is BlockIR ? selectedBlockStyle_ : selectedStyle_;
            HandleOtherElement(element, highlighter, style, action);
        }

        private void HandleOtherElement(IRElement element, ElementHighlighter highlighter,
                                        HighlightingStyle style, HighlightingEventAction action) {
            var group = new HighlightedGroup(element, style);
            highlighter.Add(group);
            RaiseElementHighlightingEvent(element, group, highlighter.Type, action);
        }

        private void HideTemporaryUI() {
            HideTooltip();
            margin_.UnselectBookmark();
        }

        private void HideTooltip() {
            ignoreNextPreviewElement_ = null;

            if (nodeToolTip_ != null) {
                nodeToolTip_.Hide();
                nodeToolTip_ = null;
            }
        }

        private bool HighlightBlockLabel(OperandIR op, ElementHighlighter highlighter,
                                         PairHighlightingStyle style, HighlightingEventAction action) {
            var labelOp = op.BlockLabelValue;

            if (labelOp?.Parent == null) {
                return false;
            }

            var group = new HighlightedGroup(style.ChildStyle);
            group.Add(op);
            group.Add(labelOp);
            highlighter.Add(group);
            RaiseElementHighlightingEvent(op, group, highlighter.Type, action);
            return true;
        }

        private HighlightedGroup HighlightDefinedOperand(IRElement op, IRElement defElement,
                                                         ElementHighlighter highlighter,
                                                         PairHighlightingStyle style) {
            var group = new HighlightedGroup(style.ChildStyle);
            group.Add(op);
            group.Add(defElement);
            highlighter.Add(group);
            return group;
        }

        private HighlightedGroup HighlightInstruction(InstructionIR instr, ElementHighlighter highlighter,
                                                      PairHighlightingStyle style) {
            var group = new HighlightedGroup(instr, style.ParentStyle);
            highlighter.Add(group);
            return group;
        }

        private HighlightedGroup HighlightOperand(IRElement op, ElementHighlighter highlighter,
                                                  PairHighlightingStyle style) {
            var group = new HighlightedGroup(op, style.ChildStyle);
            highlighter.Add(group);
            return group;
        }

        private void HighlightSingleElement(IRElement element, ElementHighlighter highlighter) {
            ClearTemporaryHighlighting();
            highlighter.Clear();

            if (element is OperandIR op && op.Parent != null) {
                // Also highlight operand's parent instruction.
                highlighter.Add(new HighlightedGroup(op.Parent, definitionStyle_.ParentStyle));
            }

            if (element is BlockIR && highlighter.Type == HighlighingType.Hovered) {
                highlighter.Add(new HighlightedGroup(element, selectedBlockStyle_));
            }
            else {
                highlighter.Add(new HighlightedGroup(element, definitionStyle_.ChildStyle));
            }

            BringElementIntoView(element);
        }

        private bool HighlightDefinition(OperandIR op, ElementHighlighter highlighter,
                                         PairHighlightingStyle style, HighlightingEventAction action,
                                         bool highlightDefInstr = true) {
            // First look for an SSA definition, if not found 
            // highlight every store to the same symbol.
            var defList = new List<IRElement>();
            var defElement = ReferenceFinder.GetSSADefinition(op);

            if (defElement != null) {
                defList.Add(defElement);
            }
            else {
                // For an indirection, look for the base value.
                if (op.IsIndirection) {
                    op = op.IndirectionBaseValue;
                }

                //? TODO: Could use block reachability to trim the set of stores marked
                var storeList = new ReferenceFinder(function_).FindAllStores(op);
                defList = storeList.ConvertAll(storeRef => storeRef.Element);
            }

            if (defList.Count == 0) {
                return false;
            }

            // Highlight the definition instructions.
            if (highlightDefInstr) {
                var instrGroup = new HighlightedGroup(style.ParentStyle);

                foreach (var element in defList) {
                    instrGroup.Add(element.ParentTuple);
                }

                highlighter.Add(instrGroup);
            }

            // Highlight the definitions.
            var group = new HighlightedGroup(op, style.ChildStyle);

            foreach (var element in defList) {
                group.Add(element);
            }

            highlighter.Add(group);
            RaiseElementHighlightingEvent(op, group, highlighter.Type, action);
            return true;
        }

        private void HighlightSSAExpression(IRElement element, ElementHighlighter highlighter,
                                            HighlightingStyleCollection style,
                                            HighlightingStyleCollection instrStyle) {
            int styleIndex;
            var locationTag = element.GetTag<SourceLocationTag>();
            var handledElements = new HashSet<IRElement>();

            if (locationTag != null) {
                styleIndex = locationTag.Line % style.Styles.Count;
            }
            else {
                styleIndex = new Random().Next(style.Styles.Count - 1);
            }

            HighlightSSAExpression(element, element, handledElements,
                                   highlighter, style, instrStyle, styleIndex, 0);
        }

        private void HighlightSSAExpression(IRElement element, IRElement parent, HashSet<IRElement> handledElements,
                                            ElementHighlighter highlighter, HighlightingStyleCollection style,
                                            HighlightingStyleCollection instrStyle, int styleIndex,
                                            int level) {
            if (!handledElements.Add(element)) {
                return; // Element already handled during recursion.
            }

            switch (element) {
                case OperandIR op: {
                        if (parent != null) {
                            //? TODO: Add option to mark each level with a brighter color.
                            highlighter.Add(new HighlightedGroup(op, style.ForIndex(styleIndex)));
                        }

                        //? TODO: Max level should be configurable
                        if (level >= 6) {
                            return;
                        }

                        var defTag = op.GetTag<SSADefinitionTag>();

                        if (defTag != null) {
                            if (defTag.Parent is OperandIR defOp) {
                                HighlightSSAExpression(defOp.Parent, parent, handledElements, highlighter, style,
                                                       instrStyle, styleIndex, level);
                            }
                        }
                        else {
                            var sourceDefOp = ReferenceFinder.GetSSADefinition(op);

                            if (sourceDefOp != null) {
                                HighlightSSAExpression(sourceDefOp, parent, handledElements, highlighter, style,
                                                       instrStyle, styleIndex, level);
                            }
                        }

                        break;
                    }
                case InstructionIR instr: {
                        highlighter.Add(new HighlightedGroup(instr, instrStyle.ForIndex(styleIndex)));

                        if (level >= 4) {
                            return;
                        }

                        foreach (var sourceOp in instr.Sources) {
                            HighlightSSAExpression(sourceOp, instr, handledElements, highlighter, style,
                                                   instrStyle, styleIndex, level + 1);
                        }

                        if (level > 0) {
                            foreach (var destOp in instr.Destinations) {
                                HighlightSSAExpression(destOp, parent, handledElements, highlighter, style,
                                                       instrStyle, styleIndex, level + 1);
                            }
                        }

                        break;
                    }
            }
        }

        private void HighlightUsers(OperandIR op, List<Reference> useList, ElementHighlighter highlighter,
                                       PairHighlightingStyle style, HighlightingEventAction action) {
            var instrGroup = new HighlightedGroup(style.ParentStyle);
            var useGroup = new HighlightedGroup(style.ChildStyle);
            useGroup.Add(op);

            foreach (var use in useList) {
                useGroup.Add(use.Element);
                var useInstr = use.Element.ParentTuple;

                if (useInstr != null) {
                    instrGroup.Add(useInstr);
                }
            }

            highlighter.Add(instrGroup);
            highlighter.Add(useGroup);
            RaiseElementHighlightingEvent(op, useGroup, highlighter.Type, action);
        }

        private void IRDocument_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e) {
            var position = e.GetPosition(TextArea.TextView);
            var element = FindPointedElement(position, out _);
            e.Handled = GoToElementDefinition(element, Utils.IsControlModifierActive());
        }

        private void IRDocument_PreviewMouseHover(object sender, MouseEventArgs e) {
            if (ignoreNextHoverEvent_) {
                ignoreNextHoverEvent_ = false;
                return;
            }

            bool highlightElement = settings_.ShowInfoOnHover &&
                                    (!settings_.ShowInfoOnHoverWithModifier || Utils.IsShiftModifierActive());

            var position = e.GetPosition(TextArea.TextView);
            var element = FindPointedElement(position, out _);

            if (element != null) {
                if (removeHoveredAction_ != null) {
                    removeHoveredAction_.Cancel();
                    removeHoveredAction_ = null;
                }

                if (element == hoveredElement_) {
                    return;
                }

                // For hover ignore blocks and instructions, it's too jarring.
                if (!selectedElements_.Contains(element) &&
                    !(element is BlockIR) &&
                    !(element is InstructionIR)) {
                    hoveredElement_ = element;
                    ShowDefinitionPreview(element);

                    if (highlightElement) {
                        hoverHighlighter_.Clear();
                        HandleElement(element, hoverHighlighter_,
                                      markExpression: Utils.IsControlModifierActive(),
                                      markReferences: Utils.IsShiftModifierActive());
                        UpdateHighlighting();
                        return;
                    }
                }
            }

            HideHoverHighlighting();
        }

        private void IRDocument_PreviewMouseHoverStopped(object sender, MouseEventArgs e) {
            removeHoveredAction_ = DelayedAction.StartNew(TimeSpan.FromMilliseconds(1000), () => {
                if (removeHoveredAction_ != null) {
                    removeHoveredAction_ = null;
                    HideHoverHighlighting();
                }
            });

            HideTooltip();
            ignoreNextHoverEvent_ = false;
        }

        private void HideHoverHighlighting() {
            if (hoveredElement_ == null) {
                return;
            }

            RaiseElementHighlightingEvent(null, null, HighlighingType.Hovered,
                                          HighlightingEventAction.ReplaceHighlighting);

            hoverHighlighter_.Clear();
            UpdateHighlighting();
            hoveredElement_ = null;
        }

        private void IRDocument_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            HideTooltip();

            if (ignoreNextScrollEvent_) {
                ignoreNextScrollEvent_ = false;
                e.Handled = true;
            }
        }

        private void IRDocument_PreviewMouseMove(object sender, MouseEventArgs e) {
            ForceCursor = true;
            Cursor = Cursors.Arrow;
            margin_.MouseMoved();
        }

        private void IRDocument_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            Focus();
            HideTemporaryUI();
        }

        private bool IsElementOutsideView(IRElement element) {
            if (!TextArea.TextView.VisualLinesValid) {
                return true;
            }

            int targetLine = element.TextLocation.Line;
            var visualLines = TextArea.TextView.VisualLines;
            int viewStart = visualLines[0].FirstDocumentLine.LineNumber;
            int viewEnd = visualLines[^1].LastDocumentLine.LineNumber;
            return targetLine < viewStart || targetLine > viewEnd;
        }

        private void LastBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            JumpToBookmark(bookmarks_.JumpToLastBookmark());
        }

        private async Task LateLoadSectionSetup(ParsedSection parsedSection) {
            Trace.TraceInformation(
                $"Document {ObjectTracker.Track(this)}: Complete setup for {parsedSection}");

            // Compute the element lists on another thread, for large functions
            // it can be slow would block showing of any text before it's done.
            await Task.Run(() => ComputeElementLists());

            // Folding uses the basic block boundaries.
            actionUndoStack_.Clear();
            SetupBlockHighlighter();
            SetupBlockFolding();
            MarkLoopBlocks();
            CreateRightMarkerMargin();
            UpdateHighlighting();
            NotifyPropertyChanged("Blocks");
            duringSectionLoading_ = false;
        }

        private void SetupBlockHighlighter() {
            // Setup highlighting of block background.
            blockHighlighter_.Clear();

            foreach (var element in blockElements_) {
                blockHighlighter_.Add(element);
            }
        }

        private void ComputeElementLists() {
            blockElements_ = new List<IRElement>(function_.Blocks.Count);
            tupleElements_ = new List<IRElement>(blockElements_.Count * 4);
            operandElements_ = new List<IRElement>(tupleElements_.Count * 2);
            selectedElements_ = new HashSet<IRElement>();

            // Add the function parameters.
            foreach (var param in function_.Parameters) {
                operandElements_.Add(param);
            }

            // Add the elements from the entire function.
            foreach (var block in function_.Blocks) {
                blockElements_.Add(block);

                foreach (var tuple in block.Tuples) {
                    tupleElements_.Add(tuple);

                    if (tuple is InstructionIR instr) {
                        foreach (var op in instr.Destinations) {
                            operandElements_.Add(op);
                        }

                        foreach (var op in instr.Sources) {
                            operandElements_.Add(op);
                        }
                    }
                }
            }

            //? TODO: Automation support.
            automationSelectedIndex_ = 0;
            automationActionIndex_ = 0;
            automationPrevElement_ = null;
        }

        public async Task LoadDiffedFunction(DiffMarkingResult diffResult, IRTextSection newSection) {
            // Take ownership of the text document
            diffResult.DiffDocument.SetOwnerThread(Thread.CurrentThread);
            Document = diffResult.DiffDocument;
            function_ = diffResult.DiffFunction;
            section_ = newSection;

            // Block folding is tied to the document, which may have been replaced.
            UninstallBlockFolding();
            SetupBlockFolding();
            TextArea.IsEnabled = true;
            await LateLoadSectionSetup(null);

            AddDiffTextSegments(diffResult.DiffSegments);
            SetupBlockFolding();
        }

        private void Margin__BookmarkChanged(object sender, Bookmark bookmark) {
            RaiseBookmarkChangedEvent(bookmark);
        }

        private void Margin__BookmarkRemoved(object sender, Bookmark bookmark) {
            RemoveBookmark(bookmark);
        }

        private void MarkBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (selectedElements_.Count == 1) {
                var element = GetSelectedElement().ParentBlock;
                var style = GetMarkerStyleForCommand(e);
                MarkBlock(element, style);
                MirrorAction(DocumentActionKind.MarkBlock, element, style);
            }
        }

        private void MarkDefinitionBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (selectedElements_.Count == 1) {
                var defOp = GetSelectedElementDefinition();

                if (defOp != null) {
                    var element = defOp.ParentBlock;
                    var style = GetMarkerStyleForCommand(e);
                    MarkBlock(element, style);
                    MirrorAction(DocumentActionKind.MarkElement, defOp, style);
                }
            }
        }

        private void MarkDefinitionExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (selectedElements_.Count == 1) {
                var defOp = GetSelectedElementDefinition();

                if (defOp != null) {
                    var style = GetPairMarkerStyleForCommand(e);
                    MarkElement(defOp, style);
                    MirrorAction(DocumentActionKind.MarkElement, defOp, style);
                }
            }
        }

        private void MarkElement(IRElement element, PairHighlightingStyle style) {
            MarkElement(element, style.ChildStyle);
        }

        private void MarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (selectedElements_.Count == 1) {
                var element = GetSelectedElement();
                var style = GetPairMarkerStyleForCommand(e);
                MarkElement(element, style);
                MirrorAction(DocumentActionKind.MarkElement, element, style);
            }
        }

        private void MarkLoopBlocks() {
            var graphStyle = new FlowGraphStyleProvider(GraphKind.FlowGraph, App.Settings.FlowGraphSettings);
            var loopGroups = new Dictionary<HighlightingStyle, HighlightedGroup>();

            foreach (var block in function_.Blocks) {
                var loopTag = block.GetTag<LoopBlockTag>();

                if (loopTag != null) {
                    var style = graphStyle.GetBlockNodeStyle(block);

                    if (!loopGroups.TryGetValue(style, out var group)) {
                        group = new HighlightedGroup(style);
                        loopGroups[style] = group;
                    }

                    group.Add(block);
                }
            }

            foreach (var group in loopGroups.Values) {
                margin_.AddBlock(group, false);
            }

            UpdateMargin();
            UpdateHighlighting();
        }

        private void MarkReferencesExecuted(object sender, ExecutedRoutedEventArgs e) {
            var selectedOp = GetSelectedElement();

            if (selectedOp is OperandIR op) {
                MarkReferences(op, markedHighlighter_);
                MirrorAction(DocumentActionKind.MarkReferences, selectedOp);
            }

            UpdateHighlighting();
        }

        private void MarkReferences(OperandIR op, ElementHighlighter highlighter) {
            if (op.IsIndirection) {
                op = op.IndirectionBaseValue;
            }

            var refFinder = new ReferenceFinder(function_);
            var operandRefs = refFinder.FindAllReferences(op);
            var markedInstrs = new HashSet<InstructionIR>();

            //? TODO: Issue when an instr has multiple operands highlighted (PHI)
            //? the prev ops are covered by the background of the other ops,
            //? plus it wastes time with the redundant highlighting
            ClearTemporaryHighlighting();

            foreach (var operandRef in operandRefs) {
                var style = GetReferenceStyle(operandRef);

                // Highlight instruction.
                var instr = operandRef.Element.ParentInstruction;

                if (instr != null && !markedInstrs.Contains(instr)) {
                    HighlightInstruction(instr, highlighter, style);
                    markedInstrs.Add(instr);
                }

                var group = HighlightOperand(operandRef.Element, highlighter, style);

                RaiseElementHighlightingEvent(operandRef.Element, group, HighlighingType.Marked,
                                              HighlightingEventAction.AppendHighlighting);
            }

            if (highlighter == markedHighlighter_) {
                // Show references in panel.
                Session.FindAllReferences(op, this);
                RecordReversibleAction(DocumentActionKind.MarkReferences, op, operandRefs);
            }
        }

        private void MarkUsesExecuted(object sender, ExecutedRoutedEventArgs e) {
            var defOp = GetSelectedElementDefinition();
            var style = GetPairMarkerStyleForCommand(e);

            if (defOp != null) {
                MarkUses(defOp, style);
                MirrorAction(DocumentActionKind.MarkUses, defOp, style);
            }
        }

        private void MarkUses(OperandIR defOp, PairHighlightingStyle style) {
            ClearTemporaryHighlighting();
            var useList = ReferenceFinder.FindSSAUses(defOp);

            HighlightUsers(defOp, useList, markedHighlighter_, style,
                              HighlightingEventAction.AppendHighlighting);

            UpdateHighlighting();
            Session.FindSSAUses(defOp, this);
            RecordReversibleAction(DocumentActionKind.MarkUses, defOp, useList);
        }

        private void NextBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (!JumpToBookmark(bookmarks_.GetNext())) {
                JumpToBookmark(bookmarks_.JumpToFirstBookmark());
            }
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void PeekDefinitionExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (selectedElements_.Count == 1) {
                ShowTooltip(GetSelectedElement());
            }
        }

        private HighlightingStyle PickMarkerLightStyle() {
            return markerParentStyle_.GetNext();
        }

        private HighlightingStyle PickMarkerStyle() {
            return markerChildStyle_.GetNext();
        }

        private PairHighlightingStyle PickPairMarkerStyle() {
            return new PairHighlightingStyle {
                ParentStyle = markerParentStyle_.GetNext(),
                ChildStyle = markerChildStyle_.GetNext()
            };
        }

        private bool HighlighterVersionChanged(MarkerMarginVersionInfo info) {
            return markedHighlighter_.Version != info.LastMarkedVersion ||
                   selectedHighlighter_.Version != info.LastSelectedVersion ||
                   hoverHighlighter_.Version != info.LastHoveredVersion ||
                   diffHighlighter_.Version != info.LastDiffVersion ||
                   margin_.Version != info.LastBlockMarginVersion ||
                   bookmarks_.Version != info.LastBookmarkVersion;
        }

        private void SaveHighlighterVersion(MarkerMarginVersionInfo info) {
            info.LastMarkedVersion = markedHighlighter_.Version;
            info.LastSelectedVersion = selectedHighlighter_.Version;
            info.LastHoveredVersion = hoverHighlighter_.Version;
            info.LastDiffVersion = diffHighlighter_.Version;
            info.LastBlockMarginVersion = margin_.Version;
            info.LastBookmarkVersion = bookmarks_.Version;
            info.NeedsRedrawing = true;
        }

        //? TODO: Extract all right margin code into own class
        private void PopulateMarkerBar() {
            // Try again to create the margin bar, the scrollbar may not have
            // been visible when the document view was loaded.
            if (markerMargin_ == null) {
                CreateRightMarkerMargin();

                if (markerMargin_ == null) {
                    return;
                }
            }

            if (!HighlighterVersionChanged(highlighterVersion_)) {
                return;
            }

            markerMargingElements_ = new List<MarkerBarElement>(64);
            int arrowButtonHeight = 16;
            int startY = arrowButtonHeight;
            double width = markerMargin_.ActualWidth;
            double height = markerMargin_.ActualHeight;
            double availableHeight = height - arrowButtonHeight * 2;
            int lines = Document.LineCount;
            double dotSize = Math.Max(2, availableHeight / lines);
            double dotWidth = width / 3;
            double markerDotSize = Math.Max(8, availableHeight / lines);
            availableHeight -= dotSize;

            PopulateMarkerBarForHighlighter(markedHighlighter_, startY, width, availableHeight,
                                            dotSize);

            PopulateMarkerBarForHighlighter(selectedHighlighter_, startY, width, availableHeight,
                                            dotSize);

            PopulateMarkerBarForHighlighter(hoverHighlighter_, startY, width, availableHeight,
                                            dotSize);

            PopulateMarkerBarForDiffs(diffHighlighter_, startY, width, availableHeight);
            PopulateMarkerBarForBlocks(startY, width, availableHeight);

            margin_.ForEachBookmark(bookmark => {
                PopulateMarkerBarForBookmark(bookmark, startY, width, height,
                                             dotWidth, dotSize);
            });

            // Sort so that searching for elements can be speed up.
            markerMargingElements_.Sort((a, b) => (int)(a.Visual.Top - b.Visual.Top));
            SaveHighlighterVersion(highlighterVersion_);
            RenderMarkerBar();
        }

        private void PopulateMarkerBarForBookmark(Bookmark bookmark, int startY, double width, double height,
                                                  double dotWidth, double dotHeight) {
            double y = (double)bookmark.Element.TextLocation.Line / LineCount * height;
            var elementVisual = new Rect(width / 3, startY + y, dotWidth, dotHeight);
            var style = bookmark.Style;

            if (bookmark.Style != null) {
                var brush = style.BackColor as SolidColorBrush;
                var color = ColorUtils.IncreaseSaturation(brush.Color);
                style = new HighlightingStyle(color);
            }
            else {
                style = new HighlightingStyle(Colors.Crimson);
            }

            markerMargingElements_.Add(new MarkerBarElement {
                Element = bookmark.Element,
                Visual = elementVisual,
                Style = style,
                HandlesInput = true
            });
        }

        private void PopulateMarkerBarForHighlighter(ElementHighlighter highlighter, int startY, double width,
                                                     double height, double dotSize) {
            highlighter.ForEachStyledElement((element, style) => {
                double y = (double)element.TextLocation.Line / LineCount * height;
                var brush = style.BackColor as SolidColorBrush;
                var color = ColorUtils.IncreaseSaturation(brush.Color);
                var elementVisual = new Rect(0, startY + y, width, dotSize);
                var barStyle = new HighlightingStyle(color);

                markerMargingElements_.Add(new MarkerBarElement {
                    Element = element,
                    Visual = elementVisual,
                    Style = barStyle,
                    HandlesInput = true
                });
            });
        }

        private void PopulateMarkerBarForBlocks(int startY, double width, double height) {
            foreach (var blockGroup in margin_.BlockGroups) {
                var brush = blockGroup.Group.Style.BackColor as SolidColorBrush;
                var color = ColorUtils.IncreaseSaturation(brush.Color, 1.5f);

                foreach (var segment in blockGroup.Segments) {
                    int startLine = Document.GetLineByOffset(segment.StartOffset).LineNumber;
                    int endLine = Document.GetLineByOffset(segment.EndOffset).LineNumber;
                    int lineSpan = endLine - startLine + 1;
                    double y = Math.Floor((double)startLine / LineCount * height);
                    double lineHeight = Math.Ceiling(Math.Max(1, (double)lineSpan / LineCount * height));
                    var elementVisual = new Rect(0, startY + y, width / 3, lineHeight);
                    var barStyle = new HighlightingStyle(color);

                    markerMargingElements_.Add(new MarkerBarElement {
                        Element = segment.Element,
                        Visual = elementVisual,
                        Style = barStyle,
                        HandlesInput = false
                    });
                }
            }
        }

        private void PopulateMarkerBarForDiffs(DiffLineHighlighter highlighter, int startY, double width,
                                               double height) {
            if (duringDiffModeSetup_) {
                return;
            }

            int lastLine = -1;
            int lineSpan = 1;
            var lastColor = Colors.Transparent;

            highlighter.ForEachDiffSegment((segment, color) => {
                int line = Document.GetLineByOffset(segment.StartOffset).LineNumber;

                if (line != lastLine) {
                    if (line == lastLine + 1 && color == lastColor) {
                        lastLine = line;
                        lastColor = color;
                        lineSpan++;
                    }
                    else {
                        if (lineSpan > 0 && lastColor != Colors.Transparent) {
                            PopulateDiffLines(lastLine, lineSpan, startY, width,
                                              height, lastColor);
                        }

                        lastLine = line;
                        lineSpan = 1;
                        lastColor = color;
                    }
                }
            });

            if (lineSpan > 0 && lastColor != Colors.Transparent) {
                PopulateDiffLines(lastLine, lineSpan, startY, width,
                                  height, lastColor);
            }
        }

        private void PopulateDiffLines(int lastLine, int lineSpan, int startY, double width, double height,
                                       Color color) {
            int startLine = lastLine - lineSpan;
            double y = Math.Floor((double)startLine / LineCount * height);
            double lineHeight = Math.Ceiling(Math.Max(1, (double)lineSpan / LineCount * height));
            color = ColorUtils.IncreaseSaturation(color, 1.5f);
            var elementVisual = new Rect(2 * width / 3, startY + y, width / 3, lineHeight);
            var barStyle = new HighlightingStyle(color);

            markerMargingElements_.Add(new MarkerBarElement {
                Element = null,
                Visual = elementVisual,
                Style = barStyle,
                HandlesInput = false
            });
        }

        private void PreviousBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (!JumpToBookmark(bookmarks_.GetPrevious())) {
                JumpToBookmark(bookmarks_.JumpToLastBookmark());
            }
        }

        private void RaiseBlockSelectedEvent(IRElement element) {
            BlockSelected?.Invoke(this, new IRElementEventArgs { Element = element, Document = this });
        }

        private void RaiseBookmarkAddedEvent(Bookmark bookmark) {
            BookmarkAdded?.Invoke(this, bookmark);
        }

        private void RaiseBookmarkChangedEvent(Bookmark bookmark) {
            BookmarkChanged?.Invoke(this, bookmark);
        }

        private void RaiseBookmarkListClearedEvent() {
            BookmarkListCleared?.Invoke(this, new EventArgs());
        }

        private void RaiseBookmarkRemovedEvent(Bookmark bookmark) {
            BookmarkRemoved?.Invoke(this, bookmark);
        }

        private void RaiseBookmarkSelectedEvent(Bookmark bookmark) {
            BookmarkSelected?.Invoke(this, new SelectedBookmarkInfo {
                Bookmark = bookmark,
                SelectedIndex = bookmarks_.SelectedIndex,
                TotalBookmarks = bookmarks_.Bookmarks.Count
            });
        }

        private void RaiseElementHighlightingEvent(IRElement element, HighlightedGroup group,
                                                   HighlighingType type, HighlightingEventAction action) {
            ElementHighlighting?.Invoke(this, new IRHighlightingEventArgs {
                Action = action,
                Type = type,
                Element = element,
                Group = group,
                MirrorAction = Utils.IsAltModifierActive()
            });
        }

        private void RaiseElementRemoveHighlightingEvent(IRElement element) {
            ElementHighlighting?.Invoke(this, new IRHighlightingEventArgs {
                Action = HighlightingEventAction.RemoveHighlighting,
                Element = element
            });
        }

        private void RaiseElementSelectedEvent(IRElement element) {
            ElementSelected?.Invoke(this, new IRElementEventArgs {
                Element = element,
                Document = this,
                MirrorAction = Utils.IsAltModifierActive()
            });
        }

        private void RaiseElementUnselectedEvent() {
            ElementUnselected?.Invoke(this, new IRElementEventArgs {
                Element = null,
                Document = this,
                MirrorAction = Utils.IsAltModifierActive()
            });
        }

        private void RaiseRemoveHighlightingEvent(HighlighingType type) {
            ElementHighlighting?.Invoke(this, new IRHighlightingEventArgs {
                Action = HighlightingEventAction.RemoveHighlighting,
                Type = type
            });
        }

        private void RemoveAllBookmarksExecuted(object sender, ExecutedRoutedEventArgs e) {
            RemoveAllBookmarks();
        }

        private void RemoveBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (margin_.SelectedBookmark != null) {
                RemoveBookmark(margin_.SelectedBookmark);
                return;
            }

            // Find the bookmark associated with the current element.
            var selectedElement = GetSelectedElement();

            if (selectedElement != null) {
                var bookmark = bookmarks_.RemoveBookmark(selectedElement);

                if (bookmark == null && selectedElement is OperandIR op) {
                    bookmark = bookmarks_.RemoveBookmark(op.ParentTuple);
                }

                if (bookmark != null) {
                    RemoveBookmark(bookmark);
                }
            }
        }

        private void RenderMarkerBar(bool forceUpdate = false) {
            if (!forceUpdate && (markerMargingElements_ == null || !highlighterVersion_.NeedsRedrawing)) {
                return;
            }

            if (Math.Abs(markerMargin_.ActualWidth) < double.Epsilon ||
                Math.Abs(markerMargin_.ActualHeight) < double.Epsilon) {
                return; // View not properly initialized yet.
            }

            var drawingVisual = new DrawingVisual();

            using (var draw = drawingVisual.RenderOpen()) {
                foreach (var barElement in markerMargingElements_) {
                    draw.DrawRectangle(barElement.Style.BackColor, barElement.Style.Border,
                                       barElement.Visual);
                }
            }

            // Create a bitmap with an area matching the margin,
            // render the visual over it and use it instead since it is more efficient to draw.
            var bitmap = new RenderTargetBitmap((int)markerMargin_.ActualWidth,
                                                (int)markerMargin_.ActualHeight, 96, 96,
                                                PixelFormats.Default);
            bitmap.Render(drawingVisual);
            bitmap.Freeze();
            Image image = null;

            if (markerMargin_.Children.Count > 0) {
                image = markerMargin_.Children[0] as Image;

                if (image != null) {
                    var prevBitmap = image.Source as RenderTargetBitmap;
                    prevBitmap.Clear(); // Required to prevent GDI leaks.
                }
            }

            if (image == null) {
                image = new Image();
                image.Source = bitmap;
                markerMargin_.Children.Add(image);
            }
            else {
                image.Source = bitmap;
            }

            highlighterVersion_.NeedsRedrawing = false;
        }

        public void SetCaretAtElement(IRElement element) {
            SetCaretAtOffset(element.TextLocation.Offset);
        }

        public void SetCaretAtOffset(int offset) {
            ignoreNextCaretEvent_ = true;
            TextArea.Caret.Offset = offset;
        }

        private void SetupCommands() {
            AddCommand(DocumentCommand.GoToDefinition, GoToDefinitionExecuted);
            AddCommand(DocumentCommand.GoToDefinitionSkipCopies, GoToDefinitionSkipCopiesExecuted);
            AddCommand(DocumentCommand.Mark, MarkExecuted);
            AddCommand(DocumentCommand.MarkBlock, MarkBlockExecuted);
            AddCommand(DocumentCommand.MarkDefinition, MarkDefinitionExecuted);
            AddCommand(DocumentCommand.MarkDefinitionBlock, MarkDefinitionBlockExecuted);
            AddCommand(DocumentCommand.ShowUses, ShowUsesExecuted);
            AddCommand(DocumentCommand.MarkUses, MarkUsesExecuted);
            AddCommand(DocumentCommand.MarkReferences, MarkReferencesExecuted);
            AddCommand(DocumentCommand.ShowReferences, ShowReferencesExecuted);
            AddCommand(DocumentCommand.ShowExpressionGraph, ShowExpressionGraphExecuted);
            AddCommand(DocumentCommand.ClearMarker, ClearMarkerExecuted);
            AddCommand(DocumentCommand.ClearAllMarkers, ClearAllMarkersExecuted);
            AddCommand(DocumentCommand.ClearBlockMarkers, ClearBlockMarkersExecuted);
            AddCommand(DocumentCommand.ClearInstructionMarkers, ClearInstructionMarkersExecuted);
            AddCommand(DocumentCommand.AddBookmark, AddBookmarkExecuted);
            AddCommand(DocumentCommand.RemoveBookmark, RemoveBookmarkExecuted);
            AddCommand(DocumentCommand.RemoveAllBookmarks, RemoveAllBookmarksExecuted);
            AddCommand(DocumentCommand.PreviousBookmark, PreviousBookmarkExecuted);
            AddCommand(DocumentCommand.NextBookmark, NextBookmarkExecuted);
            AddCommand(DocumentCommand.FirstBookmark, FirstBookmarkExecuted);
            AddCommand(DocumentCommand.LastBookmark, LastBookmarkExecuted);
            AddCommand(DocumentCommand.UndoAction, UndoActionExecuted, UndoActionCanExecute);
        }

        private void SetupEvents() {
            if (eventSetupDone_) {
                return;
            }

            MouseDown += IRDocument_MouseDown;
            PreviewMouseLeftButtonDown += TextEditor_PreviewMouseLeftButtonDown;
            PreviewMouseRightButtonDown += IRDocument_PreviewMouseRightButtonDown;
            PreviewMouseLeftButtonUp += IRDocument_PreviewMouseLeftButtonUp;
            PreviewMouseHover += IRDocument_PreviewMouseHover;
            PreviewMouseHoverStopped += IRDocument_PreviewMouseHoverStopped;
            PreviewMouseDoubleClick += IRDocument_PreviewMouseDoubleClick;
            PreviewMouseMove += IRDocument_PreviewMouseMove;
            Drop += IRDocument_Drop;
            DragOver += IRDocument_DragOver;
            GiveFeedback += IRDocument_GiveFeedback;
            AllowDrop = true;
            TextArea.Caret.PositionChanged += Caret_PositionChanged;
            margin_.BookmarkRemoved += Margin__BookmarkRemoved;
            margin_.BookmarkChanged += Margin__BookmarkChanged;
            eventSetupDone_ = true;
        }

        private void IRDocument_GiveFeedback(object sender, GiveFeedbackEventArgs e) {
            e.UseDefaultCursors = false;
            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private void IRDocument_DragOver(object sender, DragEventArgs e) {
            var position = e.GetPosition(TextArea.TextView);
            var element = FindPointedElement(position, out _);

            if (element != null) {
                HighlightElement(element, HighlighingType.Hovered);
                e.Effects = DragDropEffects.All;
                e.Handled = true;
            }
            else {
                HideHoverHighlighting();
            }
        }

        private void IRDocument_Drop(object sender, DragEventArgs e) {
            if (e.Data.GetData(typeof(IRElementDragDropSelection)) is IRElementDragDropSelection selection) {
                var position = e.GetPosition(TextArea.TextView);
                var element = FindPointedElement(position, out _);
                selection.Element = element;
                e.Effects = DragDropEffects.All;
                e.Handled = true;
            }
        }

        private void IRDocument_MouseDown(object sender, MouseButtonEventArgs e) {
            // Handle the mouse back-button being pressed.
            if (e.ChangedButton == MouseButton.XButton1) {
                UndoLastAction();
            }
        }

        private void UndoLastAction() {
            UndoReversibleAction();

            if (Utils.IsAltModifierActive()) {
                MirrorAction(DocumentActionKind.UndoAction, null);
            }
        }

        public void UninstallBlockFolding() {
            if (folding_ != null) {
                FoldingManager.Uninstall(folding_);
                folding_ = null;
            }
        }

        public void SetupBlockFolding() {
            if (!settings_.ShowBlockFolding) {
                UninstallBlockFolding();
                return;
            }
            else if (function_ == null) {
                return;
            }

            UninstallBlockFolding();
            folding_ = FoldingManager.Install(TextArea);
            var foldingStrategy = Session.CompilerInfo.CreateFoldingStrategy(function_);
            foldingStrategy.UpdateFoldings(folding_, Document);
        }

        private void SetupProperties() {
            IsReadOnly = true;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Options.AllowScrollBelowDocument = false;
            Options.EnableEmailHyperlinks = false;
            Options.EnableHyperlinks = false;

            // Don't use rounded corners for selection rectangles.
            TextArea.SelectionCornerRadius = 0;
            TextArea.SelectionBorder = null;
        }

        private void SetupStableRenderers() {
            hoverHighlighter_ = new ElementHighlighter(HighlighingType.Hovered);
            selectedHighlighter_ = new ElementHighlighter(HighlighingType.Selected);
            markedHighlighter_ = new ElementHighlighter(HighlighingType.Marked);
            diffHighlighter_ = new DiffLineHighlighter();
            TextArea.TextView.BackgroundRenderers.Add(diffHighlighter_);
            TextArea.TextView.BackgroundRenderers.Add(markedHighlighter_);
            TextArea.TextView.BackgroundRenderers.Add(selectedHighlighter_);
            TextArea.TextView.BackgroundRenderers.Add(hoverHighlighter_);
        }

        private void SetupRenderers() {
            if (blockHighlighter_ != null) {
                TextArea.TextView.BackgroundRenderers.Remove(blockHighlighter_);
                blockHighlighter_ = null;
            }

            if (settings_.ShowBlockSeparatorLine) {
                blockHighlighter_ = new BlockBackgroundHighlighter(settings_.ShowBlockSeparatorLine,
                                                                   settings_.BlockSeparatorColor,
                                                                   settings_.BackgroundColor,
                                                                   settings_.AlternateBackgroundColor);

                TextArea.TextView.BackgroundRenderers.Insert(0, blockHighlighter_);

                if (function_ != null) {
                    SetupBlockHighlighter();
                }
            }

            // Highlighting of current line.
            if (lineHighlighter_ != null) {
                TextArea.TextView.BackgroundRenderers.Remove(lineHighlighter_);
                lineHighlighter_ = null;
            }

            if (settings_.HighlightCurrentLine) {
                lineHighlighter_ = new CurrentLineHighlighter(this);
                TextArea.TextView.BackgroundRenderers.Add(lineHighlighter_);
            }

            if (margin_ != null) {
                margin_.BackgroundColor = settings_.MarginBackgroundColor;
            }
            else {
                margin_ = new DocumentMargin(settings_.MarginBackgroundColor);
                TextArea.LeftMargins.Add(margin_);
            }

            if (remarkHighlighter_ != null) {
                TextArea.TextView.BackgroundRenderers.Remove(remarkHighlighter_);
            }

            remarkHighlighter_ = new RemarkHighlighter(HighlighingType.Marked);
            TextArea.TextView.BackgroundRenderers.Add(remarkHighlighter_);

            if (overlayRenderer_ != null) {
                TextArea.TextView.BackgroundRenderers.Remove(overlayRenderer_);
            }

            overlayRenderer_ = new OverlayRenderer(markedHighlighter_);
            TextArea.TextView.BackgroundRenderers.Add(overlayRenderer_);
            TextArea.TextView.InsertLayer(overlayRenderer_, KnownLayer.Text, LayerInsertionPosition.Above);

            if (DiffModeEnabled) {
                if (diffHighlighter_ != null) {
                    diffHighlighter_.Clear();
                    TextArea.TextView.BackgroundRenderers.Remove(diffHighlighter_);
                }

                if (diffSegments_ != null) {
                    // Insert below the marker renderer so that marked,
                    // selected and hovered elements get the usual highlighting.
                    diffHighlighter_ = new DiffLineHighlighter();
                    int index = TextArea.TextView.BackgroundRenderers.IndexOf(markedHighlighter_);

                    if (index != -1) {
                        TextArea.TextView.BackgroundRenderers.Insert(index, diffHighlighter_);
                    }
                    else {
                        TextArea.TextView.BackgroundRenderers.Add(diffHighlighter_);
                    }

                    StartDiffSegmentAdding();
                    AddDiffTextSegments(diffSegments_);
                    AllDiffSegmentsAdded();
                }
            }
        }

        public void AddDiffTextSegments(List<DiffTextSegment> segments) {
            diffSegments_ = segments;
            diffHighlighter_.Add(segments);
            AllDiffSegmentsAdded();
        }

        public void StartDiffSegmentAdding() {
            diffHighlighter_.Clear();
            ClearAllMarkers();
            TextArea.IsEnabled = false;

            // Disable the caret event because it triggers redrawing of
            // the right marker bar, which besides being slow, causes a
            // GDI handle leak from the usage of RenderTargetBitmap (known issue).
            disableCaretEvent_ = true;
            duringDiffModeSetup_ = true;
        }

        public void AllDiffSegmentsAdded() {
            disableCaretEvent_ = false;
            duringDiffModeSetup_ = false;

            // This forces the updating of the right bar with the diffed line markers
            // to execute after the editor displayed the scroll bars.
            Dispatcher.Invoke(() => UpdateHighlighting(), DispatcherPriority.Render);
        }

        private HighlightingStyle GetRemarkLineStyle(Remark remark) {
            //? TODO: Caching
            return new HighlightingStyle(remark.Category.MarkColor,
                                         Pens.GetPen(remark.Category.TextMarkBorderColor,
                                                     remark.Category.TextMarkBorderWeight));
        }

        private HighlightingStyle GetRemarkBookmarkStyle(Remark remark) {
            //? TODO: Caching
            return new HighlightingStyle(remark.Category.MarkColor);
        }

        public void AddRemarks(List<Remark> allRemarks, List<RemarkLineGroup> markerRemarksGroups) {
            if (markerRemarksGroups != null) {
                AddMarginRemarks(markerRemarksGroups);
            }

            if (allRemarks != null) {
                AddDocumentRemarks(allRemarks);
            }

            UpdateMargin();
            UpdateHighlighting();
        }

        private void AddMarginRemarks(List<RemarkLineGroup> markerRemarksGroups) {
            foreach (var remarkGroup in markerRemarksGroups) {
                bool groupHandled = false;

                foreach (var remark in remarkGroup.Remarks) {
                    foreach (var element in remark.ReferencedElements) {
                        if (remark.Category.AddLeftMarginMark && !groupHandled) {
                            var style = GetRemarkBookmarkStyle(remarkGroup.LeaderRemark);
                            var bookmark = new Bookmark(0, element, remarkGroup.LeaderRemark.RemarkText, style);
                            margin_.AddRemarkBookmark(bookmark, remarkGroup);
                            groupHandled = true;
                            break;
                        }
                    }

                    if (groupHandled) {
                        break;
                    }
                }
            }
        }

        private void AddDocumentRemarks(List<Remark> allRemarks) {
            var markedElements = new HashSet<Tuple<IRElement, RemarkKind>>(allRemarks.Count);

            foreach (var remark in allRemarks) {
                foreach (var element in remark.ReferencedElements) {
                    if (remark.Category.AddTextMark) {
                        // Mark each element only once with the same kind of remark.
                        var elementKindPair = new Tuple<IRElement, RemarkKind>(element, remark.Kind);

                        if (!markedElements.Contains(elementKindPair)) {
                            var style = GetRemarkLineStyle(remark);
                            var group = new HighlightedGroup(element, style);
                            remarkHighlighter_.Add(group);
                            markedElements.Add(elementKindPair);
                        }
                    }
                }
            }
        }

        public void RemoveRemarks() {
            margin_.RemoveRemarkBookmarks();
            remarkHighlighter_.Clear();
            UpdateMargin();
            UpdateHighlighting();
        }

        public void UpdateRemarks(List<Remark> allRemarks, List<RemarkLineGroup> markerRemarksGroups) {
            RemoveRemarks();
            AddRemarks(allRemarks, markerRemarksGroups);
        }

        private void ShowDefinitionPreview(IRElement element) {
            IRElement target = null;

            if (element is OperandIR op) {
                if (op.IsLabelAddress) {
                    target = op.BlockLabelValue;
                }
                else {
                    target = ReferenceFinder.GetSSADefinition(op);
                }
            }

            if (target == null) {
                return;
            }

            // Show the preview only if outside the current view.
            if (IsElementOutsideView(target)) {
                ShowTooltip(target);
            }
        }

        private void ShowReferencesExecuted(object sender, ExecutedRoutedEventArgs e) {
            var selectedOp = GetSelectedElement();

            if (selectedOp is OperandIR op) {
                ShowReferences(op);
                MirrorAction(DocumentActionKind.ShowReferences, selectedOp);
            }
        }

        private void ShowExpressionGraphExecuted(object sender, ExecutedRoutedEventArgs e) {
            var element = GetSelectedElement();

            if (element != null) {
                ShowExpressionGraph(element);
            }
        }

        private void ShowExpressionGraph(IRElement element) {
            var action = new DocumentAction(DocumentActionKind.ShowExpressionGraph, element);
            ActionPerformed?.Invoke(this, action);
        }

        private void ShowReferences(OperandIR op) {
            Session.FindAllReferences(op, this);
        }

        private void ShowTooltip(IRElement element, bool showAlways = false) {
            if (element == null) {
                return; // Valid case for search results.
            }

            if (!showAlways) {
                if (!settings_.ShowPreviewPopup ||
                    (settings_.ShowPreviewPopupWithModifier && !Utils.IsKeyboardModifierActive())) {
                    HideTooltip();
                    return;
                }
            }

            if (nodeToolTip_ != null) {
                if (nodeToolTip_.Element == element) {
                    return; // Already showing preview for this element.
                }
                else {
                    HideTooltip();
                }
            }

            if (element == null || ignoreNextPreviewElement_ == element) {
                return;
            }

            nodeToolTip_ = new IRPreviewToolTip(600, 100, this, element);
            nodeToolTip_.Show();
        }

        private void ShowUsesExecuted(object sender, ExecutedRoutedEventArgs e) {
            var defOp = GetSelectedElementDefinition();

            if (defOp != null) {
                ShowUses(defOp);
                MirrorAction(DocumentActionKind.ShowUses, defOp);
            }
        }

        private void ShowUses(OperandIR defOp) {
            Session.FindSSAUses(defOp, this);
        }

        private void TextEditor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            var position = e.GetPosition(TextArea.TextView);

            // Ignore click outside the text view, such as the right marker bar.
            if (position.X >= TextArea.TextView.ActualWidth ||
                position.Y >= TextArea.TextView.ActualHeight) {
                return;
            }

            // Ignore click on a bookmark.
            if (margin_.HasHoveredBookmark()) {
                return;
            }

            HideTemporaryUI();
            var element = FindPointedElement(position, out int textOffset);
            SelectElement(element, true, true, textOffset);
            e.Handled = element != null;
        }

        public void EnterDiffMode() {
            DiffModeEnabled = true;
        }

        public void ExitDiffMode() {
            margin_.ClearMarkers();
            diffHighlighter_.Clear();
            DiffModeEnabled = false;
        }

        private void UpdateHighlighting() {
            PopulateMarkerBar();
            TextArea.TextView.Redraw();
        }

        private void UpdateMargin() {
            margin_.InvalidateVisual();
        }

        private class MarkerMarginVersionInfo {
            public int LastBlockMarginVersion;
            public int LastBookmarkVersion;
            public int LastDiffVersion;
            public int LastHoveredVersion;
            public int LastMarkedVersion;
            public int LastSelectedVersion;
            public bool NeedsRedrawing;
        }

        private class MarkerBarElement {
            public IRElement Element { get; set; }
            public HighlightingStyle Style { get; set; }
            public Rect Visual { get; set; }
            public bool HandlesInput { get; set; }
        }
    }
}