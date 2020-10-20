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

                if(matchingQuery != null) {
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

        public RemarkPreviewPanel() {
            InitializeComponent();
            PanelResizeGrip.ResizedControl = this;
            DataContext = this; // Used for auto-resizing with ShowPreview.

            //? TODO: Add options
            filterActiveContextRemarks_ = true;
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

        public FunctionIR Function { get; set; }
        public IRTextSection Section { get; set; }
        public ISession Session { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<RemarkContextChangedEventArgs> RemarkContextChanged;
        public event EventHandler<Remark> RemarkChanged;

        private void UpdateRemarkList() {
            RemarkList.ItemsSource = null;
            ContextRemarkTree.Items.Clear();

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
                                               string[] outputTextLines) {
            if (!treeNodeMap.TryGetValue(context, out var treeNode)) {
                treeNode = new TreeViewItem() {
                    Header = context.Name,
                    Foreground = ColorBrushes.GetBrush(Colors.DarkBlue),
                    FontWeight = FontWeights.Bold,
                    ItemContainerStyle = Application.Current.FindResource("RemarkTreeViewItemStyle") as Style
                };

                treeNodeMap[context] = treeNode;
            }

            var items = new List<Tuple<TreeViewItem, int>>();
            var highlightingList = Session.CompilerInfo.RemarkProvider.RemarkTextHighlighting;
            Remark prevRemark = null;

            foreach (var remark in context.Remarks) {
                var line = remark.RemarkLocation.Line;

                if (prevRemark != null) {
                    var prevLine = prevRemark.RemarkLocation.Line + 1;

                    //? If there is a context between the remarks,
                    //? only the text that is not included in the children should be added
                    // remark 1
                    //    other text
                    // context 1
                    //    other text
                    // context 2
                    //    other text
                    // remark2

                    ExtractOutputTextInRange(prevLine, line, outputTextLines, highlightingList, items);
                }

                prevRemark = remark;

                if (parentDocument_.IsAcceptedRemark(remark, Section, remarkFilter_.Settings)) {
                    var tempTreeNode = new TreeViewItem() {
                        Background = RemarkEx.GetRemarkBackground(remark),
                        ItemContainerStyle = Application.Current.FindResource("RemarkTreeViewItemStyle") as Style,
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
            int firstRegionEnd = context.Remarks.Count > 0 ? context.Remarks[0].RemarkLocation.Line : context.EndLine;

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

            int secondRegionStart = Math.Max(firstRegionEnd,
                context.Remarks.Count > 0 ? context.Remarks[^1].RemarkLocation.Line + 1 : context.StartLine);
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
                if (prevItem != null && prevItem.Item2 == item.Item2) {
                    continue; // Ignore multiple remarks on the same line.
                }

                treeNode.Items.Add(item.Item1);
                prevItem = item;
            }

            return (treeNode, context.StartLine);
        }

        

        private void TempTreeNode_Selected(object sender, RoutedEventArgs e) {
            var remark = (ContextRemarkTree.SelectedItem as TreeViewItem)?.Tag as Remark;

            if (remark != null) {
                RemarkChanged?.Invoke(this, remark);
            }
        }

        private static void ExtractOutputTextInRange(int prevLine, int line, string[] outputTextLines,
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
                    items.Add(new Tuple<TreeViewItem, int>(lineTreeNode, k));
                }
            }
        }

        private void BuildContextRemarkTreeView(RemarkContext rootContext, string outputText = null) {
            var treeNodeMap = new Dictionary<RemarkContext, TreeViewItem>();

            //? TODO: Could use an API that doesn't need splitting into lines again
            var outputTextLines = outputText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var (rootTreeNode, _) = BuildContextRemarkTreeView(rootContext, treeNodeMap, outputTextLines);

            ContextRemarkTree.Items.Clear();
            ContextRemarkTree.Items.Add(rootTreeNode);
            ExpandAllTreeViewNodes(ContextRemarkTree);
        }

        //? TODO: Should be async and run the collection on another thread
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
                TreeViewItem treeItem = item as TreeViewItem;

                if (treeItem != null) {
                    ExpandAllTreeViewNodes(treeItem);
                    treeItem.IsExpanded = true;
                }
            }
        }

        private void ExpandAllTreeViewNodes(TreeView treeView) {
            foreach (object item in treeView.Items) {
                TreeViewItem treeItem = (TreeViewItem)item;

                if (treeItem != null) {
                    ExpandAllTreeViewNodes(treeItem);
                    treeItem.IsExpanded = true;
                }
            }
        }

        private bool SelectTreeViewNode(TreeViewItem rootItem, object tag) {
            foreach (object item in rootItem.Items) {
                TreeViewItem treeItem = item as TreeViewItem;

                if (treeItem != null) {
                    if (treeItem.Tag == tag) {
                        treeItem.IsSelected = true;
                        return true;
                    }

                    if (SelectTreeViewNode(treeItem, tag)) {
                        return true;
                    }
                }
            }

            return false;
        }

        private void SelectTreeViewNode(TreeView treeView, object tag) {
            foreach (object item in treeView.Items) {
                TreeViewItem treeItem = (TreeViewItem)item;

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
            // Note that the context can also be a parent of the remark's context.
            string outputText = await Session.GetSectionPassOutputAsync(remark.Section.OutputBefore,
                                                                        remark.Section);
            var list = new List<Remark>();
            CollectContextTreeRemarks(context, list, true);
            BuildContextRemarkTreeView(context, outputText);
            SelectTreeViewNode(ContextRemarkTree, remark);

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

        private void ColorSelector_ColorSelected(object sender, ColorEventArgs e) {
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

        private void DetachPanel() {
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
            string outputText = await Session.GetSectionPassOutputAsync(remark.Section.OutputBefore,
                                                                    remark.Section);

            await TextView.SetText(outputText, Function, Section, parentDocument_.TextView, Session);
            TextView.SelectText(remark.RemarkLocation.Offset, remark.RemarkText.Length,
                                remark.RemarkLocation.Line);
        }

        private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (selectedRemark_ == null) {
                return;
            }

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
    }
}
