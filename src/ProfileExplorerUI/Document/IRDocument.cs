// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Analysis;
using ProfileExplorer.Core.Graph;
using ProfileExplorer.Core.IR;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Document;
using ProfileExplorer.UI.Profile;
using ProfileExplorer.UI.Query;
using TextLocation = ProfileExplorer.Core.TextLocation;

namespace ProfileExplorer.UI;

public enum BringIntoViewStyle {
  Default,
  FirstLine
}

public enum HighlightingEventAction {
  ReplaceHighlighting,
  AppendHighlighting,
  RemoveHighlighting
}

public class IRElementEventArgs : EventArgs {
  public IRElement Element { get; set; }
  public bool MirrorAction { get; set; }
  public IRDocument Document { get; set; }
}

public class IRHighlightingEventArgs : EventArgs {
  public HighlightingEventAction Action { get; set; }
  public IRElement Element { get; set; }
  public HighlightedElementGroup Group { get; set; }
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

public static class DocumentCommand {
  public static readonly RoutedUICommand GoToDefinition = new("Untitled", "GoToDefinition", typeof(IRDocumentHost));
  public static readonly RoutedUICommand GoToDefinitionSkipCopies =
    new("Untitled", "GoToDefinitionSkipCopies", typeof(IRDocumentHost));
  public static readonly RoutedUICommand PreviewDefinition =
    new("Untitled", "PreviewDefinition", typeof(IRDocumentHost));
  public static readonly RoutedUICommand MarkDefinition = new("Untitled", "MarkDefinition", typeof(IRDocumentHost));
  public static readonly RoutedUICommand MarkDefinitionBlock =
    new("Untitled", "MarkDefinitionBlock", typeof(IRDocumentHost));
  public static readonly RoutedUICommand Mark = new("Untitled", "Mark", typeof(IRDocumentHost));
  public static readonly RoutedUICommand MarkIcon = new("Untitled", "MarkIcon", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ShowUses = new("Untitled", "ShowUses", typeof(IRDocumentHost));
  public static readonly RoutedUICommand MarkUses = new("Untitled", "MarkUses", typeof(IRDocumentHost));
  public static readonly RoutedUICommand MarkBlock = new("Untitled", "MarkBlock", typeof(IRDocumentHost));
  public static readonly RoutedUICommand MarkReferences = new("Untitled", "MarkReferences", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ShowExpressionGraph =
    new("Untitled", "ShowExpressionGraph", typeof(IRDocumentHost));
  public static readonly RoutedUICommand NextBlock = new("Untitled", "NextBlock", typeof(IRDocumentHost));
  public static readonly RoutedUICommand PreviousBlock = new("Untitled", "PreviousBlock", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ShowReferences = new("Untitled", "ShowReferences", typeof(IRDocumentHost));
  public static readonly RoutedUICommand AddBookmark = new("Untitled", "AddBookmark", typeof(IRDocumentHost));
  public static readonly RoutedUICommand RemoveBookmark = new("Untitled", "RemoveBookmark", typeof(IRDocumentHost));
  public static readonly RoutedUICommand RemoveAllBookmarks =
    new("Untitled", "RemoveAllBookmarks", typeof(IRDocumentHost));
  public static readonly RoutedUICommand PreviousBookmark = new("Untitled", "PreviousBookmark", typeof(IRDocumentHost));
  public static readonly RoutedUICommand NextBookmark = new("Untitled", "NextBookmark", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ShowBookmarks = new("Untitled", "ShowBookmarks", typeof(IRDocumentHost));
  public static readonly RoutedUICommand FirstBookmark = new("Untitled", "FirstBookmark", typeof(IRDocumentHost));
  public static readonly RoutedUICommand LastBookmark = new("Untitled", "LastBookmark", typeof(IRDocumentHost));
  public static readonly RoutedUICommand FocusBlockSelector =
    new("Untitled", "FocusBlockSelector", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ClearMarker = new("Untitled", "ClearMarker", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ClearBlockMarkers =
    new("Untitled", "ClearBlockMarkers", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ClearInstructionMarkers =
    new("Untitled", "ClearInstructionMarkers", typeof(IRDocumentHost));
  public static readonly RoutedUICommand ClearAllMarkers = new("Untitled", "ClearAllMarkers", typeof(IRDocumentHost));
  public static readonly RoutedUICommand UndoAction = new("Untitled", "UndoAction", typeof(IRDocumentHost));
}

public sealed class IRDocument : TextEditor, INotifyPropertyChanged {
  private const float ParentStyleLightAdjustment = 1.20f;
  private const int DefaultMaxExpressionLevel = 4;
  private const int ExpressionLevelIncrement = 2;
  private static DocumentActionKind[] AutomationActions = {
    DocumentActionKind.SelectElement,
    DocumentActionKind.MarkElement,
    DocumentActionKind.ShowReferences,
    DocumentActionKind.GoToDefinition
  };
  private Stack<ReversibleDocumentAction> actionUndoStack_;
  private List<IRElement> blockElements_;
  private BlockBackgroundHighlighter blockHighlighter_;
  private BookmarkManager bookmarks_;
  private BlockIR currentBlock_;
  private HighlightedElementGroup currentSearchResultGroup_;
  private PairHighlightingStyle definitionStyle_;
  private DiffLineHighlighter diffHighlighter_;
  private List<DiffTextSegment> diffSegments_;
  private bool disableCaretEvent_;
  public bool disableOverlayEvents_;
  private ScrollBar docVerticalScrollBar_;
  private bool duringDiffModeSetup_;
  private bool eventSetupDone_;
  private HighlightingStyleCollection expressionOperandStyle_;
  private HighlightingStyleCollection expressionStyle_;
  private FoldingManager folding_;
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
  private bool overlayRendererConnectedTemporarely_;
  private HighlightingStyleCyclingCollection markerChildStyle_;
  private Canvas markerMargin_;
  private List<MarkerBarElement> markerMargingElements_;
  private HighlightingStyleCyclingCollection markerParentStyle_;
  private IRDocumentPopup previewPopup_;
  private bool preparingPreviewPopup_;
  private List<IRElement> operandElements_;
  private RemarkHighlighter remarkHighlighter_;
  private DelayedAction removeHoveredAction_;
  private Dictionary<TextSearchResult, IRElement> searchResultMap_;
  private HighlightedElementGroup searchResultsGroup_;
  private HighlightingStyle selectedBlockStyle_;
  private HashSet<IRElement> selectedElements_;
  private ElementHighlighter selectedHighlighter_;
  private HighlightingStyle selectedStyle_;
  private TextViewSettingsBase settings_;
  private PairHighlightingStyle ssaDefinitionStyle_;
  private PairHighlightingStyle ssaUserStyle_;
  private PairHighlightingStyle iteratedUserStyle_;
  private PairHighlightingStyle iteratedDefinitionStyle_;
  private List<IRElement> tupleElements_;
  private bool updateSuspended_;
  private IRElement currentExprElement_;
  private int currentExprStyleIndex_;
  private int currentExprLevel_;
  private Remark selectedRemark_;
  private bool selectingText_;
  private HashSet<FoldingSection> foldedBlocks_;
  private bool hasCustomLineNumbers_;
  private List<IVisualLineTransformer> registerdTransformers_;
  private List<HoverPreview> registeredHoverPreviews_;
  private CancelableTaskInstance markerBarUpdateTask_;
  private ReaderWriterLockSlim markerBarUpdateLock_;

  public IRDocument() {
    // Setup element tracking data structures.
    selectedElements_ = new HashSet<IRElement>();
    bookmarks_ = new BookmarkManager();
    actionUndoStack_ = new Stack<ReversibleDocumentAction>();
    highlighterVersion_ = new MarkerMarginVersionInfo();

    // Setup styles and colors.
    //? TODO: Expose colors as option
    definitionStyle_ = new PairHighlightingStyle {
      ParentStyle = new HighlightingStyle(Color.FromRgb(255, 215, 191)),
      ChildStyle = new HighlightingStyle(Color.FromRgb(255, 197, 163), ColorPens.GetBoldPen(Colors.Black))
    };

    expressionOperandStyle_ = DefaultHighlightingStyles.StyleSet;
    expressionStyle_ = DefaultHighlightingStyles.LightStyleSet;
    markerChildStyle_ = new HighlightingStyleCyclingCollection(DefaultHighlightingStyles.StyleSet);
    markerParentStyle_ = new HighlightingStyleCyclingCollection(DefaultHighlightingStyles.LightStyleSet);
    registerdTransformers_ = new List<IVisualLineTransformer>();
    registeredHoverPreviews_ = new List<HoverPreview>();
    markerBarUpdateTask_ = new CancelableTaskInstance(true);
    markerBarUpdateLock_ = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    SetupProperties();
    SetupStableRenderers();
    SetupCommands();
  }

  public List<BlockIR> Blocks => Function.Blocks;
  public BookmarkManager BookmarkManager => bookmarks_;
  public ISession Session { get; private set; }
  public FunctionIR Function { get; set; }
  public IRTextSection Section { get; set; }
  public ReadOnlyMemory<char> SectionText { get; set; }
  public bool DiffModeEnabled { get; set; }
  public bool IsLoaded { get; set; }
  public bool DuringSectionLoading { get; set; }

  public TextViewSettingsBase Settings {
    get => settings_;
    private set {
      settings_ = value;
      ReloadSettings();
    }
  }

  public IRDocumentColumnData ProfileColumnData { get; set; }
  public FunctionProcessingResult ProfileProcessingResult { get; set; }
  public double DefaultLineHeight => TextArea.TextView.DefaultLineHeight;
  public IEnumerable<FoldingSection> BlockFoldings => folding_?.AllFoldings;
  public event PropertyChangedEventHandler PropertyChanged;
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
  public event EventHandler<int> CaretChanged;
  public event EventHandler<FoldingSection> TextRegionFolded;
  public event EventHandler<FoldingSection> TextRegionUnfolded;
  public event EventHandler<IRTextSection> FunctionCallOpen;

  private static int AdjustVisibleLine(int line) {
    // Leave a few lines be visible above.
    if (line > 2) {
      line -= 2;
    }

    return line;
  }

  public void Initialize(TextViewSettingsBase settings, ISession session) {
    Session = session;
    Settings = settings;
  }

  public void BookmarkInfoChanged(Bookmark bookmark) {
    UpdateHighlighting();
  }

  public void DisableOverlayEventHandlers() {
    disableOverlayEvents_ = true;
    overlayRenderer_.MouseLeave();
  }

  public void EnableOverlayEventHandlers() {
    disableOverlayEvents_ = false;
  }

  public void ExecuteDocumentAction(DocumentAction action) {
#if DEBUG
    Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Execute {action}");
#endif

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
                            true, false);
            }
          }
          else {
            HandleElement(action.Element, highlighter,
                          true, false);
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
    ScrollToLine(AdjustVisibleLine(line));
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

      ScrollToLine(AdjustVisibleLine(line));
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
    int index = Function.Blocks.IndexOf(currentBlock_);

    if (index + 1 < Function.Blocks.Count) {
      GoToBlock(Function.Blocks[index + 1]);
    }

    return currentBlock_;
  }

  public BlockIR GoToPredecessorBlock() {
    return currentBlock_;
  }

  public BlockIR GoToPreviousBlock() {
    int index = Function.Blocks.IndexOf(currentBlock_);

    if (index == -1) {
      return currentBlock_;
    }

    if (index > 0 && Function.Blocks.Count > 0) {
      GoToBlock(Function.Blocks[index - 1]);
    }

    return currentBlock_;
  }

  public void HighlightElement(IRElement element, HighlighingType type) {
    HighlightSingleElement(element, GetHighlighter(type));
  }

  public void SelectElementsOnSourceLine(int lineNumber, SourceStackFrame inlinee = null) {
    ClearTemporaryHighlighting();
    MarkElementsOnSourceLine(selectedHighlighter_, lineNumber, Colors.Transparent,
                             false, true, inlinee);
  }

  public void SelectElementsInLineRange(int startLine, int endLine,
                                        Func<int, int> lineMapper = null,
                                        Brush backColor = null) {
    ClearTemporaryHighlighting();
    var style = selectedStyle_;

    if (backColor != null) {
      style = new HighlightingStyle(backColor, selectedStyle_.Border);
    }

    var group = new HighlightedElementGroup(style);

    if (lineMapper != null) {
      startLine = lineMapper(startLine);
      endLine = lineMapper(endLine);
    }

    foreach (var block in Function.Blocks) {
      foreach (var tuple in block.Tuples) {
        if (tuple.TextLocation.Line + 1 >= startLine &&
            tuple.TextLocation.Line + 1 <= endLine) {
          group.Add(tuple);
        }
      }
    }

    if (!group.IsEmpty()) {
      selectedHighlighter_.Add(group);
    }

    UpdateHighlighting();
  }

  public void SelectLine(int line) {
    if (line >= 0 && line <= Document.LineCount) {
      TextArea.Caret.Line = line;
      ScrollToLine(line);
    }
  }

  public void MarkElementsOnSourceLine(int lineNumber, Color selectedColor, bool raiseEvent = true) {
    MarkElementsOnSourceLine(markedHighlighter_, lineNumber, selectedColor, raiseEvent, false, null);
  }

  public void MarkElementWithDefaultStyle(IRElement element) {
    HighlightSingleElement(element, markedHighlighter_);
  }

  public void UnloadDocument() {
    UnregisterTextTransformers();
    UnregisterHoverPreviews();
    ResetRenderers();
    IsLoaded = false;
    Text = "";
    SectionText = ReadOnlyMemory<char>.Empty;
    ProfileColumnData = null;
    Section = null;
    Function = null;
    selectedRemark_ = null;
    currentExprElement_ = null;
    foldedBlocks_ = null;
    ClearSelectedElements();
  }

  public async Task<bool> InitializeFromDocument(IRDocument doc, bool copyTemporaryHighlighting = true,
                                                 string text = null, bool analyzeFunction = true) {
    if (Section == doc.Section) {
      return false;
    }

    UnloadDocument();
    Session = doc.Session;
    Settings = doc.Settings;
    Section = doc.Section;
    Function = doc.Function;
    blockElements_ = doc.blockElements_;
    tupleElements_ = doc.tupleElements_;
    operandElements_ = doc.operandElements_;

    if (copyTemporaryHighlighting) {
      hoverHighlighter_.CopyFrom(doc.hoverHighlighter_);
      selectedHighlighter_.CopyFrom(doc.selectedHighlighter_);
    }

    markedHighlighter_.CopyFrom(doc.markedHighlighter_);
    bookmarks_.CopyFrom(doc.bookmarks_);
    margin_.CopyFrom(doc.margin_);
    ignoreNextCaretEvent_ = true;

    if (text != null) {
      Text = text;
      SectionText = text.AsMemory();
    }
    else {
      Text = doc.SectionText.ToString();
      SectionText = doc.SectionText;
    }

    SetupBlockFolding();

    if (analyzeFunction) {
      await Session.CompilerInfo.HandleLoadedSection(this, Function, Section);
    }

    return true;
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

  public async Task LoadSavedSection(ParsedIRTextSection parsedSection, IRDocumentState savedState) {
#if DEBUG
    Trace.TraceInformation(
      $"Document {ObjectTracker.Track(this)}: Load saved section {parsedSection}");
#endif

    selectedElements_ = savedState.SelectedElements?.ToHashSet(item => (IRElement)item) ??
                        new HashSet<IRElement>();

    bookmarks_.LoadState(savedState.Bookmarks, Function);
    hoverHighlighter_.LoadState(savedState.HoverHighlighter, Function);
    selectedHighlighter_.LoadState(savedState.SelectedHighlighter, Function);
    markedHighlighter_.LoadState(savedState.MarkedHighlighter, Function);
    overlayRenderer_.LoadState(savedState.ElementOverlays, Function, SetupElementOverlayEvents);
    margin_.LoadState(savedState.Margin);

    bookmarks_.Bookmarks.ForEach(item => RaiseBookmarkAddedEvent(item));
    SetCaretAtOffset(savedState.CaretOffset);

    await ComputeElementListsAsync();
    await LateLoadSectionSetup(parsedSection);
  }

  public async Task LoadSection(ParsedIRTextSection parsedSection,
                                bool isSourceCode = false) {
#if DEBUG
    Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Load section {parsedSection}");
#endif
    // If the section loading is not done in two stages,
    // run the first stage now to initialize the text view.
    if (!DuringSectionLoading) {
      PreloadSection(parsedSection);
    }

    await ComputeElementListsAsync();
    await LateLoadSectionSetup(parsedSection, isSourceCode);
  }

  public void MarkBlock(IRElement element, HighlightingStyle style, bool raiseEvent = true) {
    var group = new HighlightedElementGroup(element, style);
    margin_.AddBlock(group, raiseEvent);

    if (raiseEvent) {
      RecordReversibleAction(DocumentActionKind.MarkElement, element);
    }

    ClearTemporaryHighlighting();
    UpdateMargin();
    UpdateHighlighting();

    if (raiseEvent) {
      RaiseElementHighlightingEvent(element, group, markedHighlighter_.Type,
                                    HighlightingEventAction.ReplaceHighlighting);
    }
  }

  public void MarkElement(IRElement element, Color selectedColor, bool raiseEvent = true) {
    var style = new HighlightingStyle(selectedColor, null);
    MarkElement(element, style, raiseEvent);
  }

  public void MarkElements(IEnumerable<IRElement> elements, Color selectedColor) {
    var style = new HighlightingStyle(selectedColor, null);
    var group = new HighlightedElementGroup(style);
    group.AddRange(elements);

    ClearTemporaryHighlighting();
    markedHighlighter_.Add(group);
    UpdateHighlighting();
  }

  public void SelectElements(IEnumerable<IRElement> elements) {
    var group = new HighlightedElementGroup(selectedStyle_);
    group.AddRange(elements);

    ClearTemporaryHighlighting();
    selectedHighlighter_.Add(group);
    UpdateHighlighting();
  }

  public void MarkElement(IRElement element, HighlightingStyle style, bool raiseEvent = true) {
    if (raiseEvent) {
#if DEBUG
      Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Mark element {element.Id}");
#endif
      RecordReversibleAction(DocumentActionKind.MarkElement, element);
    }

    ClearTemporaryHighlighting();
    var group = new HighlightedElementGroup(element, style);
    markedHighlighter_.Remove(element);
    markedHighlighter_.Add(group);
    UpdateHighlighting();

    if (raiseEvent) {
      RaiseElementHighlightingEvent(element, group, markedHighlighter_.Type,
                                    HighlightingEventAction.AppendHighlighting);
    }
  }

  public void BeginMarkElementAppend(HighlighingType highlightingType) {
    if (highlightingType == HighlighingType.Hovered ||
        highlightingType == HighlighingType.Selected) {
      ClearTemporaryHighlighting();
    }
  }

  public void EndMarkElementAppend(HighlighingType highlightingType) {
    UpdateHighlighting();
  }

  public void MarkElementAppend(IRElement element, Color selectedColor,
                                HighlighingType highlightingType, bool raiseEvent = true) {
    var style = new HighlightingStyle(selectedColor, null);
    MarkElementAppend(element, style, highlightingType, raiseEvent);
  }

  public void MarkElementAppend(IRElement element, HighlightingStyle style,
                                HighlighingType highlightingType, bool raiseEvent = true) {
#if DEBUG
    Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Mark element {element.Id}");
#endif
    var highlighter = GetHighlighter(highlightingType);

    var group = new HighlightedElementGroup(element, style);
    highlighter.Remove(element);
    highlighter.Add(group);

    if (raiseEvent) {
      RaiseElementHighlightingEvent(element, group, highlightingType,
                                    HighlightingEventAction.AppendHighlighting);
    }
  }

  public void SetRootConnectedElement(IRElement element, HighlightingStyle style,
                                      bool isTemporary) {
    overlayRenderer_.SetRootElement(element, style);
    overlayRendererConnectedTemporarely_ = isTemporary;
  }

  public void AddConnectedElement(IRElement element, HighlightingStyle style) {
    overlayRenderer_.AddConnectedElement(element, style);
  }

  public void ClearConnectedElements() {
    overlayRenderer_.ClearConnectedElements();
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

  public void MarkTextRange(int offset, int length, Color color) {
    var style = new HighlightingStyle(color, ColorPens.GetPen(Colors.DarkGray));
    var element = CreateDummyElement(offset, length);
    var group = new HighlightedElementGroup(element, style);
    markedHighlighter_.Add(group);
    UpdateHighlighting();
  }

  public void MarkSearchResults(List<TextSearchResult> results, Color color) {
    ClearSearchResults();

    if (results.Count == 0) {
      return;
    }

    var style = new HighlightingStyle(color, ColorPens.GetPen(Colors.DarkGray));
    searchResultMap_ = new Dictionary<TextSearchResult, IRElement>(results.Count);
    searchResultsGroup_ = new HighlightedElementGroup(style);

    foreach (var result in results) {
      var element = CreateDummyElement(result.Offset, result.Length);
      searchResultsGroup_.Add(element);
      searchResultMap_[result] = element;
    }

    markedHighlighter_.Add(searchResultsGroup_, false);
    ClearTemporaryHighlighting();
    UpdateHighlighting();
  }

  public void ClearSearchResults() {
    if (searchResultsGroup_ != null) {
      markedHighlighter_.Remove(searchResultsGroup_);
      searchResultsGroup_ = null;

      if (currentSearchResultGroup_ != null) {
        markedHighlighter_.Remove(currentSearchResultGroup_);
        currentSearchResultGroup_ = null;
      }
    }

    UpdateHighlighting();
  }

  public void JumpToSearchResult(TextSearchResult result, Color color) {
    if (currentSearchResultGroup_ != null) {
      markedHighlighter_.Remove(currentSearchResultGroup_);
      searchResultsGroup_.Add(currentSearchResultGroup_.Elements[0]);
    }

    var style = new HighlightingStyle(color, ColorPens.GetPen(Colors.Black));
    currentSearchResultGroup_ = new HighlightedElementGroup(style);
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
    savedState.SelectedElements = selectedElements_.ToList(item => new IRElementReference(item));
    savedState.Bookmarks = bookmarks_.SaveState(Function);
    savedState.HoverHighlighter = hoverHighlighter_.SaveState(Function);
    savedState.SelectedHighlighter = selectedHighlighter_.SaveState(Function);
    savedState.MarkedHighlighter = markedHighlighter_.SaveState(Function);
    savedState.ElementOverlays = overlayRenderer_.SaveState(Function);
    savedState.Margin = margin_.SaveState();
    savedState.CaretOffset = TextArea.Caret.Offset;
    return savedState;
  }

  public void SelectElement(IRElement element, bool raiseEvent = true, bool fromUICommand = false,
                            int textOffset = -1) {
    if (settings_ is not DocumentSettings) { // For source code documents.
      return;
    }

    // During a text selection, don't select more
    // than the first clicked element.
    if (selectingText_) {
      return;
    }

    ClearTemporaryHighlighting();
    ResetExpressionLevel(element);

    if (element != null) {
#if DEBUG
      Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Select element {element.Id}");
#endif

      // Don't highlight a block unless the header is selected.
      if (element is BlockIR && fromUICommand && textOffset != -1) {
        int line = Document.GetLineByOffset(textOffset).LineNumber;
        int blockStartLine = element.TextLocation.Line;

        if (line - 1 != blockStartLine) {
          HandelNoElementSelection();
          return;
        }
      }

      bool markExpression = fromUICommand && Utils.IsControlModifierActive();
      bool markReferences = fromUICommand && Utils.IsShiftModifierActive();
      AddSelectedElement(element, raiseEvent);

      var highlighter = Utils.IsAltModifierActive() ? markedHighlighter_ : selectedHighlighter_;
      HandleElement(element, highlighter, markExpression, markReferences);
      UpdateHighlighting();

      if (fromUICommand) {
        ignoreNextCaretEvent_ = true;
        MirrorAction(DocumentActionKind.SelectElement, element);
      }
    }
    else if (raiseEvent) {
      // Notify of no element being selected.
      HandelNoElementSelection();
    }
  }

  public void UnselectElements() {
    ClearSelectedElements();
    UpdateHighlighting();
    ResetExpressionLevel();
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

  public Bookmark AddBookmark(IRElement selectedElement, string text = null, bool pinned = false) {
    var bookmark = bookmarks_.AddBookmark(selectedElement);
    bookmark.Text = text;
    bookmark.IsPinned = pinned;

    margin_.AddBookmark(bookmark);
    margin_.SelectBookmark(bookmark);
    UpdateMargin();
    UpdateHighlighting();
    RaiseBookmarkAddedEvent(bookmark);
    return bookmark;
  }

  public void PreloadSection(ParsedIRTextSection parsedSection) {
#if DEBUG
    Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Start setup for {parsedSection}");
#endif

    DuringSectionLoading = true;
    Section = parsedSection.Section;
    Function = parsedSection.Function;
    ignoreNextCaretEvent_ = true;
    ClearSelectedElements();
    ResetRenderers();

    // Replace the text in the view.
    Text = parsedSection.Text.ToString();
    SectionText = parsedSection.Text; // Cache raw text.
    SetCaretAtOffset(0);
  }

  public IRElement GetElementAt(Point position) {
    int offset = DocumentUtils.GetOffsetFromMousePosition(position, this, out _);
    return offset != -1 ? FindElementAtOffset(offset) : null;
  }

  public async Task LoadDiffedFunction(DiffMarkingResult diffResult, IRTextSection newSection) {
    StartDiffSegmentAdding();
    Function = diffResult.DiffFunction;
    Section = newSection;
    SectionText = diffResult.DiffText.AsMemory();

    // Compute the element lists before removing the block folding.
    // If done after, the UI may update during the async call
    // and it would be noticeable how the block folding disappears, then immediately
    // reappears once LateLoadSectionSetup reinstalls the block folding.
    await ComputeElementListsAsync();

    // Take ownership of the text document.
    diffResult.DiffDocument.SetOwnerThread(Thread.CurrentThread);
    Document = diffResult.DiffDocument;

    // Remove the current block folding since it's bound to the current text area.
    UninstallBlockFolding();
    ClearTemporaryHighlighting();
    await LateLoadSectionSetup(null);

    AddDiffTextSegments(diffResult.DiffSegments);
    AllDiffSegmentsAdded();
  }

  public void SetCaretAtElement(IRElement element) {
    SetCaretAtOffset(element.TextLocation.Offset);
  }

  public void SetCaretAtOffset(int offset) {
    if (offset < Document.TextLength) {
      ignoreNextCaretEvent_ = true;
      TextArea.Caret.Offset = offset;
      BringTextOffsetIntoView(offset);
    }
  }

  public void UninstallBlockFolding() {
    if (folding_ != null) {
      FoldingManager.Uninstall(folding_);
      folding_ = null;
    }
  }

  private void SetupBlockFolding() {
    if (settings_ is not DocumentSettings docSettings) {
      return;
    }

    if (!docSettings.ShowBlockFolding) {
      UninstallBlockFolding();
      return;
    }

    if (Function == null) {
      return; // Function couldn't be parsed.
    }

    UninstallBlockFolding();
    folding_ = FoldingManager.Install(TextArea);
    var foldingStrategy = Session.CompilerInfo.CreateFoldingStrategy(Function);
    foldingStrategy.UpdateFoldings(folding_, Document);
    SetupBlockFoldingEvents(folding_.AllFoldings);
  }

  public IEnumerable<FoldingSection> SetupCustomBlockFolding(IBlockFoldingStrategy foldingStrategy) {
    UninstallBlockFolding();
    folding_ = FoldingManager.Install(TextArea);
    foldingStrategy.UpdateFoldings(folding_, Document);
    SetupBlockFoldingEvents(folding_.AllFoldings);
    return folding_.AllFoldings;
  }

  private void SetupBlockFoldingEvents(IEnumerable<FoldingSection> foldings) {
    var foldingMargin = TextArea.LeftMargins.OfType<FoldingMargin>().FirstOrDefault();

    if (foldingMargin == null) {
      return;
    }

    // Set initial folded state.
    foldedBlocks_ = new HashSet<FoldingSection>();

    foreach (var folding in foldings) {
      if (folding.IsFolded) {
        foldedBlocks_.Add(folding);
      }
    }

    void DetectFoldingChanges() {
      // Check each folding if there is a change, since there is
      // no event for a single folding being changed.
      foreach (var folding in folding_.AllFoldings) {
        if (folding.IsFolded) {
          if (foldedBlocks_.Add(folding)) {
            TextRegionFolded?.Invoke(this, folding);
          }
        }
        else {
          if (foldedBlocks_.Remove(folding)) {
            TextRegionUnfolded?.Invoke(this, folding);
          }
        }
      }
    }

    foldingMargin.PreviewMouseLeftButtonUp += (sender, args) => DetectFoldingChanges();
    foldingMargin.PreviewTouchUp += (sender, args) => DetectFoldingChanges();
  }

  public void SetupCustomLineNumbers(LineNumberMargin lineNumbers) {
    // Disable builtin line numbers margin and replace it with a custom one,
    // plus a dotted line between it and the document itself.
    ShowLineNumbers = false;
    UninstallCustomLineNumbers(false);

    var leftMargins = TextArea.LeftMargins;
    var line = (Line)DottedLineMargin.Create();
    leftMargins.Insert(0, lineNumbers);
    leftMargins.Insert(1, line);
    var lineNumbersForeground = new Binding("LineNumbersForeground") {Source = this};
    line.SetBinding(Shape.StrokeProperty, lineNumbersForeground);
    lineNumbers.SetBinding(ForegroundProperty, lineNumbersForeground);
    hasCustomLineNumbers_ = true;
  }

  public void UninstallCustomLineNumbers(bool restoreBuiltin) {
    if (hasCustomLineNumbers_) {
      var leftMargins = TextArea.LeftMargins;

      for (int i = 0; i < leftMargins.Count; i++) {
        if (leftMargins[i] is LineNumberMargin ||
            leftMargins[i] is Line) {
          leftMargins.RemoveAt(i);
          i--;
        }
      }

      hasCustomLineNumbers_ = false;
    }

    ShowLineNumbers = restoreBuiltin;
  }

  public void RegisterTextTransformer(DocumentColorizingTransformer transformer) {
    if (!TextArea.TextView.LineTransformers.Contains(transformer)) {
      TextArea.TextView.LineTransformers.Add(transformer);
      registerdTransformers_.Add(transformer);
    }
  }

  public void UnregisterTextTransformer(DocumentColorizingTransformer transformer) {
    if (registerdTransformers_.Remove(transformer)) {
      TextArea.TextView.LineTransformers.Remove(transformer);
    }
  }

  public void UnregisterTextTransformers() {
    foreach (var transformer in registerdTransformers_) {
      TextArea.TextView.LineTransformers.Remove(transformer);
    }

    registerdTransformers_.Clear();
  }

  public void RegisterHoverPreview(PopupHoverPreview preview) {
    registeredHoverPreviews_.Add(preview);
  }

  public void UnregisterHoverPreviews() {
    foreach (var ppreview in registeredHoverPreviews_) {
      ppreview.Unregister();
    }

    registeredHoverPreviews_.Clear();
  }

  private void AddDiffTextSegments(List<DiffTextSegment> segments) {
    diffSegments_ = segments;
    diffHighlighter_.Add(segments);
  }

  public void RemoveDiffTextSegments() {
    diffHighlighter_.Clear();
    UpdateHighlighting();
  }

  private void StartDiffSegmentAdding() {
    diffHighlighter_.Clear();
    ClearAllMarkers();
    TextArea.IsEnabled = false;

    // Disable the caret event because it triggers redrawing of
    // the right marker bar, which besides being slow, causes a
    // GDI handle leak from the usage of RenderTargetBitmap (known WPF issue).
    disableCaretEvent_ = true;
    duringDiffModeSetup_ = true;
  }

  private void AllDiffSegmentsAdded() {
    disableCaretEvent_ = false;
    duringDiffModeSetup_ = false;
    TextArea.IsEnabled = true;

    // This forces the updating of the right bar with the diffed line markers
    // to execute after the editor displayed the scroll bars.
    Dispatcher.Invoke(() => UpdateHighlighting(), DispatcherPriority.Render);
  }

  public void SelectDocumentRemark(Remark remark) {
    var element = remark.ReferencedElements[0];
    HighlightElement(element, HighlighingType.Hovered);
    selectedRemark_ = remark;
  }

  public void AddRemarks(List<Remark> allRemarks, List<RemarkLineGroup> markerRemarksGroups,
                         bool hasContextFilter) {
    if (markerRemarksGroups != null) {
      AddMarginRemarks(markerRemarksGroups, hasContextFilter);
    }

    if (allRemarks != null) {
      AddDocumentRemarks(allRemarks, hasContextFilter);
    }

    UpdateMargin();
    UpdateHighlighting();
  }

  public void RemoveRemarks() {
    selectedRemark_ = null;
    remarkHighlighter_.Clear();
    margin_.RemoveRemarkBookmarks();
    UpdateMargin();
    UpdateHighlighting();
  }

  public void UpdateRemarks(List<Remark> allRemarks, List<RemarkLineGroup> markerRemarksGroups,
                            bool hasContextFilter) {
    RemoveRemarks();
    AddRemarks(allRemarks, markerRemarksGroups, hasContextFilter);
  }

  public void AddElementOverlay(IRElement element, IElementOverlay overlay) {
    overlayRenderer_.AddElementOverlay(element, overlay);
    UpdateHighlighting();
  }

  public IconElementOverlay AddIconElementOverlay(IRElement element, IconDrawing icon,
                                                  double width = 16, double height = 0,
                                                  string label = "", string tooltip = "",
                                                  HorizontalAlignment alignmentX = HorizontalAlignment.Right,
                                                  VerticalAlignment alignmentY = VerticalAlignment.Center,
                                                  double marginX = 8, double marginY = 4) {
    var overlay = RegisterIconElementOverlay(element, icon, width, height, label, tooltip,
                                             alignmentX, alignmentY, marginX, marginY);
    UpdateHighlighting();
    return overlay;
  }

  private IconElementOverlay RegisterIconElementOverlay(IRElement element, IconDrawing icon,
                                                        double width, double height,
                                                        string label, string tooltip,
                                                        HorizontalAlignment alignmentX,
                                                        VerticalAlignment alignmentY,
                                                        double marginX, double marginY) {
    var overlay = IconElementOverlay.CreateDefault(icon, width, height,
                                                   ColorBrushes.Transparent,
                                                   selectedStyle_.BackColor,
                                                   selectedStyle_.Border,
                                                   label, tooltip,
                                                   alignmentX, alignmentY,
                                                   marginX, marginY);
    return (IconElementOverlay)RegisterElementOverlay(element, overlay);
  }

  public IElementOverlay RegisterElementOverlay(IRElement element, string label, string tooltip) {
    return RegisterIconElementOverlay(element, null, 0, 0, label, tooltip);
  }

  public IElementOverlay RegisterElementOverlay(IRElement element, IElementOverlay overlay,
                                                bool prepend = false) {
    SetupElementOverlayEvents(overlay);
    overlayRenderer_.AddElementOverlay(element, overlay, prepend);
    return overlay;
  }

  public void RemoveElementOverlays(IRElement element, object onlyWithTag = null) {
    overlayRenderer_.RemoveAllElementOverlays(element, onlyWithTag);
  }

  public void ClearElementOverlays() {
    overlayRenderer_.ClearElementOverlays();
    UpdateHighlighting();
  }

  public void EnterDiffMode() {
    DiffModeEnabled = true;
    SectionText = ReadOnlyMemory<char>.Empty;
  }

  public void ExitDiffMode() {
    margin_.ClearMarkers();
    diffHighlighter_.Clear();
    SectionText = ReadOnlyMemory<char>.Empty;
    DiffModeEnabled = false;
  }

  //? TODO: This is a more efficient way of marking the loop blocks
  //? by using a single group that covers all blocks in a loop
  //private void MarkLoopNest(Loop loop, HashSet<BlockIR> handledBlocks) {
  //    foreach(var nestedLoop in loop.NestedLoops) {
  //        MarkLoopNest(nestedLoop, handledBlocks);
  //    }

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

  public DocumentLine GetLineByNumber(int lineNumber) {
    return Document.GetLineByNumber(lineNumber);
  }

  public DocumentLine GetLineByOffset(int offset) {
    return Document.GetLineByOffset(offset);
  }

  public void MarkBlock(IRElement element, Color selectedColor, bool raiseEvent = true) {
    var style = new HighlightingStyle(selectedColor, null);
    MarkBlock(element, style, raiseEvent);
  }

  public void MarkBlock(IRElement element, Brush selectedColor, bool raiseEvent = true) {
    var style = new HighlightingStyle(selectedColor, null);
    MarkBlock(element, style, raiseEvent);
  }

  public void MarkElements(ICollection<ValueTuple<IRElement, Brush>> elementColorPairs) {
    var colorGroupMap = new Dictionary<Brush, HighlightedElementGroup>(elementColorPairs.Count);
    ClearTemporaryHighlighting();

    foreach (var pair in elementColorPairs) {
      var element = pair.Item1;
      var color = pair.Item2;

      if (!colorGroupMap.TryGetValue(color, out var group)) {
        var style = new HighlightingStyle(color, null);
        group = new HighlightedElementGroup(style);
        colorGroupMap[color] = group;
      }

      group.Add(element);
    }

    foreach (var pair in colorGroupMap) {
      markedHighlighter_.Add(pair.Value);
    }

    UpdateHighlighting();
  }

  public void ClearInstructionMarkers() {
    markedHighlighter_.ForEachElement(RaiseElementRemoveHighlightingEvent);
    markedHighlighter_.Clear();
    UpdateHighlighting();
  }

  public IconElementOverlay RegisterIconElementOverlay(IRElement element, IconDrawing icon,
                                                       double width, double height,
                                                       string label = null, string tooltip = null,
                                                       bool prepend = false) {
    var overlay = IconElementOverlay.CreateDefault(icon, width, height,
                                                   ColorBrushes.Transparent,
                                                   selectedStyle_.BackColor,
                                                   selectedStyle_.Border,
                                                   label, tooltip);
    return (IconElementOverlay)RegisterElementOverlay(element, overlay, prepend);
  }

  public void SuspendUpdate() {
    updateSuspended_ = true;
    disableCaretEvent_ = true;
    overlayRenderer_.SuspendUpdate();
    markerBarUpdateTask_.CancelTask();
    markerBarUpdateLock_.EnterWriteLock();
  }

  public void ResumeUpdate() {
    updateSuspended_ = false;
    disableCaretEvent_ = false;
    overlayRenderer_.ResumeUpdate();
    markerBarUpdateLock_.ExitWriteLock();
    UpdateHighlighting();
  }

  public bool HandleOverlayKeyPress(KeyEventArgs e) {
    return overlayRenderer_.KeyPressed(e);
  }

  private void IRDocument_PreviewKeyDown(object sender, KeyEventArgs e) {
    if (margin_.SelectedBookmark != null) {
      e.Handled = EditBookmarkText(margin_.SelectedBookmark, e.Key);
      return;
    }

    // Notify overlay layer in case there is a selected overlay visual.
    if (overlayRenderer_.KeyPressed(e)) {
      e.Handled = true;
      return;
    }

    switch (e.Key) {
      case Key.Return: {
        if (Utils.IsAltModifierActive()) {
          PreviewDefinitionExecuted(this, null);
        }
        else {
          if (GetSelectedElement() is var element &&
              TryOpenFunctionCallTarget(element)) {
          }
          else {
            GoToDefinitionExecuted(this, null);
          }
        }

        e.Handled = true;
        break;
      }
      case Key.Escape: {
        HidePreviewPopup();
        e.Handled = true;
        break;
      }
      // Saving of markings with profiling not yet implemented.
      // case Key.M: {
      //   if (Utils.IsControlModifierActive()) {
      //     if (Utils.IsShiftModifierActive()) {
      //       MarkBlockExecuted(this, null);
      //     }
      //     else {
      //       MarkExecuted(this, null);
      //     }
      //   }
      //
      //   e.Handled = true;
      //   break;
      // }
      // case Key.D: {
      //   if (Utils.IsControlModifierActive()) {
      //     if (Utils.IsShiftModifierActive()) {
      //       MarkDefinitionBlockExecuted(this, null);
      //     }
      //     else {
      //       MarkDefinitionExecuted(this, null);
      //     }
      //   }
      //
      //   e.Handled = true;
      //   break;
      // }
      // case Key.U: {
      //   if (Utils.IsControlModifierActive()) {
      //     if (Utils.IsShiftModifierActive()) {
      //       MarkUsesExecuted(this, null);
      //     }
      //     else {
      //       ShowUsesExecuted(this, null);
      //     }
      //   }
      //
      //   e.Handled = true;
      //   break;
      // }
      // case Key.Delete: {
      //   if (Utils.IsControlModifierActive() ||
      //       Utils.IsShiftModifierActive()) {
      //     ClearAllMarkersExecuted(this, null);
      //   }
      //   else {
      //     ClearMarkerExecuted(this, null);
      //   }
      //
      //   e.Handled = true;
      //   break;
      // }
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
        if (Utils.IsControlModifierActive()) {
          ShowExpressionGraphExecuted(this, null);
          e.Handled = true;
        }

        break;
      }
    }
  }

  private void SetupStyles() {
    if (settings_ is not DocumentSettings docSettings) {
      // Selection styles are also used in non-IR documents,
      // make sure they are not null and have reasonable defaults.
      selectedStyle_ = new HighlightingStyle {
        BackColor = ColorBrushes.GetTransparentBrush(settings_.SelectedValueColor, 0.5),
        Border = settings_.CurrentLineBorderColor.AsPen()
      };

      selectedBlockStyle_ = new HighlightingStyle {
        BackColor = settings_.BackgroundColor.AsBrush(),
        Border = settings_.CurrentLineBorderColor.AsPen()
      };
      return;
    }

    var borderPen = ColorPens.GetBoldPen(docSettings.BorderColor);
    var lightBorderPen = ColorPens.GetTransparentPen(docSettings.BorderColor, 150);

    selectedStyle_ ??= new HighlightingStyle {
      BackColor = ColorBrushes.GetTransparentBrush(settings_.SelectedValueColor, 0.5),
      Border = borderPen
    };

    selectedBlockStyle_ = selectedStyle_;
    ssaUserStyle_ ??= new PairHighlightingStyle();
    ssaUserStyle_.ParentStyle.BackColor =
      ColorBrushes.GetBrush(
        ColorUtils.AdjustLight(docSettings.UseValueColor, ParentStyleLightAdjustment));

    ssaUserStyle_.ChildStyle.BackColor = ColorBrushes.GetBrush(docSettings.UseValueColor);
    ssaUserStyle_.ChildStyle.Border = borderPen;

    iteratedUserStyle_ = new PairHighlightingStyle {
      ParentStyle = {
        BackColor = ColorBrushes.GetTransparentBrush(docSettings.UseValueColor, 0)
      },
      ChildStyle = {
        BackColor = ColorBrushes.GetTransparentBrush(docSettings.UseValueColor, 50),
        Border = lightBorderPen
      }
    };

    ssaDefinitionStyle_ = new PairHighlightingStyle {
      ParentStyle = {
        BackColor = ColorBrushes.GetBrush(
          ColorUtils.AdjustLight(docSettings.DefinitionValueColor, ParentStyleLightAdjustment))
      },
      ChildStyle = {
        BackColor = ColorBrushes.GetBrush(docSettings.DefinitionValueColor),
        Border = borderPen
      }
    };

    iteratedDefinitionStyle_ = new PairHighlightingStyle {
      ParentStyle = {
        BackColor = ColorBrushes.GetTransparentBrush(docSettings.DefinitionValueColor, 0)
      },
      ChildStyle = {
        BackColor = ColorBrushes.GetTransparentBrush(docSettings.DefinitionValueColor, 50),
        Border = lightBorderPen
      }
    };
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
#if DEBUG
      Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Record undo action {action}");
#endif
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

  private ElementHighlighter GetHighlighter(HighlighingType type) {
    return type switch {
      HighlighingType.Hovered  => hoverHighlighter_,
      HighlighingType.Selected => selectedHighlighter_,
      HighlighingType.Marked   => markedHighlighter_,
      _                        => throw new Exception("Unknown type")
    };
  }

  private void MarkElementsOnSourceLine(ElementHighlighter highlighter, int lineNumber, Color selectedColor,
                                        bool raiseEvent, bool bringIntoView, SourceStackFrame inlinee) {
    var style = highlighter == selectedHighlighter_ ? selectedStyle_ : new HighlightingStyle(selectedColor);
    var group = new HighlightedElementGroup(style);
    IRElement firstTuple = null;

    foreach (var block in Function.Blocks) {
      foreach (var tuple in block.Tuples) {
        var sourceTag = tuple.GetTag<SourceLocationTag>();
        bool found = false;

        if (sourceTag == null) {
          continue;
        }

        if (inlinee == null) {
          found = sourceTag.Line == lineNumber;
        }
        else if (sourceTag.HasInlinees) {
          found = sourceTag.Inlinees.Find(item => item.Function == inlinee.Function &&
                                                  item.Line == lineNumber) != null;
        }

        if (found) {
          group.Add(tuple);

          if (firstTuple == null) {
            firstTuple = tuple;
          }

          if (raiseEvent) {
            RaiseElementHighlightingEvent(tuple, group, highlighter.Type,
                                          HighlightingEventAction.AppendHighlighting);
          }
        }
      }
    }

    if (!group.IsEmpty()) {
      highlighter.Add(group);

      if (bringIntoView) {
        BringElementIntoView(firstTuple);
      }
    }

    UpdateHighlighting();
  }

  private IRElement CreateDummyElement(int offset, int length) {
    int line = Document.GetLineByOffset(offset).LineNumber;
    var location = new TextLocation(offset, line, 0);
    return new IRElement(location, length);
  }

  private void HandelNoElementSelection() {
    UpdateHighlighting();
    RaiseElementUnselectedEvent();
    RaiseElementHighlightingEvent(null, null, HighlighingType.Selected,
                                  HighlightingEventAction.ReplaceHighlighting);
  }

  private void AddBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
    var selectedElement = GetSelectedElement();

    if (selectedElement != null) {
      AddBookmark(selectedElement);
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
      if (currentBlock_ != previousBlock) {
        RaiseBlockSelectedEvent(currentBlock_);
      }

      RaiseElementSelectedEvent(element);
    }
  }

  private void ScrollBar_MouseLeave(object sender, MouseEventArgs e) {
    if (hoveredBarElement_ != null) {
      hoveredBarElement_.Style.Border = null;
      hoveredBarElement_ = null;
      RenderMarkerBar();
    }

    HideTemporaryUI();
  }

  private void ScrollBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    HideTemporaryUI();
    var position = e.GetPosition(markerMargin_);
    var barElement = FindMarkerBarElement(position);

    if (barElement != null && barElement.HandlesInput) {
      ignoreNextPreviewElement_ = barElement.Element;
      ignoreNextBarHover_ = true;
      ignoreNextScrollEvent_ = true;
      BringElementIntoView(barElement.Element);
      e.Handled = true;
    }
  }

  private async void ScrollBar_PreviewMouseMove(object sender, MouseEventArgs e) {
    if (ignoreNextBarHover_) {
      ignoreNextBarHover_ = false;
      return;
    }

    if (e.LeftButton != MouseButtonState.Released ||
        e.RightButton != MouseButtonState.Released) {
      return;
    }

    bool needsRendering = false;

    if (hoveredBarElement_ != null) {
      hoveredBarElement_.Style.Border = null;
      hoveredBarElement_ = null;
      needsRendering = true;
    }

    var position = e.GetPosition(markerMargin_);
    var barElement = FindMarkerBarElement(position);

    if (barElement != null && barElement.HandlesInput) {
      barElement.Style.Border = ColorPens.GetBoldPen(Colors.Black);
      hoveredBarElement_ = barElement;
      needsRendering = true;
      await ShowPreviewPopup(barElement.Element);
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

    // Trigger event, used during diff mode to sync caret with other document.
    CaretChanged?.Invoke(this, TextArea.Caret.Offset);

    if (ignoreNextCaretEvent_) {
      ignoreNextCaretEvent_ = false;
      return;
    }

    var element = FindElementAtOffset(TextArea.Caret.Offset);
    SelectElement(element, true, false, TextArea.Caret.Offset);
  }

  private void ClearAllMarkersExecuted(object sender, ExecutedRoutedEventArgs e) {
    ClearAllMarkers();
  }

  private void ClearAllMarkers() {
    ClearBlockMarkers();
    ClearInstructionMarkers();
    overlayRenderer_.Clear();
    TextArea.TextView.Redraw();
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

  private void ClearMarkerExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GetSelectedElement() is var element) {
      ClearMarkedElement(element);
    }
  }

  public void ClearSelectedElements() {
    selectedHighlighter_.Clear();
    selectedElements_.Clear();
    UpdateHighlighting();
  }

  private void ClearTemporaryHighlighting(bool clearSelected = true) {
    if (!IsLoaded) {
      return;
    }

    HideHoverHighlighting();

    if (clearSelected) {
      ClearSelectedElements();
    }

    if (overlayRendererConnectedTemporarely_) {
      overlayRenderer_.ClearConnectedElements();
    }
  }

  private void CreateRightMarkerMargin() {
    if (markerMargin_ != null) {
      return; // Margin already set up.
    }

    // Find the right scrollbar and place the canvas under it,
    // then make it semi-transparent so that the canvas is visible.
    var scrollViewer = Utils.FindChild<ScrollViewer>(this);

    if (scrollViewer != null) {
      docVerticalScrollBar_ = Utils.FindChild<ScrollBar>(scrollViewer);

      if (docVerticalScrollBar_ != null) {
        docVerticalScrollBar_.Opacity = 0.6;
        docVerticalScrollBar_.PreviewMouseMove += ScrollBar_PreviewMouseMove;
        docVerticalScrollBar_.PreviewMouseLeftButtonDown += ScrollBar_PreviewMouseLeftButtonDown;
        docVerticalScrollBar_.MouseLeave += ScrollBar_MouseLeave;
        var parent = VisualTreeHelper.GetParent(docVerticalScrollBar_) as Grid;
        markerMargin_ = new Canvas();
        Panel.SetZIndex(markerMargin_, 1);
        Panel.SetZIndex(docVerticalScrollBar_, 2);
        Grid.SetColumn(markerMargin_, Grid.GetColumn(docVerticalScrollBar_));
        markerMargin_.Background = Brushes.White;
        parent.Children.Add(markerMargin_);
      }
    }
  }

  private void ResetRenderers() {
    markerBarUpdateTask_.CancelTask();
    bookmarks_?.Clear();
    hoverHighlighter_?.Clear();
    selectedHighlighter_?.Clear();
    markedHighlighter_?.Clear();
    blockHighlighter_?.Clear();
    diffHighlighter_?.Clear();
    remarkHighlighter_?.Clear();
    overlayRenderer_?.Clear();
    margin_?.Reset();
  }

  private bool EditBookmarkText(Bookmark bookmark, Key key) {
    // Because a TextBox is not used for the bookmarks,
    // editing the optional text must be handled manually.
    // This method is not part of Bookmark because it handles lots
    // of state through the BookmarkManager.
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
    // Exit if the element lists are still being computed or
    // for source code documents.
    if (DuringSectionLoading || Function == null) {
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

  private MarkerBarElement FindMarkerBarElement(Point position) {
    if (markerMargingElements_ != null) {
      //? TODO: This is inefficient, at least sort by Y and do binary search
      foreach (var barElement in markerMargingElements_) {
        if (barElement.Element == null) {
          continue;
        }

        if (barElement.Visual.Contains(position)) {
          return barElement;
        }

        if (barElement.Visual.Top > position.Y) {
          break;
        }
      }
    }

    return null;
  }

  private IRElement FindPointedElement(Point position, out int textOffset) {
    if (Function == null) { // For source code documents.
      textOffset = 0;
      return null;
    }

    int offset = DocumentUtils.GetOffsetFromMousePosition(position, this, out _);

    if (offset != -1) {
      textOffset = offset;
      return FindElementAtOffset(offset);
    }

    textOffset = -1;
    return null;
  }

  private void FirstBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
    JumpToBookmark(bookmarks_.JumpToFirstBookmark());
  }

  private HighlightingStyle GetMarkerStyleForCommand(ExecutedRoutedEventArgs e) {
    if (e != null && e.Parameter is SelectedColorEventArgs colorArgs) {
      return new HighlightingStyle(colorArgs.SelectedColor);
    }

    return PickMarkerStyle();
  }

  private PairHighlightingStyle GetPairMarkerStyleForCommand(ExecutedRoutedEventArgs e) {
    if (e != null && e.Parameter is SelectedColorEventArgs colorArgs) {
      var style = new HighlightingStyle(colorArgs.SelectedColor);

      return new PairHighlightingStyle {
        ChildStyle = new HighlightingStyle(colorArgs.SelectedColor),
        ParentStyle = new HighlightingStyle(Utils.ChangeColorLuminisity(colorArgs.SelectedColor, 1.4))
      };
    }

    return PickPairMarkerStyle();
  }

  private PairHighlightingStyle GetReferenceStyle(Reference reference) {
    //? TODO: Expose colors in settings
    return reference.Kind switch {
      ReferenceKind.Address => new PairHighlightingStyle {
        ChildStyle = new HighlightingStyle("#FF9090", ColorPens.GetBoldPen(Colors.DarkRed)),
        ParentStyle = new HighlightingStyle("#FFC9C9")
      },
      ReferenceKind.Load => new PairHighlightingStyle {
        ChildStyle = new HighlightingStyle("#BDBAEC", ColorPens.GetPen(Colors.DarkBlue)),
        ParentStyle = new HighlightingStyle("#D9D8F4")
      },
      ReferenceKind.Store => ssaUserStyle_,
      ReferenceKind.SSA => new PairHighlightingStyle {
        ChildStyle = new HighlightingStyle("#BAD6EC", ColorPens.GetPen(Colors.DarkBlue)),
        ParentStyle = new HighlightingStyle("#D8E9F4")
      },
      _ => throw new InvalidOperationException("Unknown ReferenceKind")
    };
  }

  public IRElement GetSelectedElement() {
    if (selectedElements_.Count == 0) {
      return null;
    }

    var selectedEnum = selectedElements_.GetEnumerator();
    selectedEnum.MoveNext();
    return selectedEnum.Current;
  }

  private OperandIR GetSelectedElementDefinition() {
    var defs = GetSelectedElementDefinitions();
    return defs.Count == 1 ? defs[0] : null;
  }

  private List<OperandIR> GetSelectedElementDefinitions() {
    var selectedOp = GetSelectedElement();

    if (!(selectedOp is OperandIR op)) {
      return new List<OperandIR>();
    }

    var refFinder = CreateReferenceFinder();
    return refFinder.FindAllDefinitions(op).
      ConvertAll(item => item as OperandIR);
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
    if (GetSelectedElement() is var element) {
      if (!TryOpenFunctionCallTarget(element)) {
        GoToElementDefinition(element);
        MirrorAction(DocumentActionKind.GoToDefinition, element);
      }
    }
  }

  private async void PreviewDefinitionExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GetSelectedElement() is var element) {
      await ShowDefinitionPreview(element, true);
    }
  }

  private void GoToDefinitionSkipCopiesExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GetSelectedElement() is var element) {
      if (!TryOpenFunctionCallTarget(element)) {
        GoToElementDefinition(element, true);
        MirrorAction(DocumentActionKind.GoToDefinition, element);
      }
    }
  }

  private IRElement SkipAllCopies(IRElement op) {
    //? TODO: This should be in some IR utility class.
    var defInstr = op.ParentInstruction;

    while (defInstr != null) {
      var sourceOp = Session.CompilerInfo.IR.SkipCopyInstruction(defInstr);

      if (sourceOp != null && sourceOp != defInstr) {
        op = sourceOp;
        defInstr = sourceOp.ParentInstruction;
      }
      else {
        break;
      }
    }

    return op;
  }

  private ReferenceFinder CreateReferenceFinder() {
    return DocumentUtils.CreateReferenceFinder(Function, Session,
                                               settings_ as DocumentSettings);
  }

  private bool GoToElementDefinition(IRElement element, bool skipCopies = false) {
    RecordReversibleAction(DocumentActionKind.GoToDefinition, element);
    ClearTemporaryHighlighting();

    if (element == null) {
      return false;
    }

    if (element is OperandIR op) {
      // If it's a destination operand and has a single user, jump to it.
      var refFinder = CreateReferenceFinder();

      if (op.Role == OperandRole.Destination) {
        var useOp = refFinder.GetSingleUse(op);

        if (useOp != null) {
          HighlightSingleElement(useOp, selectedHighlighter_);
          SetCaretAtElement(useOp);
          return true;
        }
      }

      // Try to find a definition for the source operand.
      var defOp = refFinder.FindSingleDefinition(op);

      if (defOp != null) {
        if (skipCopies) {
          defOp = SkipAllCopies(defOp);
        }

        HighlightSingleElement(defOp, selectedHighlighter_);
        SetCaretAtElement(defOp);
        return true;
      }

      if (op.IsLabelAddress) {
        // For labels go to the target block.
        var targetOp = op.BlockLabelValue;
        HighlightSingleElement(targetOp, selectedHighlighter_);
        SetCaretAtElement(targetOp);
        return true;
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
      highlighted = HandleInstructionElement(instr, highlighter, markExpression, ref action);
    }
    else if (element is BlockLabelIR blockLabel) {
      highlighted = HandleBlockLabel(highlighter, action, blockLabel);
    }

    if (!highlighted) {
      HandleOtherElement(element, highlighter, action);
    }
  }

  private bool HandleBlockLabel(ElementHighlighter highlighter, HighlightingEventAction action,
                                BlockLabelIR blockLabel) {
    var block = blockLabel.Parent;

    if (block.Predecessors.Count == 0) {
      return false;
    }

    // Mark all branches referencing the label.
    var group = new HighlightedElementGroup(ssaDefinitionStyle_.ChildStyle);

    foreach (var predBlock in block.Predecessors) {
      var branchInstr = Session.CompilerInfo.IR.GetTransferInstruction(predBlock);

      if (branchInstr != null) {
        group.Add(branchInstr);
        RaiseElementHighlightingEvent(branchInstr, group, highlighter.Type, action);
      }
    }

    // Mark the label itself.
    group.Add(blockLabel);
    RaiseElementHighlightingEvent(blockLabel, group, highlighter.Type, action);
    highlighter.Add(group);
    return true;
  }

  private bool HandleInstructionElement(InstructionIR instr, ElementHighlighter highlighter,
                                        bool markExpression, ref HighlightingEventAction action) {
    if (markExpression) {
      // Mark an entire SSA def-use expression DAG.
      HighlightExpression(instr, highlighter, expressionOperandStyle_, expressionStyle_);
      return true;
    }

    if (settings_ is not DocumentSettings docSettings) {
      return false;
    }

    if (!docSettings.HighlightInstructionOperands) {
      return false;
    }

    // Highlight each source operand. The instruction itself
    // is also highlighted below, in append mode.
    action = HighlightingEventAction.AppendHighlighting;
    RaiseRemoveHighlightingEvent(highlighter.Type);

    foreach (var sourceOp in instr.Sources) {
      if (sourceOp.IsLabelAddress) {
        HighlightBlockLabel(sourceOp, highlighter, ssaUserStyle_, action);
      }
      else {
        HighlightDefinition(sourceOp, highlighter, ssaDefinitionStyle_, action, false);
      }
    }

    if (docSettings.HighlightDestinationUses) {
      foreach (var destOp in instr.Destinations) {
        HandleOperandElement(destOp, highlighter, markExpression, false, action);
      }
    }

    return true;
  }

  private bool HandleOperandElement(OperandIR op, ElementHighlighter highlighter,
                                    bool markExpression, bool markReferences,
                                    HighlightingEventAction action) {
    if (settings_ is not DocumentSettings docSettings) {
      return false;
    }

    if (op.Role == OperandRole.Source) {
      if (markExpression) {
        // Mark an entire SSA def-use expression DAG.
        HighlightExpression(op, highlighter, expressionOperandStyle_, expressionStyle_);
        return true;
      }

      // Further handling of sources is done below.
    }
    else if ((op.Role == OperandRole.Destination || op.Role == OperandRole.Parameter) &&
             docSettings.HighlightDestinationUses) {
      // First look for an SSA definition and its uses,
      // if not found highlight every load of the same symbol.
      var refFinder = CreateReferenceFinder();
      var useList = refFinder.FindAllUses(op);
      List<IRElement> iteratedUseList = null;
      bool handled = false;

      //? TODO: Default expansion needs an option
      if (markExpression) {
        // Collect the transitive set of users, marking instructions
        // that depend on the value of this destination operand.
        iteratedUseList = ExpandIteratedUseList(op, useList);
        handled = true;
      }
      else if (markReferences) {
        MarkReferences(op, highlighter);
      }

      if (useList.Count > 0) {
        HighlightUsers(op, useList, highlighter, ssaUserStyle_, action);
        handled = true;

        if (iteratedUseList != null && iteratedUseList.Count > 0) {
          HighlightUsers(op, iteratedUseList, highlighter, iteratedUserStyle_, action);
        }
      }

      // If the operand is an indirection, also try to mark
      // the definition of the base address value below.
      if (!op.IsIndirection) {
        return handled;
      }
    }

    if (docSettings.HighlightSourceDefinition) {
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

  private List<IRElement> ExpandIteratedUseList(OperandIR operand, List<IRElement> useList) {
    var handledElements = new HashSet<IRElement>(useList.Count);

    foreach (var use in useList) {
      handledElements.Add(use);
    }

    // Each expansion of the same element doubles the recursion depth.
    if (currentExprElement_ == operand) {
      currentExprLevel_ += ExpressionLevelIncrement;
    }
    else {
      currentExprElement_ = operand;
      currentExprLevel_ = DefaultMaxExpressionLevel;
    }

    int maxLevel = currentExprLevel_;
    var refFinder = CreateReferenceFinder();
    var newUseList = new List<IRElement>(useList);

    ExpandIteratedUseList(newUseList, handledElements, 0, maxLevel, refFinder);
    newUseList.RemoveRange(0, useList.Count);
    return newUseList;
  }

  private void ExpandIteratedUseList(List<IRElement> useList, HashSet<IRElement> handledElements,
                                     int level, int maxLevel, ReferenceFinder refFinder) {
    if (level > maxLevel) {
      return;
    }

    var newUseLists = new List<List<IRElement>>(1 << maxLevel - level + 1);

    foreach (var use in useList) {
      if (use is not OperandIR op) {
        continue;
      }

      var useInstr = op.ParentInstruction;

      if (useInstr != null) {
        foreach (var iteratedUse in refFinder.FindAllUses(useInstr)) {
          if (!handledElements.Add(iteratedUse)) {
            continue; // Use already visited during recursion.
          }

          // Recursively iterate over and collect uses.
          var iteratedUseList = new List<IRElement>(1 << maxLevel - level);
          iteratedUseList.Add(iteratedUse);

          ExpandIteratedUseList(iteratedUseList, handledElements,
                                level + 1, maxLevel, refFinder);
          newUseLists.Add(iteratedUseList);
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
    var group = new HighlightedElementGroup(element, style);
    highlighter.Add(group);
    RaiseElementHighlightingEvent(element, group, highlighter.Type, action);
  }

  private void HideTemporaryUI() {
    HidePreviewPopup();
    margin_.UnselectBookmark();
  }

  private void HidePreviewPopup(bool force = false) {
    ignoreNextPreviewElement_ = null;

    if (previewPopup_ != null && (force || !previewPopup_.IsMouseOver)) {
      previewPopup_.ClosePopup();
      previewPopup_ = null;
    }
  }

  private bool HighlightBlockLabel(OperandIR op, ElementHighlighter highlighter,
                                   PairHighlightingStyle style, HighlightingEventAction action) {
    var labelOp = op.BlockLabelValue;

    if (labelOp?.Parent == null) {
      return false;
    }

    var group = new HighlightedElementGroup(style.ChildStyle);
    group.Add(op);
    group.Add(labelOp);
    highlighter.Add(group);
    RaiseElementHighlightingEvent(op, group, highlighter.Type, action);
    return true;
  }

  private HighlightedElementGroup HighlightDefinedOperand(IRElement op, IRElement defElement,
                                                          ElementHighlighter highlighter,
                                                          PairHighlightingStyle style) {
    var group = new HighlightedElementGroup(style.ChildStyle);
    group.Add(op);
    group.Add(defElement);
    highlighter.Add(group);
    return group;
  }

  private HighlightedElementGroup HighlightInstruction(InstructionIR instr, ElementHighlighter highlighter,
                                                       PairHighlightingStyle style) {
    var group = new HighlightedElementGroup(instr, style.ParentStyle);
    highlighter.Add(group);
    return group;
  }

  private HighlightedElementGroup HighlightOperand(IRElement op, ElementHighlighter highlighter,
                                                   PairHighlightingStyle style) {
    var group = new HighlightedElementGroup(op, style.ChildStyle);
    highlighter.Add(group);
    return group;
  }

  private void HighlightSingleElement(IRElement element, ElementHighlighter highlighter) {
    ClearTemporaryHighlighting(highlighter.Type == HighlighingType.Selected);
    highlighter.Clear();

    if (element is OperandIR op && op.Parent != null) {
      // Also highlight operand's parent instruction.
      highlighter.Add(new HighlightedElementGroup(op.Parent, definitionStyle_.ParentStyle));
    }

    if (element is BlockIR && highlighter.Type == HighlighingType.Selected) {
      highlighter.Add(new HighlightedElementGroup(element, selectedBlockStyle_));
    }
    else {
      highlighter.Add(new HighlightedElementGroup(element, definitionStyle_.ChildStyle));
    }

    BringElementIntoView(element);
  }

  private bool HighlightDefinition(OperandIR op, ElementHighlighter highlighter,
                                   PairHighlightingStyle style, HighlightingEventAction action,
                                   bool highlightDefInstr = true) {
    // First look for an SSA definition, if not found
    // highlight every store to the same symbol.
    var refFinder = CreateReferenceFinder();
    var defList = refFinder.FindAllDefinitions(op);

    if (defList.Count == 0) {
      return false;
    }

    // Highlight the definition instructions.
    if (highlightDefInstr) {
      var instrGroup = new HighlightedElementGroup(style.ParentStyle);

      foreach (var element in defList) {
        instrGroup.Add(element.ParentTuple);
      }

      highlighter.Add(instrGroup);
    }

    // Highlight the definitions.
    var group = new HighlightedElementGroup(op, style.ChildStyle);

    foreach (var element in defList) {
      group.Add(element);
    }

    highlighter.Add(group);
    RaiseElementHighlightingEvent(op, group, highlighter.Type, action);
    return true;
  }

  private void HighlightExpression(IRElement element, ElementHighlighter highlighter,
                                   HighlightingStyleCollection style,
                                   HighlightingStyleCollection instrStyle) {
    var locationTag = element.GetTag<SourceLocationTag>();
    var handledElements = new HashSet<IRElement>(128);

    // Each expansion of the same element doubles the recursion depth.
    if (currentExprElement_ == element) {
      currentExprLevel_ += ExpressionLevelIncrement;
    }
    else {
      currentExprElement_ = element;
      currentExprStyleIndex_ = new Random().Next(style.Styles.Count - 1);
      currentExprLevel_ = DefaultMaxExpressionLevel;
    }

    int maxLevel = currentExprLevel_;
    int styleIndex = currentExprStyleIndex_;
    var refFinder = CreateReferenceFinder();
    HighlightExpression(element, null, handledElements,
                        highlighter, style, instrStyle, styleIndex,
                        0, maxLevel, refFinder);
  }

  private void HighlightExpression(IRElement element, IRElement parent, HashSet<IRElement> handledElements,
                                   ElementHighlighter highlighter, HighlightingStyleCollection style,
                                   HighlightingStyleCollection instrStyle, int styleIndex,
                                   int level, int maxLevel, ReferenceFinder refFinder) {
    if (!handledElements.Add(element)) {
      return; // Element already handled during recursion.
    }

    switch (element) {
      case OperandIR op: {
        //highlighter.Add(new HighlightedGroup(element, style.ForIndex(styleIndex)));
        highlighter.Add(new HighlightedElementGroup(element, iteratedDefinitionStyle_.ChildStyle));

        if (level >= maxLevel) {
          return;
        }

        var sourceDefOp = refFinder.FindSingleDefinition(op);

        if (sourceDefOp != null) {
          //highlighter.Add(new HighlightedGroup(sourceDefOp, style.ForIndex(styleIndex)));
          highlighter.Add(new HighlightedElementGroup(sourceDefOp, iteratedDefinitionStyle_.ChildStyle));

          if (sourceDefOp.ParentInstruction != null) {
            HighlightExpression(sourceDefOp.ParentInstruction, parent, handledElements,
                                highlighter, style, instrStyle, styleIndex,
                                level, maxLevel, refFinder);
          }
        }

        break;
      }
      case InstructionIR instr: {
        //highlighter.Add(new HighlightedGroup(instr, instrStyle.ForIndex(styleIndex)));
        highlighter.Add(new HighlightedElementGroup(instr, iteratedDefinitionStyle_.ParentStyle));

        if (level >= maxLevel) {
          return;
        }

        foreach (var sourceOp in instr.Sources) {
          HighlightExpression(sourceOp, instr, handledElements, highlighter, style,
                              instrStyle, styleIndex, level + 1, maxLevel, refFinder);
        }

        if (level > 0) {
          foreach (var destOp in instr.Destinations) {
            HighlightExpression(destOp, instr, handledElements, highlighter, style,
                                instrStyle, styleIndex, level + 1, maxLevel, refFinder);
          }
        }

        break;
      }
    }
  }

  private void ResetExpressionLevel(IRElement element = null) {
    if (element == null || element != currentExprElement_ ||
        !Utils.IsControlModifierActive()) {
      currentExprElement_ = null;
      currentExprLevel_ = 0;
    }
  }

  private void HighlightUsers(OperandIR op, List<IRElement> useList, ElementHighlighter highlighter,
                              PairHighlightingStyle style, HighlightingEventAction action) {
    var instrGroup = new HighlightedElementGroup(style.ParentStyle);
    var useGroup = new HighlightedElementGroup(style.ChildStyle);
    useGroup.Add(op);

    //? TODO: Implement arrows - either to all uses, or just ones outside view
    // overlayRenderer_.SetRootElement(op, new HighlightingStyle(Colors.Black));
    // overlayRendererConnectedTemporarely_ = highlighter.Type != HighlighingType.Marked;
    // var arrowStyle = new HighlightingStyle(Colors.DarkGreen, ColorPens.GetDashedPen(Colors.DarkGreen, DashStyles.Dash, 1.5));

    foreach (var use in useList) {
      useGroup.Add(use);
      var useInstr = use.ParentTuple;

      if (useInstr != null) {
        instrGroup.Add(useInstr);
      }

      // overlayRenderer_.AddConnectedElement(use.Element, arrowStyle);
    }

    highlighter.Add(instrGroup);
    highlighter.Add(useGroup);
    RaiseElementHighlightingEvent(op, useGroup, highlighter.Type, action);
  }

  private async void IRDocument_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e) {
    // If Ctrl is pressed, instead of go to definition
    // the action is a text search, which is handled by the host.
    if (Utils.IsControlModifierActive()) {
      return;
    }

    var position = e.GetPosition(TextArea.TextView);
    var element = FindPointedElement(position, out _);

    if (TryOpenFunctionCallTarget(element)) {
      e.Handled = true;
      return;
    }

    e.Handled = GoToElementDefinition(element, Utils.IsAltModifierActive());
  }

  private bool TryOpenFunctionCallTarget(IRElement element) {
    // For call function names, notify owner in case it wants
    // to switch the view to the called function.
    if (IsCallTargetElement(element)) {
      var targetSection = DocumentUtils.FindCallTargetSection(element, Section, Session);

      if (targetSection != null && FunctionCallOpen != null) {
        FunctionCallOpen.Invoke(this, targetSection);
        return true;
      }
    }

    return false;
  }

  private async void IRDocument_PreviewMouseHover(object sender, MouseEventArgs e) {
    if (settings_ is not DocumentSettings docSettings) {
      return;
    }

    if (ignoreNextHoverEvent_) {
      ignoreNextHoverEvent_ = false;
      return;
    }

    bool highlightElement = docSettings.ShowInfoOnHover &&
                            (!docSettings.ShowInfoOnHoverWithModifier ||
                             Utils.IsKeyboardModifierActive());

    if (!highlightElement) {
      return;
    }

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
        bool previewDisplayed = await ShowDefinitionPreview(element);

        if (previewDisplayed) {
          HideHoverHighlighting();
          hoveredElement_ = element;
          HandleElement(element, hoverHighlighter_,
                        Utils.IsControlModifierActive(),
                        Utils.IsShiftModifierActive());
          UpdateHighlighting();
          return;
        }
      }
    }

    HideHoverHighlighting();
    UpdateHighlighting();
  }

  private void IRDocument_PreviewMouseHoverStopped(object sender, MouseEventArgs e) {
    removeHoveredAction_ = DelayedAction.StartNew(() => {
      if (removeHoveredAction_ != null) {
        removeHoveredAction_ = null;
        HideHoverHighlighting();
        HidePreviewPopup();
      }
    });

    ignoreNextHoverEvent_ = false;
  }

  private void HideHoverHighlighting() {
    if (hoveredElement_ == null) {
      return;
    }

    RaiseElementHighlightingEvent(null, null, HighlighingType.Hovered,
                                  HighlightingEventAction.ReplaceHighlighting);

    hoverHighlighter_.Clear();
    hoveredElement_ = null;
  }

  private void IRDocument_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
    HidePreviewPopup();
    selectingText_ = false;

    if (ignoreNextScrollEvent_) {
      ignoreNextScrollEvent_ = false;
      //e.Handled = true;
    }
  }

  private void IRDocument_PreviewMouseMove(object sender, MouseEventArgs e) {
    ForceCursor = true;
    Cursor = Cursors.Arrow;
    margin_.MouseMoved(e);

    // Don't send event to overlays that may extend under the scrollbar.
    if (!disableOverlayEvents_ &&
        (docVerticalScrollBar_ == null || !docVerticalScrollBar_.IsMouseOver) &&
        e.LeftButton == MouseButtonState.Released &&
        e.RightButton == MouseButtonState.Released) {
      overlayRenderer_.MouseMoved(e);
    }
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

  private async Task ComputeElementListsAsync() {
    // Compute the element lists on another thread, for large functions
    // it can be slow would block showing of any text before it's done.
    await Task.Run(() => ComputeElementLists());
  }

  private async Task LateLoadSectionSetup(ParsedIRTextSection parsedSection,
                                          bool isSourceCode = false) {
#if DEBUG
    Trace.TraceInformation(
      $"Document {ObjectTracker.Track(this)}: Complete setup for {parsedSection}");
#endif

    // Folding uses the basic block boundaries.
    actionUndoStack_.Clear();
    SetupBlockHighlighter();
    SetupBlockFolding();
    ClearElementOverlays();

    CreateRightMarkerMargin();
    MarkLoopBlocks();
    DuringSectionLoading = false;
    IsLoaded = true;

    //? TODO: Check if other sections have marked elements and try to mark same ones
    //!  - session can be queried
    //!  - load func and deserialize state object
    //!    - for each marker/bookmark, use FindEquivalentValue
    //!    - maybe set "no saving" flag for these copied markers
    //var other = Session.GetNextSection(Section);
    //if (other != null)
    //    CloneOtherSectionAnnotations(other);

    // Do compiler-specific document work.
    if (!isSourceCode) {
      await Session.CompilerInfo.HandleLoadedSection(this, Function, Section);
    }

    UpdateHighlighting();
  }

  //? TODO: Check if other sections have marked elements and try to mark same ones
  private async Task CloneOtherSectionAnnotations(IRTextSection otherSection) {
    var parsedSection = await Session.LoadAndParseSection(otherSection);
    if (parsedSection == null)
      return;

    object data = Session.LoadDocumentState(otherSection);
    var refFinder = CreateReferenceFinder();

    if (data != null) {
      var state = StateSerializer.Deserialize<IRDocumentHostState>(data, parsedSection.Function);
      var markerState = state.DocumentState.MarkedHighlighter;

      if (markerState != null && markerState.Groups != null) {
        foreach (var groupState in markerState.Groups) {
          var group = new HighlightedElementGroup(groupState.Style);

          foreach (var item in groupState.Elements) {
            if (item.Value == null) {
              continue;
            }

            // Search for equivalent value in current section.
            var equivElement = refFinder.FindEquivalentValue(item);

            if (equivElement != null) {
              group.Add(equivElement);
            }
          }

          if (!group.IsEmpty()) {
            markedHighlighter_.Add(new HighlightedSegmentGroup(group, false));
          }
        }
      }
    }
  }

  private void SetupBlockHighlighter() {
    if (!settings_.ShowBlockSeparatorLine) {
      return;
    }

    // Setup highlighting of block background.
    blockHighlighter_.Clear();

    foreach (var element in blockElements_) {
      blockHighlighter_.Add(element);
    }
  }

  private void ComputeElementLists() {
    // In case the function couldn't be parsed, bail
    // after creating the elements, this should prevent further issues.
    int blocks = Function != null ? Function.Blocks.Count + 1 : 1;
    blockElements_ = new List<IRElement>(blocks);
    tupleElements_ = new List<IRElement>(blocks * 4);
    operandElements_ = new List<IRElement>(blocks * 12);
    selectedElements_ = new HashSet<IRElement>();

    if (Function == null) { // For source code documents.
      return;
    }

    // Add the function parameters.
    foreach (var param in Function.Parameters) {
      operandElements_.Add(param);
    }

    // Add the elements from the entire function.
    foreach (var block in Function.Blocks) {
      blockElements_.Add(block);

      if (block.Label != null) {
        operandElements_.Add(block.Label);
      }

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
  }

  private void Margin__BookmarkChanged(object sender, Bookmark bookmark) {
    RaiseBookmarkChangedEvent(bookmark);
  }

  private void Margin__BookmarkRemoved(object sender, Bookmark bookmark) {
    RemoveBookmark(bookmark);
  }

  private void MarkBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GetSelectedElement() is var element) {
      element = element.ParentBlock;
      var style = GetMarkerStyleForCommand(e);
      MarkBlock(element, style);
      MirrorAction(DocumentActionKind.MarkBlock, element, style);
    }
  }

  private void MarkDefinitionBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (selectedElements_.Count == 1) {
      var defOps = GetSelectedElementDefinitions();

      foreach (var defOp in defOps) {
        var element = defOp.ParentBlock;
        var style = GetMarkerStyleForCommand(e);
        MarkBlock(element, style);
        MirrorAction(DocumentActionKind.MarkElement, defOp, style);
      }
    }
  }

  private void MarkDefinitionExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (selectedElements_.Count == 1) {
      var defOps = GetSelectedElementDefinitions();

      foreach (var defOp in defOps) {
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
    if (GetSelectedElement() is var element) {
      var style = GetPairMarkerStyleForCommand(e);
      MarkElement(element, style);
      MirrorAction(DocumentActionKind.MarkElement, element, style);
    }
  }

  private void MarkIconExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GetSelectedElement() is var element) {
      if (e != null && e.Parameter is SelectedIconEventArgs iconArgs) {
        var icon = IconDrawing.FromIconResource(iconArgs.SelectedIconName);
        var overlay = AddIconElementOverlay(element, icon, DefaultLineHeight, DefaultLineHeight);
        overlay.Background = Background;
        overlay.ShowBorderOnMouseOverOnly = true;
        overlay.ShowBackgroundOnMouseOverOnly = true;
        overlay.IsLabelPinned = true;
        // MirrorAction(DocumentActionKind.MarkIcon, element, iconArgs);
      }
    }
  }

  private void MarkLoopBlocks() {
    var dummyGraph = new Graph(GraphKind.FlowGraph);
    var graphStyle = new FlowGraphStyleProvider(dummyGraph, App.Settings.FlowGraphSettings);
    var loopGroups = new Dictionary<HighlightingStyle, HighlightedElementGroup>();

    foreach (var block in Function.Blocks) {
      var loopTag = block.GetTag<LoopBlockTag>();

      if (loopTag != null) {
        var style = graphStyle.GetBlockNodeStyle(block);

        if (!loopGroups.TryGetValue(style, out var group)) {
          group = new HighlightedElementGroup(style);
          loopGroups[style] = group;
        }

        group.Add(block);
      }
    }

    foreach (var group in loopGroups.Values) {
      margin_.AddBlock(group, false);
    }
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

    var refFinder = CreateReferenceFinder();
    var operandRefs = refFinder.FindAllReferences(op);
    var markedInstrs = new HashSet<InstructionIR>(operandRefs.Count);

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
      Session.ShowAllReferences(op, this);
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
    var refFinder = CreateReferenceFinder();
    var useList = refFinder.FindAllUses(defOp);

    HighlightUsers(defOp, useList, markedHighlighter_, style,
                   HighlightingEventAction.AppendHighlighting);

    UpdateHighlighting();
    Session.ShowSSAUses(defOp, this);
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
           bookmarks_.Version != info.LastBookmarkVersion ||
           overlayRenderer_.Version != info.LastOverlayVersion;
  }

  private void SaveHighlighterVersion(MarkerMarginVersionInfo info) {
    info.LastMarkedVersion = markedHighlighter_.Version;
    info.LastSelectedVersion = selectedHighlighter_.Version;
    info.LastHoveredVersion = hoverHighlighter_.Version;
    info.LastDiffVersion = diffHighlighter_.Version;
    info.LastBlockMarginVersion = margin_.Version;
    info.LastBookmarkVersion = bookmarks_.Version;
    info.LastOverlayVersion = overlayRenderer_.Version;
    info.NeedsRedrawing = true;
  }

  //? TODO: Extract all right margin code into own class
  private void PopulateMarkerBar() {
    // Try again to create the margin bar, the scrollbar may not have
    // been visible when the document view was loaded.
    if (markerMargin_ == null) {
      CreateRightMarkerMargin();

      if (markerMargin_ == null) {
        //? TODO: Fix
        //Trace.WriteLine("NO MARGIN");
        //Trace.WriteLine(Environment.StackTrace);
        return;
      }
    }

    if (!HighlighterVersionChanged(highlighterVersion_)) {
      return;
    }

    markerMargingElements_ = new List<MarkerBarElement>(128);
    int arrowButtonHeight = 16;
    int startY = arrowButtonHeight;

    // Delay the update to ensure the markerMargin has layout updated.
    // Since the code runs later on the UI thread, it can end up interleaved
    // with code that changes the OverlayRenderer data structs. used by the
    // PopulateMarker* functions, abort the update in that case.
    var updateTask = markerBarUpdateTask_.CancelCurrentAndCreateTask();

    Dispatcher.BeginInvoke(() => {
      if (updateTask.IsCanceled) {
        return;
      }

      markerBarUpdateLock_.EnterWriteLock();
      double width = markerMargin_.ActualWidth;
      double height = markerMargin_.ActualHeight;
      double availableHeight = height - arrowButtonHeight * 2;
      int lines = Document.LineCount;
      double dotSize = Math.Max(2, availableHeight / lines);
      double dotWidth = width / 3;
      availableHeight -= dotSize;

      PopulateMarkerBarForHighlighter(markedHighlighter_, startY, width, availableHeight, dotSize);
      PopulateMarkerBarForHighlighter(selectedHighlighter_, startY, width, availableHeight, dotSize);
      PopulateMarkerBarForHighlighter(hoverHighlighter_, startY, width, availableHeight, dotSize);
      PopulateMarkerBarForElementOverlays(startY, width, availableHeight, dotSize);
      PopulateMarkerBarForDiffs(startY, width, availableHeight);
      PopulateMarkerBarForBlocks(startY, width, availableHeight);

      margin_.ForEachBookmark(bookmark => {
        PopulateMarkerBarForBookmark(bookmark, startY, width, height, dotWidth, dotSize);
      });

      // Sort so that searching for elements can be speed up.
      markerMargingElements_.Sort((a, b) => (int)(a.Visual.Top - b.Visual.Top));
      SaveHighlighterVersion(highlighterVersion_);
      RenderMarkerBar();

      markerBarUpdateLock_.ExitWriteLock();
      updateTask.Complete();
    }, DispatcherPriority.Background);
  }

  private void PopulateMarkerBarForBookmark(Bookmark bookmark, int startY, double width, double height,
                                            double dotWidth, double dotHeight) {
    double y = (double)bookmark.Element.TextLocation.Line / LineCount * height;
    var elementVisual = new Rect(width / 3, startY + y, dotWidth, dotHeight);
    var style = bookmark.Style;

    if (bookmark.Style != null) {
      var brush = style.BackColor as SolidColorBrush;
      var color = ColorUtils.AdjustSaturation(brush.Color);
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

      if (brush.Color == Colors.Transparent) {
        return;
      }

      var color = ColorUtils.AdjustSaturation(brush.Color);
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
      var color = ColorUtils.AdjustSaturation(brush.Color, 1.5f);

      if (!blockGroup.SavesStateToFile) {
        continue;
      }

      foreach (var segment in blockGroup.Segments) {
        int startLine = Document.GetLineByOffset(segment.StartOffset).LineNumber;
        int endLine = Document.GetLineByOffset(segment.EndOffset - 1).LineNumber;
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

  private void PopulateMarkerBarForDiffs(int startY, double width, double height) {
    if (duringDiffModeSetup_) {
      return;
    }

    int lastLine = -1;
    int lineSpan = 1;
    var lastColor = Colors.Transparent;

    diffHighlighter_.ForEachDiffSegment((segment, color) => {
      // Combine the marking of multiple diffs of the same type
      // to speed up rendering.
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
    color = ColorUtils.AdjustSaturation(color, 1.5f);
    var elementVisual = new Rect(2 * width / 3, startY + y, width / 3, lineHeight);
    var barStyle = new HighlightingStyle(color);

    markerMargingElements_.Add(new MarkerBarElement {
      Element = null,
      Visual = elementVisual,
      Style = barStyle,
      HandlesInput = false
    });
  }

  private void PopulateMarkerBarForElementOverlays(int startY, double width,
                                                   double height, double dotSize) {
    overlayRenderer_.ForEachElementOverlay((element, overlaySegment) => {
      // Only consider elements with overlays that should appear on the marker bar.
      bool showOnMarkerBar = false;

      foreach (var overlay in overlaySegment.Overlays) {
        if (overlay.ShowOnMarkerBar) {
          showOnMarkerBar = true;
          break;
        }
      }

      if (!showOnMarkerBar) {
        return;
      }

      double y = (double)element.TextLocation.Line / LineCount * height;
      var brush = Brushes.Blue;
      var color = ColorUtils.AdjustSaturation(brush.Color);
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

  private void PreviousBookmarkExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (!JumpToBookmark(bookmarks_.GetPrevious())) {
      JumpToBookmark(bookmarks_.JumpToLastBookmark());
    }
  }

  private void RaiseBlockSelectedEvent(IRElement element) {
    BlockSelected?.Invoke(this, new IRElementEventArgs {Element = element, Document = this});
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

  private void RaiseElementHighlightingEvent(IRElement element, HighlightedElementGroup group,
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

  private void SetupCommands() {
    AddCommand(DocumentCommand.GoToDefinition, GoToDefinitionExecuted);
    AddCommand(DocumentCommand.GoToDefinitionSkipCopies, GoToDefinitionSkipCopiesExecuted);
    AddCommand(DocumentCommand.PreviewDefinition, PreviewDefinitionExecuted);
    AddCommand(DocumentCommand.Mark, MarkExecuted);
    AddCommand(DocumentCommand.MarkIcon, MarkIconExecuted);
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

    PreviewKeyDown += IRDocument_PreviewKeyDown;
    MouseDown += IRDocument_MouseDown;
    PreviewMouseLeftButtonDown += IRDocument_PreviewMouseLeftButtonDown;
    PreviewMouseRightButtonDown += IRDocument_PreviewMouseRightButtonDown;
    PreviewMouseLeftButtonUp += IRDocument_PreviewMouseLeftButtonUp;
    PreviewMouseHover += IRDocument_PreviewMouseHover;
    PreviewMouseHoverStopped += IRDocument_PreviewMouseHoverStopped;
    PreviewMouseDoubleClick += IRDocument_PreviewMouseDoubleClick;
    PreviewMouseMove += IRDocument_PreviewMouseMove;
    MouseLeave += IRDocument_MouseLeave;
    Drop += IRDocument_Drop;
    DragOver += IRDocument_DragOver;
    GiveFeedback += IRDocument_GiveFeedback;
    TextArea.Caret.PositionChanged += Caret_PositionChanged;
    margin_.BookmarkRemoved += Margin__BookmarkRemoved;
    margin_.BookmarkChanged += Margin__BookmarkChanged;
    eventSetupDone_ = true;
    AllowDrop = true; // Enable drag-and-drop handilng.
  }

  private void IRDocument_MouseLeave(object sender, MouseEventArgs e) {
    HideTemporaryUI();
    overlayRenderer_.MouseLeave();
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
    // Highlighting of current line.
    if (lineHighlighter_ != null) {
      TextArea.TextView.BackgroundRenderers.Remove(lineHighlighter_);
      lineHighlighter_ = null;
    }

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

      if (Function != null) {
        SetupBlockHighlighter();
      }
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
      TextArea.TextView.Layers.Remove(overlayRenderer_);
    }

    // Create the overlay and place it on top of the text.
    overlayRenderer_ ??= new OverlayRenderer(markedHighlighter_);
    overlayRenderer_.TextFont = new Typeface(FontFamily, FontStyle,
                                             FontWeight, FontStretch);
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

        // AvalonEdit text segments cannot be reused, even when removed from the
        // segment collection, since they are left with internal fields referencing
        // the old tree data struct and adding them to another collection would fail.
        // Make a copy of each segment as a workaround.
        diffSegments_ = diffSegments_.ConvertAll(segment => new DiffTextSegment(segment));
        StartDiffSegmentAdding();
        AddDiffTextSegments(diffSegments_);
        AllDiffSegmentsAdded();
      }
    }

    if (settings_.HighlightCurrentLine) {
      lineHighlighter_ = new CurrentLineHighlighter(this, settings_.CurrentLineBorderColor);
      TextArea.TextView.BackgroundRenderers.Add(lineHighlighter_);
    }
  }

  private HighlightingStyle GetRemarkLineStyle(Remark remark, bool hasContextFilter, bool isSelected = false) {
    //? TODO: Caching of the style
    if (hasContextFilter) {
      // Use the background of the remark with the same bold pen for all kinds.
      var backColor = remark.Category.MarkColor;

      if (backColor == Colors.Black || backColor == Colors.Transparent) {
        backColor = Colors.LightGray;
      }

      var borderColor = Colors.Black;
      double borderWeight = Math.Max(2, remark.Category.TextMarkBorderWeight);
      return new HighlightingStyle(backColor, ColorPens.GetPen(borderColor, borderWeight));
    }

    return new HighlightingStyle(remark.Category.MarkColor,
                                 ColorPens.GetPen(remark.Category.TextMarkBorderColor,
                                                  remark.Category.TextMarkBorderWeight));
  }

  private HighlightingStyle GetRemarkBookmarkStyle(Remark remark, bool hasContextFilter) {
    //? TODO: Caching
    return new HighlightingStyle(remark.Category.MarkColor);
  }

  private void AddMarginRemarks(List<RemarkLineGroup> markerRemarksGroups, bool hasContextFilter) {
    foreach (var remarkGroup in markerRemarksGroups) {
      bool groupHandled = false;

      foreach (var remark in remarkGroup.Remarks) {
        foreach (var element in remark.ReferencedElements) {
          // Add a single marker for all remarks mapping to the same line (group).
          if (remark.Category.AddLeftMarginMark && !groupHandled) {
            var style = GetRemarkBookmarkStyle(remarkGroup.LeaderRemark, hasContextFilter);
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

  private void AddDocumentRemarks(List<Remark> allRemarks, bool hasContextFilter) {
    var markedElements = new HashSet<Tuple<IRElement, RemarkKind>>(allRemarks.Count);

    foreach (var remark in allRemarks) {
      foreach (var element in remark.ReferencedElements) {
        if (remark.Category.AddTextMark || hasContextFilter) {
          // Mark each element only once with the same kind of remark.
          var elementKindPair = new Tuple<IRElement, RemarkKind>(element, remark.Kind);

          if (!markedElements.Contains(elementKindPair)) {
            var style = GetRemarkLineStyle(remark, hasContextFilter);
            var group = new HighlightedElementGroup(element, style);
            remarkHighlighter_.Add(group);
            markedElements.Add(elementKindPair);
          }
        }
      }
    }
  }

  private async Task<bool> ShowDefinitionPreview(IRElement element, bool alwaysShow = false) {
    if (settings_ is not DocumentSettings docSettings) {
      return false;
    }

    IRElement target = null;
    bool isCallTarget = false;
    bool show = false;

    if (!alwaysShow) {
      if (!docSettings.ShowPreviewPopup) {
        HidePreviewPopup();
        return false;
      }
    }

    if (element is OperandIR op) {
      if (op.IsLabelAddress) {
        target = op.BlockLabelValue;
        show = target != null && (alwaysShow || IsElementOutsideView(target));
      }
      else if (op.ParentInstruction != null) {
        if (op.Equals(Session.CompilerInfo.IR.GetCallTarget(op.ParentInstruction))) {
          target = op;
          isCallTarget = true;
          show = true;
        }
      }

      if (target == null) {
        var refFinder = CreateReferenceFinder();
        target = refFinder.FindSingleDefinition(op);
        show = target != null && (alwaysShow || IsElementOutsideView(target));
      }
    }

    // Show the preview only if outside the current view.
    if (show) {
      await ShowPreviewPopup(target, isCallTarget, alwaysShow);
      return true;
    }

    return false;
  }

  private bool IsCallTargetElement(IRElement element) {
    if (element is OperandIR op &&
        op.ParentInstruction != null) {
      return op.Equals(Session.CompilerInfo.IR.GetCallTarget(op.ParentInstruction));
    }

    return false;
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
    Session.ShowAllReferences(op, this);
  }

  private async Task ShowPreviewPopup(IRElement element, bool isCallTarget = false, bool alwaysShow = false) {
    if (element == null || preparingPreviewPopup_) {
      return; // Valid case for search results.
    }

    // Close current popup.
    if (previewPopup_ != null) {
      if (previewPopup_.PreviewedElement == element) {
        return; // Already showing preview for this element.
      }

      HidePreviewPopup();
    }

    if (ignoreNextPreviewElement_ == element) {
      return;
    }

    // Try to create a popup for the element.
    var position = Mouse.GetPosition(TextArea.TextView).AdjustForMouseCursor();
    preparingPreviewPopup_ = true; // Prevent another popup to be created due to await.

    if (isCallTarget) {
      previewPopup_ = await CreateCallTargetPreviewPopup(element, alwaysShow, position);
    }
    else {
      previewPopup_ = await CreateElementPreviewPopup(element, position);
    }

    preparingPreviewPopup_ = false;

    if (previewPopup_ != null) {
      previewPopup_.PopupDetached += Popup_PopupDetached;
      previewPopup_.ShowPopup();

      if (alwaysShow) {
        // Keep open when triggered manually from UI or shortcut.
        previewPopup_.DetachPopup();
      }
    }
  }

  private async Task<IRDocumentPopup> CreateElementPreviewPopup(IRElement element, Point position) {
    return await IRDocumentPopup.CreateNew(this, element, position,
                                           this, App.Settings.GetElementPreviewPopupSettings(ToolPanelKind.Other));
  }

  private async Task<IRDocumentPopup> CreateCallTargetPreviewPopup(IRElement element, bool alwaysShow, Point position) {
    // Try to find a function definition for the call.
    var targetSection = DocumentUtils.FindCallTargetSection(element, Section, Session);

    if (targetSection == null) {
      if (alwaysShow) {
        Utils.ShowWarningMessageBox($"Couldn't find call target in opened document:\n{element.Name}", this);
      }

      return null;
    }

    var result = await Session.LoadAndParseSection(targetSection);

    if (result == null)
      return null;

    return await IRDocumentPopup.CreateNew(result, position, this, Session,
                                           App.Settings.PreviewPopupSettings,
                                           $"Function: {element.Name}");
  }

  private void Popup_PopupDetached(object sender, EventArgs e) {
    var popup = (IRDocumentPopup)sender;

    if (popup == previewPopup_) {
      previewPopup_ = null; // Prevent automatic closing.
    }
  }

  private void ShowUsesExecuted(object sender, ExecutedRoutedEventArgs e) {
    var defOp = GetSelectedElementDefinition();

    if (defOp != null) {
      ShowUses(defOp);
      MirrorAction(DocumentActionKind.ShowUses, defOp);
    }
  }

  private void ShowUses(OperandIR defOp) {
    Session.ShowSSAUses(defOp, this);
  }

  private void IRDocument_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
    if (updateSuspended_) {
      return;
    }

    var position = e.GetPosition(TextArea.TextView);

    // Ignore click outside the text view, such as the right marker bar.
    if (position.X <= 0 ||
        position.X >= TextArea.TextView.ActualWidth ||
        position.Y >= TextArea.TextView.ActualHeight) {
      return;
    }

    // Ignore click on a bookmark.
    if (margin_.HasHoveredBookmark()) {
      return;
    }

    Focus();
    HideTemporaryUI();

    // Check if there is any overlay being clicked
    // and don't propagate event to elements if it is.
    if (overlayRenderer_.MouseClick(e)) {
      e.Handled = true;
      return;
    }

    var element = FindPointedElement(position, out int textOffset);
    SelectElement(element, true, true, textOffset);
    selectingText_ = true;

    //? TODO: This would prevent selection of text from working,
    //? but allowing it also sometimes selects a letter of the element...
    // e.Handled = element != null;
  }

  private void SetupElementOverlayEvents(IElementOverlay overlay) {
    overlay.OnKeyPress += ElementOverlay_OnKeyPress;
    overlay.OnClick += ElementOverlay_OnClick;
  }

  private void ElementOverlay_OnClick(object sender, MouseEventArgs e) {
    var overlay = (IElementOverlay)sender;
    SelectElement(overlay.Element);
  }

  private void ElementOverlay_OnKeyPress(object sender, KeyEventArgs e) {
    if (e.Key == Key.Delete) {
      overlayRenderer_.RemoveElementOverlay((IElementOverlay)sender);
    }
  }

  private void UpdateHighlighting() {
    if (updateSuspended_ || !IsLoaded) {
      return;
    }

    PopulateMarkerBar();
    TextArea.TextView.Redraw();
  }

  private void UpdateMargin() {
    if (updateSuspended_) {
      return;
    }

    margin_.InvalidateVisual();
  }

  public void Redraw() {
    UpdateHighlighting();
    UpdateMargin();
  }

  private class MarkerMarginVersionInfo {
    public int LastBlockMarginVersion;
    public int LastBookmarkVersion;
    public int LastDiffVersion;
    public int LastHoveredVersion;
    public int LastMarkedVersion;
    public int LastSelectedVersion;
    public int LastOverlayVersion;
    public bool NeedsRedrawing;
  }

  private class MarkerBarElement {
    public IRElement Element { get; set; }
    public HighlightingStyle Style { get; set; }
    public Rect Visual { get; set; }
    public bool HandlesInput { get; set; }
  }
}