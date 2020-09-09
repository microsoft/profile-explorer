using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Controls;

namespace IRExplorerUI.Document {
    public class RemarkEx {
        public Remark Remark { get; set; }

        public string Text {
            get {
                if (!string.IsNullOrEmpty(Remark.Category.Title)) {
                    return $"{Remark.Section.Number} | {Remark.RemarkText}";
                }

                return FormatRemarkText();
            }
        }

        public Brush Background =>
            Utils.EstimateBrightness(Remark.Category.MarkColor) < 200 ?
            ColorBrushes.GetBrush(Utils.ChangeColorLuminisity(Remark.Category.MarkColor, 1.75)) :
            ColorBrushes.GetBrush(Utils.ChangeColorLuminisity(Remark.Category.MarkColor, 1.2));

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

    public class ContextTreeRemarkEx : RemarkEx {

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
        private Popup colorPopup_;

        public RemarkPreviewPanel() {
            InitializeComponent();
            DataContext = this; // Used for auto-resizing with ShowPreview.
            PanelResizeGrip.ResizedControl = this;
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

        public FunctionIR Function { get; set; }
        public IRTextSection Section { get; set; }
        public ISession Session { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<RemarkContextChangedEventArgs> RemarkContextChanged;
        public event EventHandler<Remark> RemarkChanged;

        private void UpdateRemarkList() {
            RemarkList.ItemsSource = null;
            var remarkTag = element_.GetTag<RemarkTag>();

            if (remarkTag != null) {
                var list = new List<Remark>();
                var listEx = new List<ListRemarkEx>();
                ListRemarkEx firstSectionRemark = null;

                if (activeRemarkContext_ != null &&
                    remarkFilter_.Settings.ShowOnlyContextRemarks) {
                    CollectContextTreeRemarks(activeRemarkContext_, list, false);

                    foreach (var remark in list) {
                        AppendAcceptedRemark(listEx, remark);
                    }
                }
                else {
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
                }

                RemarkList.ItemsSource = listEx;

                // Scroll down to current section.
                if (firstSectionRemark != null) {
                    RemarkList.ScrollIntoView(firstSectionRemark);
                }
            }

            ContextRemarkTree.Items.Clear();
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
            Remark prevRemark = null;

            foreach (var remark in context.Remarks) {
                var line = remark.RemarkLocation.Line;

                if (parentDocument_.IsAcceptedRemark(remark, Section, remarkFilter_.Settings)) {
                    if (prevRemark != null) {
                        var prevLine = prevRemark.RemarkLocation.Line + 1;
                        ExtractOutputTextInRange(prevLine, line, outputTextLines, items);
                    }

                    var tempTreeNode = new TreeViewItem() {
                        Header = remark.RemarkText,
                        Foreground = ColorBrushes.GetBrush(Colors.Black),
                        FontWeight = FontWeights.Bold,
                        Tag = remark,
                        ItemContainerStyle = Application.Current.FindResource("RemarkTreeViewItemStyle") as Style
                    };

                    items.Add(new Tuple<TreeViewItem, int>(tempTreeNode, remark.RemarkLocation.Line));
                    prevRemark = remark;
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
                                     outputTextLines, items);

            int secondRegionStart = Math.Max(firstRegionEnd,
                context.Remarks.Count > 0 ? context.Remarks[^1].RemarkLocation.Line + 1 : context.StartLine);
            int secondRegionEnd = context.EndLine;

            if (context.Children.Count > 0) {
                secondRegionStart = Math.Max(secondRegionStart, context.Children[^1].EndLine);
            }

            ExtractOutputTextInRange(secondRegionStart, secondRegionEnd,
                                     outputTextLines, items);

            // Recursively add remarks from the child contexts.
            foreach (var child in context.Children) {
                var (childTreeNode, childFirstLine) =
                    BuildContextRemarkTreeView(child, treeNodeMap, outputTextLines);
                items.Add(new Tuple<TreeViewItem, int>(childTreeNode, childFirstLine));
            }

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

        private static void ExtractOutputTextInRange(int prevLine, int line, string[] outputTextLines,
                                                     List<Tuple<TreeViewItem, int>> items) {
            for (int k = prevLine; k < line; k++) {
                var lineText = outputTextLines[k];

                //? TODO: Check could be part of the remark provider (ShouldIgnore...)
                //? but a cleaner approach should be having a pass output filter interface,
                //? with Session.GetSectionPassOutputAsync using it, plus a new
                //? GetRawSectionPassOutputAsync so that the remark prov. gets the metadata lines
                if (!lineText.StartsWith("/// irx:")) {
                    var lineTreeNode = new TreeViewItem() {
                        Header = lineText,
                        Foreground = ColorBrushes.GetBrush(Colors.DimGray),
                    };
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
            var item = itemEx.Remark;
            string outputText = await Session.GetSectionPassOutputAsync(item.Section.OutputBefore, item.Section);

            TextView.Text = outputText;
            TextView.ScrollToLine(item.RemarkLocation.Line);
            TextView.Select(item.RemarkLocation.Offset, item.RemarkText.Length);
            SectionLabel.Content = itemEx.Description;
            ShowPreview = true;

            if (item.Context != null) {
                // Show context and children.
                var list = new List<Remark>();
                CollectContextTreeRemarks(item.Context, list, true);
                BuildContextRemarkTreeView(item.Context, outputText);
                SelectTreeViewNode(ContextRemarkTree, item);

                //? TODO: Setting for enabling
                if (true) {
                    RemarkContextChanged?.Invoke(this, new RemarkContextChangedEventArgs(item.Context, list));
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            UpdateRemarkList();
        }

        public void Initialize(IRElement element, Point position, IRDocumentHost parent,
                               RemarkSettings filter, RemarkContext activeRemarkContext = null) {
            parentDocument_ = parent;
            activeRemarkContext_ = activeRemarkContext;

            filter = (RemarkSettings)filter.Clone();
            filter.ShowOnlyContextRemarks = activeRemarkContext_ != null;
            remarkFilter_ = new RemarkSettingsEx(filter);
            ToolbarPanel.DataContext = remarkFilter_;

            UpdatePosition(position, parent);
            RemarkList.UnselectAll();
            SectionLabel.Content = "";
            ShowPreview = false;
            UpdateSize();

            Element = element;
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
            ClosePanelButton.Visibility = Visibility.Visible;
            SetPanelAccentColor(GenerateRandomPastelColor());
        }

        private void ClosePanelButton_Click(object sender, RoutedEventArgs e) {
            IsOpen = false;
        }

        private void CollapseTextViewButton_Click(object sender, RoutedEventArgs e) {
            ShowPreview = false;
        }

        public override bool ShouldStartDragging() {
            HideColorSelector();

            if (ToolbarPanel.IsMouseOver) {
                DetachPanel();
                return true;
            }

            return false;
        }

        private void ContextRemarkTree_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            var remark = (ContextRemarkTree.SelectedItem as TreeViewItem)?.Tag as Remark;

            if (remark != null) {
                RemarkChanged?.Invoke(this, remark);
            }
        }
    }
}
