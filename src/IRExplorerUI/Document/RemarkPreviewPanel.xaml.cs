using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Controls;

namespace IRExplorerUI.Document {
    public class RemarkEx {
        private static readonly FontFamily RemarkFont = new FontFamily("Consolas");

        public static SolidColorBrush GetRemarkBackground(Remark remark) {
            return Utils.EstimateBrightness(remark.Category.MarkColor) < 200 ?
                   ColorBrushes.GetBrush(Utils.ChangeColorLuminisity(remark.Category.MarkColor, 1.75)) :
                   ColorBrushes.GetBrush(Utils.ChangeColorLuminisity(remark.Category.MarkColor, 1.2));
        }

        public static TextBlock FormatRemarkTextLine(Remark remark, List<RemarkTextHighlighting> highlightingList = null) {
            int lineOffset = remark.RemarkLocation.Offset;
            int elementOffset = remark.OutputElements[0].TextLocation.Offset;
            int elementLength = remark.OutputElements[0].TextLength;
            int elementLineOffset = elementOffset - lineOffset;
            int afterElementOffset = elementLineOffset + elementLength;

            var textLine = remark.RemarkLine;
            var textBlock = new TextBlock();
            textBlock.FontFamily = RemarkFont;
            textBlock.FontWeight = FontWeights.Normal;
            textBlock.Foreground = Brushes.Black;

            if (elementLineOffset > 0) {
                // Append text found before the IR element.
                var text = textLine.Substring(0, elementLineOffset);
                AppendExtraOutputTextRun(text, textBlock, highlightingList);
            }

            if (elementLength > 0) {
                // Append the IR element text.
                var text = textLine.Substring(elementLineOffset, elementLength);
                textBlock.Inlines.Add(new Run(text) {
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.DarkBlue
                });
            }

            if (afterElementOffset < textLine.Length) {
                // Append text following the IR element.
                var text = textLine.Substring(afterElementOffset, textLine.Length - afterElementOffset);
                AppendExtraOutputTextRun(text, textBlock, highlightingList);
            }

            return textBlock;
        }

        public static TextBlock FormatExtraTextLine(string text, List<RemarkTextHighlighting> highlightingList = null) {
            var textBlock = new TextBlock();
            textBlock.FontFamily = RemarkFont;
            textBlock.Foreground = Brushes.Black;
            textBlock.FontWeight = FontWeights.Normal;
            AppendExtraOutputTextRun(text, textBlock, highlightingList);
            return textBlock;
        }

        private static void AppendExtraOutputTextRun(string text, TextBlock textBlock,
                                                     List<RemarkTextHighlighting> highlightingList = null) {
            if (highlightingList == null || highlightingList.Count == 0) {
                textBlock.Inlines.Add(text);
                return;
            }

            int index = 0;

            while (index < text.Length) {
                // Try to find a highlighting match in the order they are in the list,
                // as defined by the user in the settings.
                RemarkTextHighlighting matchingQuery = null;
                TextSearchResult? matchResult = null;

                foreach (var query in highlightingList) {
                    var result = TextSearcher.FirstIndexof(text, query.SearchedText, index, query.SearchKind);

                    if (result.HasValue) {
                        matchResult = result.Value;
                        matchingQuery = query;
                        break;
                    }
                }

                if (matchingQuery != null) {
                    if (index < matchResult.Value.Offset) {
                        textBlock.Inlines.Add(text.Substring(index, matchResult.Value.Offset - index));
                    }

                    textBlock.Inlines.Add(new Run(text.Substring(matchResult.Value.Offset, matchResult.Value.Length)) {
                        Foreground = matchingQuery.HasTextColor ? ColorBrushes.GetBrush(matchingQuery.TextColor) : Brushes.Black,
                        Background = matchingQuery.HasBackgroundColor ? ColorBrushes.GetBrush(matchingQuery.BackgroundColor) : null,
                        FontStyle = matchingQuery.UseItalicText ? FontStyles.Italic : FontStyles.Normal,
                        FontWeight = matchingQuery.UseBoldText ? FontWeights.Bold : FontWeights.Normal
                    });

                    index = matchResult.Value.Offset + matchResult.Value.Length;
                }
                else {
                    break;
                }
            }

            if (index < text.Length) {
                // Append any remaining text at the end.
                textBlock.Inlines.Add(text.Substring(index, text.Length - index));
            }
        }

        public Remark Remark { get; set; }
        public TextBlock Text => FormatRemarkTextLine(Remark);
        public Brush Background => GetRemarkBackground(Remark);

        private string FormatRemarkText() {
            if (Remark.Kind == RemarkKind.None) {
                return Remark.RemarkText;
            }

            return $"{Remark.Section.Number} | {Remark.RemarkText}";
        }
    }

    public class ListRemarkEx : RemarkEx {
        public ListRemarkEx(Remark remark, string sectionName, bool inCurrentSection) {
            Remark = remark;
            InCurrentSection = inCurrentSection;
            SectionName = sectionName;
        }

        public bool InCurrentSection { get; set; }
        public bool HasContext => Remark.Context != null;
        public string SectionName { get; set; }

        public bool IsOptimization => Remark.Kind == RemarkKind.Optimization;
        public bool IsAnalysis => Remark.Kind == RemarkKind.Analysis;
        public bool HasCustomBackground => Remark.Category.MarkColor != Colors.Black;
        public string Description => SectionName;
    }

    public class RemarkSettingsEx : BindableObject {
        private bool hasOptimizationRemarks_;
        private bool hasAnalysisRemarks_;
        private bool hasDefaultRemarks_;
        private bool hasVerboseRemarks_;
        private bool hasTraceRemarks_;

        public RemarkSettings Settings { get; set; }

        public RemarkSettingsEx(RemarkSettings settings) {
            Settings = settings;
        }

        public bool HasOptimizationRemarks {
            get => hasOptimizationRemarks_;
            set => SetAndNotify(ref hasOptimizationRemarks_, value);
        }

        public bool HasAnalysisRemarks {
            get => hasAnalysisRemarks_;
            set => SetAndNotify(ref hasAnalysisRemarks_, value);
        }

        public bool HasDefaultRemarks {
            get => hasDefaultRemarks_;
            set => SetAndNotify(ref hasDefaultRemarks_, value);
        }

        public bool HasVerboseRemarks {
            get => hasVerboseRemarks_;
            set => SetAndNotify(ref hasVerboseRemarks_, value);
        }

        public bool HasTraceRemarks {
            get => hasTraceRemarks_;
            set => SetAndNotify(ref hasTraceRemarks_, value);
        }
    }

    public class RemarkContextChangedEventArgs : EventArgs {
        public RemarkContextChangedEventArgs(RemarkContext context, List<Remark> remarks) {
            Context = context;
            Remarks = remarks;
        }

        public RemarkContext Context { get; set; }
        public List<Remark> Remarks { get; set; }
    }

    public partial class RemarkPreviewPanel : DraggablePopup, INotifyPropertyChanged {
        private const double RemarkListTop = 48;
        private const double RemarkPreviewWidth = 600;
        private const double RemarkPreviewHeight = 300;
        private const double RemarkListItemHeight = 20;
        private const double MaxRemarkListItems = 10;
        private const double MinRemarkListItems = 3;
        private const double ColorButtonLeft = 175;

        private IRDocumentHost parentDocument_;
        private RemarkContext activeRemarkContext_;
        private IRElement element_;
        private RemarkSettingsEx remarkFilter_;
        private bool showPreview_;
        private bool filterActiveContextRemarks_;
        private Popup colorPopup_;
        private Remark selectedRemark_;
        private bool contextSearchPanelVisible_;
        private SearchInfo contextSearchInfo_;
        private List<TreeViewItem> contextSearchResults_;

        public RemarkPreviewPanel() {
            InitializeComponent();
            PanelResizeGrip.ResizedControl = this;
            DataContext = this; // Used for auto-resizing with ShowPreview.

            //? TODO: Add options
            filterActiveContextRemarks_ = true;

            // Setup search panel for context tree.
            ContextSearchPanel.UseAutoComplete = false;
            RemarkTextView.UseAutoComplete = false;
            ContextSearchPanel.SearchChanged += ContextSearchPanel_SearchChanged;
            ContextSearchPanel.CloseSearchPanel += ContextSearchPanel_CloseSearchPanel;
            ContextSearchPanel.NavigateToNextResult += ContextSearchPanel_NavigateToResult;
            ContextSearchPanel.NavigateToPreviousResult += ContextSearchPanel_NavigateToResult;
        }

        public bool ShowPreview {
            get => showPreview_;
            set {
                if (showPreview_ != value) {
                    showPreview_ = value;
                    NotifyPropertyChanged(nameof(ShowPreview));
                    UpdateSize();
                }
            }
        }

        public IRElement Element {
            get => element_;
            set {
                if (element_ == value) {
                    return;
                }

                element_ = value;
                UpdateRemarkList();
            }
        }

        public bool FilterActiveContextRemarks {
            get => filterActiveContextRemarks_;
            set {
                if (filterActiveContextRemarks_ != value) {
                    if (value && selectedRemark_ != null) {
                        NotifyRemarkContextChanged(selectedRemark_.Context, null); // Recomputes the list.
                    }
                    else {
                        NotifyRemarkContextChanged(null, null); // Disables filtering.
                    }

                    filterActiveContextRemarks_ = value;
                }
            }
        }

        public bool ShowSearchPanel {
            get {
                if (IsContextTreeVisible()) {
                    return contextSearchPanelVisible_;
                }
                else {
                    return RemarkTextView.SearchPanelVisible;
                }
            }
            set {
                if (IsContextTreeVisible()) {
                    if (value != contextSearchPanelVisible_) {
                        contextSearchPanelVisible_ = value;
                        ContextSearchPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;

                        if (value) {
                            ContextSearchPanel.Show();
                        }
                        else {
                            ContextSearchPanel.Hide();
                        }

                        NotifyPropertyChanged(nameof(ShowSearchPanel));
                    }
                }
                else {
                    if (value != RemarkTextView.SearchPanelVisible) {
                        RemarkTextView.SearchPanelVisible = value;
                        NotifyPropertyChanged(nameof(ShowSearchPanel));
                    }
                }
            }
        }

        public FunctionIR Function { get; set; }
        public IRTextSection Section { get; set; }
        public ISession Session { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<RemarkContextChangedEventArgs> RemarkContextChanged;
        public event EventHandler<Remark> RemarkChanged;

        private void UpdateRemarkList() {
            RemarkList.ItemsSource = null;
            RemarkContextTree.Items.Clear();

            var remarkTag = element_.GetTag<RemarkTag>();

            if (remarkTag == null) {
                return;
            }

            var list = new List<Remark>();
            var listEx = new List<ListRemarkEx>();
            ListRemarkEx firstSectionRemark = null;

            foreach (var remark in remarkTag.Remarks) {
                AccountForRemarkKind(remark);

                if (parentDocument_.IsAcceptedContextRemark(remark, Section, remarkFilter_.Settings)) {
                    var remarkEx = AppendAcceptedRemark(listEx, remark);

                    // Find first remark in current section.
                    if (remark.Section == Section && firstSectionRemark == null) {
                        firstSectionRemark = remarkEx;
                    }
                }
            }

            RemarkList.ItemsSource = listEx;

            // Scroll down to current section.
            if (firstSectionRemark != null) {
                RemarkList.ScrollIntoView(firstSectionRemark);
            }

        }

        private (TreeViewItem, int) BuildContextRemarkTreeView(RemarkContext context,
                                               Dictionary<RemarkContext, TreeViewItem> treeNodeMap,
                                               List<string> outputTextLines) {
            if (!treeNodeMap.TryGetValue(context, out var treeNode)) {
                treeNode = new TreeViewItem() {
                    Header = context.Name,
                    Foreground = ColorBrushes.GetBrush(Colors.DarkBlue),
                    FontWeight = FontWeights.Bold,
                    ItemContainerStyle = Application.Current.FindResource("RemarkTreeViewItemStyle") as Style
                };

                treeNodeMap[context] = treeNode;
            }

            // Combine the remarks and child contexts into one list and sort it by start line.
            // This then allows adding the non-remark text found between remarks, but not part
            // part of a child context (they would be added too when processing the child context,
            // ending up with duplicate text lines).
            var inputItems = new List<Tuple<object, int, int>>(context.Remarks.Count + context.Children.Count);

            foreach (var remark in context.Remarks) {
                inputItems.Add(new Tuple<object, int, int>(remark, remark.RemarkLocation.Line,
                                                           remark.RemarkLocation.Line));
            }

            foreach (var child in context.Children) {
                inputItems.Add(new Tuple<object, int, int>(child, child.StartLine, child.EndLine));
            }

            inputItems.Sort((a, b) => a.Item2 - b.Item2);

            // Append all remarks in the context to the result list.
            var items = new List<Tuple<TreeViewItem, int>>();
            var highlightingList = Session.CompilerInfo.RemarkProvider.RemarkTextHighlighting;
            Tuple<object, int, int> prevRemark = null;

            foreach (var item in inputItems) {
                var line = item.Item2; // Start line.

                if (prevRemark != null) {
                    // Add text between consecutive remarks or the end
                    // of a child context and a remark.
                    var prevLine = prevRemark.Item3 + 1; // End line.
                    ExtractOutputTextInRange(prevLine, line, outputTextLines, highlightingList, items);
                }

                prevRemark = item;

                if (!(item.Item1 is Remark remark)) {
                    continue; // Child context.
                }

                if (parentDocument_.IsAcceptedRemark(remark, Section, remarkFilter_.Settings)) {
                    var tempTreeNode = new TreeViewItem() {
                        Background = RemarkEx.GetRemarkBackground(remark),
                        ItemContainerStyle = Application.Current.FindResource("RemarkTreeViewItemStyle") as Style,
                        ToolTip = remark.RemarkText,
                        Tag = remark
                    };

                    tempTreeNode.Header = RemarkEx.FormatRemarkTextLine(remark, highlightingList);
                    tempTreeNode.Selected += TempTreeNode_Selected;
                    items.Add(new Tuple<TreeViewItem, int>(tempTreeNode, remark.RemarkLocation.Line));
                }
            }

            // Include text preceding the first remark or child context,
            // and following the last remark or child context.
            int firstRegionStart = context.StartLine;
            int firstRegionEnd = context.Remarks.Count > 0 ? context.Remarks[0].RemarkLocation.Line :
                                                             context.EndLine;
            if (context.Children.Count > 0) {
                firstRegionEnd = Math.Min(firstRegionEnd, context.Children[0].StartLine);
            }

            ExtractOutputTextInRange(firstRegionStart, firstRegionEnd,
                                     outputTextLines, highlightingList, items);

            // Recursively add remarks from the child contexts.
            foreach (var child in context.Children) {
                var (childTreeNode, childFirstLine) =
                    BuildContextRemarkTreeView(child, treeNodeMap, outputTextLines);
                items.Add(new Tuple<TreeViewItem, int>(childTreeNode, childFirstLine));
            }

            // Add text found after the last remark in the context.
            int secondRegionStart = Math.Max(firstRegionEnd, context.Remarks.Count > 0 ?
                                                             context.Remarks[^1].RemarkLocation.Line + 1 :
                                                             context.StartLine);
            int secondRegionEnd = context.EndLine;

            if (context.Children.Count > 0) {
                secondRegionStart = Math.Max(secondRegionStart, context.Children[^1].EndLine);
            }

            ExtractOutputTextInRange(secondRegionStart, secondRegionEnd,
                                     outputTextLines, highlightingList, items);

            // Sort by line number, so that remarks and sub-contexts
            // appear in the same order as in the output text.
            items.Sort((a, b) => a.Item2 - b.Item2);
            Tuple<TreeViewItem, int> prevItem = null;

            foreach (var item in items) {
                // Ignore multiple remarks on the same line.
                // If this remark represents an entire instruction and the other one
                // is one of its operands, pick the instruction remark.
                //? TODO: Probably the remark provider shouldn't create the operand remarks at all in this case
                if (prevItem != null && prevItem.Item2 == item.Item2) {
                    if (prevItem.Item1.Tag is Remark prevItemRemark &&
                        item.Item1.Tag is Remark itemRemark) {
                        if (itemRemark.OutputElements[0] is InstructionIR &&
                            !(prevItemRemark.OutputElements[0] is InstructionIR)) {
                            treeNode.Items.Remove(prevItem.Item1);
                            treeNode.Items.Add(item.Item1);
                        }
                    }
                }
                else {
                    treeNode.Items.Add(item.Item1);
                }

                prevItem = item;
            }

            return (treeNode, context.StartLine);
        }

        private void TempTreeNode_Selected(object sender, RoutedEventArgs e) {
            var remark = (RemarkContextTree.SelectedItem as TreeViewItem)?.Tag as Remark;

            if (remark != null) {
                RemarkChanged?.Invoke(this, remark);
            }
        }

        private void ExtractOutputTextInRange(int prevLine, int line, List<string> outputTextLines,
                                              List<RemarkTextHighlighting> highlightingList,
                                              List<Tuple<TreeViewItem, int>> items) {
            for (int k = prevLine; k < line; k++) {
                var lineText = outputTextLines[k];

                //? TODO: Check could be part of the remark provider (ShouldIgnore...)
                //? but a cleaner approach should be having a pass output filter interface,
                //? with Session.GetSectionPassOutputAsync using it, plus a new
                //? GetRawSectionPassOutputAsync so that the remark prov. gets the metadata lines
                if (!lineText.StartsWith("/// irx:")) {
                    var lineTreeNode = new TreeViewItem();
                    lineTreeNode.Header = RemarkEx.FormatExtraTextLine(lineText, highlightingList);
                    lineTreeNode.ToolTip = lineText.Trim();
                    items.Add(new Tuple<TreeViewItem, int>(lineTreeNode, k));
                }
            }
        }

        private TextBlock ApplyTextLineSearch(TextBlock textBlock, SearchInfo searchInfo) {
            List<Inline> newInlines = null;

            foreach (var element in textBlock.Inlines) {
                if (element is Run run) {
                    var searchResults = TextSearcher.AllIndexesOf(run.Text, searchInfo.SearchedText, 0,
                                                                  searchInfo.SearchKind);
                    if (searchResults == null || searchResults.Count == 0) {
                        if (newInlines != null) {
                            // A new text block is being made, append to it.
                            newInlines.Add(element);
                        }
                        continue;
                    }

                    if (newInlines == null) {
                        // Create a new text block that will have the highlighted searched text.
                        // Copy all elements that were skipped until now.
                        newInlines = new List<Inline>(textBlock.Inlines.Count);
                        var prevElement = textBlock.Inlines.FirstInline;

                        while (prevElement != element) {
                            newInlines.Add(prevElement);
                            prevElement = prevElement.NextInline;
                        }
                    }

                    int previousOffset = 0;

                    foreach (var searchResult in searchResults) {
                        if (searchResult.Offset > previousOffset) {
                            // Append text before the searched text.
                            previousOffset = AppendTextInRange(newInlines, run.Text,
                                                               searchResult.Offset, previousOffset);
                        }

                        var searchText = run.Text.Substring(searchResult.Offset, searchResult.Length);
                        previousOffset += searchText.Length;

                        newInlines.Add(new Run(searchText) {
                            Background = ColorBrushes.GetBrush(Colors.Khaki) //? TODO: Customize
                        });
                    }

                    if (previousOffset < run.Text.Length) {
                        AppendTextInRange(newInlines, run.Text,
                                          run.Text.Length, previousOffset);
                    }
                }
                else if (newInlines != null) {
                    // A new text block is being made, append to it.
                    newInlines.Add(element);
                }
            }

            if (newInlines != null) {
                var markedTextBlock = CloneEmptyTextBlock(textBlock);
                markedTextBlock.Inlines.AddRange(newInlines);
                return markedTextBlock;
            }

            return textBlock;
        }

        private int AppendTextInRange(List<Inline> newInlines, string text, int offset, int previousOffset) {
            int distance = offset - previousOffset;
            var rangeText = text.Substring(previousOffset, distance);
            newInlines.Add(new Run(rangeText));
            return previousOffset + distance;
        }

        private TextBlock CloneEmptyTextBlock(TextBlock textBlock) {
            var copyTextBlock = new TextBlock();
            copyTextBlock.FontFamily = textBlock.FontFamily;
            copyTextBlock.Foreground = textBlock.Foreground;
            copyTextBlock.Background = textBlock.Background;
            copyTextBlock.FontWeight = textBlock.FontWeight;
            copyTextBlock.FontSize = textBlock.FontSize;
            copyTextBlock.FontStyle = textBlock.FontStyle;
            return copyTextBlock;
        }

        private void BuildContextRemarkTreeView(RemarkContext rootContext, List<string> outputTextLines) {
            var treeNodeMap = new Dictionary<RemarkContext, TreeViewItem>();
            var (rootTreeNode, _) = BuildContextRemarkTreeView(rootContext, treeNodeMap, outputTextLines);

            RemarkContextTree.Items.Clear();
            RemarkContextTree.Items.Add(rootTreeNode);
            ExpandAllTreeViewNodes(RemarkContextTree);
        }

        private Remark CollectContextTreeRemarks(RemarkContext rootContext, List<Remark> list,
                                                 bool filterDuplicates) {
            Remark firstSectionRemark = null;
            var worklist = new Queue<RemarkContext>();
            worklist.Enqueue(rootContext);

            while (worklist.Count > 0) {
                var context = worklist.Dequeue();

                foreach (var remark in context.Remarks) {
                    if (parentDocument_.IsAcceptedRemark(remark, Section, remarkFilter_.Settings)) {
                        list.Add(remark);

                        // Find first remark in current section.
                        if (remark.Section == Section && firstSectionRemark == null) {
                            firstSectionRemark = remark;
                        }
                    }
                }
            }

            list.Sort((a, b) => a.RemarkLocation.Line - b.RemarkLocation.Line);

            if (filterDuplicates) {
                Remark prevItem = null;

                foreach (var item in list) {
                    if (prevItem != null && prevItem.RemarkLocation.Line == item.RemarkLocation.Line) {
                        continue; // Ignore multiple remarks on the same line.
                    }

                    prevItem = item;
                }
            }

            return firstSectionRemark;
        }

        private void ExpandAllTreeViewNodes(TreeViewItem rootItem) {
            foreach (object item in rootItem.Items) {
                var treeItem = item as TreeViewItem;

                if (treeItem != null) {
                    ExpandAllTreeViewNodes(treeItem);
                    treeItem.IsExpanded = true;
                }
            }
        }

        private void ExpandAllTreeViewNodes(TreeView treeView) {
            foreach (object item in treeView.Items) {
                var treeItem = (TreeViewItem)item;

                if (treeItem != null) {
                    ExpandAllTreeViewNodes(treeItem);
                    treeItem.IsExpanded = true;
                }
            }
        }

        private bool SelectTreeViewNode(TreeViewItem rootItem, object tag) {
            foreach (object item in rootItem.Items) {
                var treeItem = item as TreeViewItem;

                if (treeItem != null) {
                    if (treeItem.Tag == tag) {
                        SelectTreeViewNode(treeItem);
                        return true;
                    }

                    if (SelectTreeViewNode(treeItem, tag)) {
                        return true;
                    }
                }
            }

            return false;
        }

        private void SelectTreeViewNode(TreeViewItem treeItem) {
            treeItem.IsSelected = true;
            treeItem.BringIntoView();
        }

        private void SelectTreeViewNode(TreeView treeView, object tag) {
            foreach (object item in treeView.Items) {
                var treeItem = (TreeViewItem)item;

                if (treeItem != null) {
                    if (treeItem.Tag == tag) {
                        treeItem.IsSelected = true;
                        treeItem.BringIntoView();
                        return;
                    }

                    if (SelectTreeViewNode(treeItem, tag)) {
                        return;
                    }
                }
            }
        }

        private void ResetContextTreeTextSearch() {
            contextSearchInfo_ = null;
            contextSearchResults_ = null;
        }

        private void ApplyContextTreeTextSearch(TreeView treeView, SearchInfo searchInfo) {
            if (searchInfo == null || !searchInfo.HasSearchedText ||
                searchInfo.SearchedText.Length < 2) {
                return; // Search not enabled.
            }

            contextSearchInfo_ = searchInfo;
            contextSearchResults_ = new List<TreeViewItem>();

            foreach (object item in treeView.Items) {
                var treeItem = (TreeViewItem)item;

                if (treeItem != null) {
                    ApplyContextTreeTextSearch(treeItem);
                }
            }

            searchInfo.ResultCount = contextSearchResults_.Count;
            searchInfo.CurrentResult = 0;

            if(contextSearchResults_.Count > 0) {
                SelectTreeViewNode(contextSearchResults_[0]);
            }
        }

        private void ApplyContextTreeTextSearch(TreeViewItem rootItem) {
            var textBlock = rootItem.Header as TextBlock;

            if (textBlock != null) {
                // Change formatting of the line if it contains the searched text.
                var newTextBlock = ApplyTextLineSearch(textBlock, contextSearchInfo_);

                if (newTextBlock != textBlock) {
                    rootItem.Header = newTextBlock;
                    contextSearchResults_.Add(rootItem);
                }
            }

            foreach (object item in rootItem.Items) {
                var treeItem = item as TreeViewItem;

                if (treeItem != null) {
                    ApplyContextTreeTextSearch(treeItem);
                }
            }
        }

        private ListRemarkEx AppendAcceptedRemark(List<ListRemarkEx> list, Remark remark) {
            string sectionName = Session.CompilerInfo.NameProvider.GetSectionName(remark.Section);
            sectionName = $"({remark.Section.Number}) {sectionName}";
            var remarkEx = new ListRemarkEx(remark, sectionName, remark.Section == Section);
            list.Add(remarkEx);
            return remarkEx;
        }

        private void AccountForRemarkKind(Remark remark) {
            switch (remark.Kind) {
                case RemarkKind.Optimization: {
                    remarkFilter_.HasOptimizationRemarks = true;
                    break;
                }
                case RemarkKind.Analysis: {
                    remarkFilter_.HasAnalysisRemarks = true;
                    break;
                }
                case RemarkKind.Default: {
                    remarkFilter_.HasDefaultRemarks = true;
                    break;
                }
                case RemarkKind.Verbose: {
                    remarkFilter_.HasVerboseRemarks = true;
                    break;
                }
                case RemarkKind.Trace: {
                    remarkFilter_.HasTraceRemarks = true;
                    break;
                }
            }
        }

        public void NotifyPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateSize() {
            var width = Math.Max(RemarkPreviewWidth, ActualWidth);
            var height = Math.Max(RemarkListTop + RemarkListItemHeight *
                                  Math.Clamp(RemarkList.Items.Count, MinRemarkListItems, MaxRemarkListItems),
                                  ActualHeight);
            if (ShowPreview) {
                height += RemarkPreviewHeight;
            }

            UpdateSize(width, height);
        }

        private async void RemarkList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (e.AddedItems.Count != 1) {
                return;
            }

            var itemEx = e.AddedItems[0] as ListRemarkEx;
            selectedRemark_ = itemEx.Remark;
            activeRemarkContext_ = selectedRemark_.Context;
            SectionLabel.Content = itemEx.Description;
            ShowPreview = true;

            if (selectedRemark_.Context != null && IsContextTreeVisible()) {
                // Show context and children.
                await UpdateContextTree(selectedRemark_, activeRemarkContext_);
            }
            else {
                RemarksTabControl.SelectedItem = OutputTextTabItem;
                await UpdateOutputText(selectedRemark_);
            }
        }

        private async Task UpdateContextTree(Remark remark, RemarkContext context) {
            // Note that the context can also be a parent of the remarks context.
            var outputText = await Session.GetSectionOutputTextLinesAsync(remark.Section.OutputBefore,
                                                                          remark.Section);
            var list = new List<Remark>();
            CollectContextTreeRemarks(context, list, true);
            BuildContextRemarkTreeView(context, outputText);
            SelectTreeViewNode(RemarkContextTree, remark);

            if (FilterActiveContextRemarks) {
                NotifyRemarkContextChanged(context, list);
            }
        }

        private void NotifyRemarkContextChanged(RemarkContext context, List<Remark> list) {
            if (list == null && context != null) {
                list = new List<Remark>();
                CollectContextTreeRemarks(context, list, true);
            }

            RemarkContextChanged?.Invoke(this, new RemarkContextChangedEventArgs(context, list));
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            UpdateRemarkList();
        }

        public void Initialize(IRElement element, Point position, IRDocumentHost parent,
                               RemarkSettings filter) {
            parentDocument_ = parent;
            filter = (RemarkSettings)filter.Clone();
            remarkFilter_ = new RemarkSettingsEx(filter);
            ToolbarPanel.DataContext = remarkFilter_;

            UpdatePosition(position, parent);
            RemarkList.UnselectAll();
            SectionLabel.Content = "";
            ShowPreview = false;

            Element = element;
            UpdateSize();
        }

        private void RemarkList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            var itemEx = RemarkList.SelectedItem as ListRemarkEx;

            if (itemEx != null) {
                RemarkChanged?.Invoke(this, itemEx.Remark);
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e) {
            UpdateRemarkList();
        }

        private Color GenerateRandomPastelColor() {
#if false
            Random random = new Random();
            int red = random.Next(256);
            int green = random.Next(256);
            int blue = random.Next(256);

            Color mix = Colors.White;
            red = (red + mix.R) / 2;
            green = (green + mix.G) / 2;
            blue = (blue + mix.B) / 2;
            return Color.FromRgb((byte)red, (byte)green, (byte)blue);
#else
            string[] PastelColors = new string[] {
                "#FE9BA1",
                "#FFABA0",
                "#FFC69E",
                "#D8BBCA",
                "#D5A2BB",
                "#EAA2B9",
                "#FFAB9F",
                "#FFBEA1",
                "#FFE0A0",
                "#DBD2CA",
                "#D9B5B9",
                "#EAB4B8",
                "#FFBEA0",
                "#FFFF9F",
                "#C8F3A9",
                "#D4F3D4",
                "#CFD3BC",
                "#AAC4C5",
                "#99C4C6",
                "#9AE2B3",
                "#DCEFAC",
                "#A4C7F0",
                "#B7C6F0",
                "#DEDCB8",
                "#B6C9EE",
                "#EAB4B8",
                "#B0ABDB",
                "#CCA9DB",
                "#ACC6C5",
                "#F1DCB8"
            };

            return Utils.ColorFromString(PastelColors[new Random().Next(PastelColors.Length)]);
#endif
        }

        private void ColorSelector_ColorSelected(object sender, SelectedColorEventArgs e) {
            SetPanelAccentColor(e.SelectedColor);
            HideColorSelector();
        }

        private void HideColorSelector() {
            if (colorPopup_ != null) {
                colorPopup_.IsOpen = false;
                colorPopup_ = null;
            }
        }

        private void SetPanelAccentColor(Color color) {
            ToolbarPanel.Background = ColorBrushes.GetBrush(color);
            ContextToolbarPanel.Background = ColorBrushes.GetBrush(color);
            PanelBorder.BorderBrush = ColorBrushes.GetBrush(color);
            PanelBorder.BorderThickness = new Thickness(2);
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e) {
            var colorSelector = new ColorSelector();
            colorSelector.BorderBrush = SystemColors.ActiveBorderBrush;
            colorSelector.BorderThickness = new Thickness(1);
            colorSelector.Background = ToolbarPanel.Background;
            colorSelector.ColorSelected += ColorSelector_ColorSelected;

            var location = ToolbarPanel.PointToScreen(new Point(ColorButtonLeft, ToolbarPanel.ActualHeight));
            colorPopup_ = new Popup();
            colorPopup_.HorizontalOffset = location.X;
            colorPopup_.VerticalOffset = location.Y;
            colorPopup_.StaysOpen = true;
            colorPopup_.Child = colorSelector;
            colorPopup_.IsOpen = true;
        }

        private void PopupPanelButton_Click(object sender, RoutedEventArgs e) {
            DetachPanel();
        }

        public void DetachPanel() {
            if (IsDetached) {
                return;
            }

            DetachPopup();
            PopupPanelButton.Visibility = Visibility.Collapsed;
            ColorButton.Visibility = Visibility.Visible;
            SetPanelAccentColor(GenerateRandomPastelColor());
        }

        private void ClosePanelButton_Click(object sender, RoutedEventArgs e) {
            IsOpen = false;
        }

        public override bool ShouldStartDragging() {
            HideColorSelector();

            if (ToolbarPanel.IsMouseOver) {
                DetachPanel();
                return true;
            }

            return false;
        }

        private bool IsOutputTextVisible() {
            return RemarksTabControl.SelectedItem == OutputTextTabItem;
        }

        private bool IsContextTreeVisible() {
            return RemarksTabControl.SelectedItem == ContextTreeTabItem;
        }

        private async Task UpdateOutputText(Remark remark) {
            string outputText = await Session.GetSectionOutputTextAsync(remark.Section.OutputBefore,
                                                                        remark.Section);
            await RemarkTextView.SetText(outputText, Function, Section, parentDocument_.TextView, Session);
            RemarkTextView.SelectText(remark.RemarkLocation.Offset, remark.RemarkText.Length,
                                      remark.RemarkLocation.Line);
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (selectedRemark_ == null) {
                return;
            }

            await UpdateRemarkView();
            NotifyPropertyChanged(nameof(ShowSearchPanel));
        }

        private async Task UpdateRemarkView() {
            if (IsOutputTextVisible()) {
                await UpdateOutputText(selectedRemark_);
            }
            else if (IsContextTreeVisible() && activeRemarkContext_ != null) {
                await UpdateContextTree(selectedRemark_, activeRemarkContext_);
            }
        }

        private async void ContextParentButton_Click(object sender, RoutedEventArgs e) {
            if (activeRemarkContext_ == null ||
                activeRemarkContext_.Parent == null) {
                return;
            }

            activeRemarkContext_ = activeRemarkContext_.Parent;
            await UpdateContextTree(selectedRemark_, activeRemarkContext_);
        }

        private async void ContextSearchPanel_CloseSearchPanel(object sender, SearchInfo e) {
            ShowSearchPanel = false;
            ResetContextTreeTextSearch();
            await UpdateRemarkView();
        }

        private async void ContextSearchPanel_SearchChanged(object sender, SearchInfo e) {
            await UpdateRemarkView();
            ApplyContextTreeTextSearch(RemarkContextTree, e);
        }

        private void ContextSearchPanel_NavigateToResult(object sender, SearchInfo e) {
            if (contextSearchResults_ == null) {
                return;
            }

            SelectTreeViewNode(contextSearchResults_[e.CurrentResult]);
        }

    }
}
