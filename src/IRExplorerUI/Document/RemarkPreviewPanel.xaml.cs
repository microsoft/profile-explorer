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
        public RemarkEx(Remark remark, string sectionName, bool inCurrentSection) {
            Remark = remark;
            InCurrentSection = inCurrentSection;
            SectionName = sectionName;
            ContextTreeLevel = -1;
        }

        public Remark Remark { get; set; }
        public bool InCurrentSection { get; set; }
        public string SectionName { get; set; }
        public int ContextTreeLevel { get; set; }

        public bool IsOptimization => Remark.Kind == RemarkKind.Optimization;
        public bool IsAnalysis => Remark.Kind == RemarkKind.Analysis;
        public bool HasCustomBackground => Remark.Category.MarkColor != Colors.Black;
        public string Description => SectionName;

        public string Text {
            get {
                if (!string.IsNullOrEmpty(Remark.Category.Title)) {
                    return $"{Remark.Section.Number} | {Remark.RemarkText}";
                }

                if (ContextTreeLevel >= 0) {
                    var contextName = Remark.Context?.Name ?? "";
                    return $"{contextName} | {FormatRemarkText()}";
                }

                return FormatRemarkText();
            }
        }

        private string FormatRemarkText() {
            if (Remark.Kind == RemarkKind.None) {
                return Remark.RemarkText;
            }

            return $"{Remark.Section.Number} | {Remark.RemarkText}";
        }

        public Brush Background =>
            Utils.EstimateBrightness(Remark.Category.MarkColor) < 200 ?
            ColorBrushes.GetBrush(Utils.ChangeColorLuminisity(Remark.Category.MarkColor, 1.75)) :
            ColorBrushes.GetBrush(Utils.ChangeColorLuminisity(Remark.Category.MarkColor, 1.2));
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

    public partial class RemarkPreviewPanel : DraggablePopup, INotifyPropertyChanged {
        private const double RemarkListTop = 48;
        private const double RemarkPreviewWidth = 600;
        private const double RemarkPreviewHeight = 200;
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
        public event EventHandler<RemarkContext> RemarkContextChanged;

        private void UpdateRemarkList() {
            RemarkList.ItemsSource = null;
            var remarkTag = element_.GetTag<RemarkTag>();

            if (remarkTag != null) {
                var list = new List<RemarkEx>();
                RemarkEx firstSectionRemark = null;

                if (remarkFilter_.Settings.ShowOnlyContextRemarks) {
                    firstSectionRemark = CollectContextTreeRemarks(activeRemarkContext_, list);
                }
                else {
                    foreach (var remark in remarkTag.Remarks) {
                        AccountForRemarkKind(remark);

                        if (parentDocument_.IsAcceptedContextRemark(remark, Section, remarkFilter_.Settings)) {
                            var remarkEx = AppendAcceptedRemark(list, remark);

                            // Find first remark in current section.
                            if (remark.Section == Section && firstSectionRemark == null) {
                                firstSectionRemark = remarkEx;
                            }
                        }
                    }
                }

                RemarkList.ItemsSource = list;

                // Scroll down to current section.
                if (firstSectionRemark != null) {
                    RemarkList.ScrollIntoView(firstSectionRemark);
                }
            }
        }

        //? TODO: Should be async and run the collection on another thread
        private RemarkEx CollectContextTreeRemarks(RemarkContext rootContext, List<RemarkEx> list,
                                                   string outputText = null) {
            var treeNodeMap = new Dictionary<RemarkContext, TreeViewItem>();
            RemarkEx firstSectionRemark = null;
            var worklist = new Queue<RemarkContext>();
            worklist.Enqueue(rootContext);

            TreeViewItem treeRootNode = new TreeViewItem();
            treeRootNode.Header = rootContext.Name;
            treeNodeMap[rootContext] = treeRootNode;

            while (worklist.Count > 0) {
                var context = worklist.Dequeue();
                var treeNode = treeNodeMap[context];

                foreach (var remark in context.Remarks) {
                    if (parentDocument_.IsAcceptedRemark(remark, Section, remarkFilter_.Settings)) {
                        var remarkEx = AppendAcceptedRemark(list, remark);
                        remarkEx.ContextTreeLevel = context.ContextTreeLevel - rootContext.ContextTreeLevel;

                        // Find first remark in current section.
                        if (remark.Section == Section && firstSectionRemark == null) {
                            firstSectionRemark = remarkEx;
                        }
                    }
                }

                foreach (var child in context.Children) {
                    worklist.Enqueue(child);
                    var childTreeNode = new TreeViewItem();
                    childTreeNode.Header = child.Name;
                    treeNode.Items.Add(childTreeNode);
                    treeNodeMap[child] = childTreeNode;
                }
            }

            //? TODO: Could use an API that doesn't need splitting into lines again
            var outputTextLines = outputText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var newList = new List<RemarkEx>(list.Count);
            RemarkEx prevRemark = null;

            var outputCategory = new RemarkCategory {
                Kind = RemarkKind.None,
                Title = "",
                SearchedText = "",
                MarkColor = Colors.Gray,
            };


            for (int i = 0; i < list.Count; i++) {
                var remark = list[i];
                var treeNode = treeNodeMap[remark.Remark.Context];

                if (prevRemark != null) {
                    var prevLine = prevRemark.Remark.OutputElements[0].TextLocation.Line;
                    var line = remark.Remark.OutputElements[0].TextLocation.Line;

                    // Line numbers start with 1, +2 is needed to start with the line after the prev. remark.
                    for (int k = prevLine + 2; k <= line; k++) {
                        var lineText = outputTextLines[k];

                        //? TODO: Check could be part of the remark provider (ShouldIgnore...)
                        //? but a cleaner approach should be having a pass output filter interface,
                        //? with Session.GetSectionPassOutputAsync using it, plus a new
                        //? GetRawSectionPassOutputAsync so that the remark prov. gets the metadata lines
                        if (!lineText.StartsWith("/// irx:")) {
                            var temp = new Remark(outputCategory, remark.Remark.Section,
                                                  lineText, new TextLocation(0, k, 0));
                            treeNode.Items.Add(temp.RemarkText);
                            //var tempEx = new RemarkEx(temp, remark.SectionName, false);
                            //newList.Add(tempEx);
                        }
                    }
                }

                treeNode.Items.Add(remark.Text);

                newList.Add(remark);
                prevRemark = remark;
            }

            list.Clear();
            list.AddRange(newList);

            ContextRemarkTree.Items.Clear();
            ContextRemarkTree.Items.Add(treeRootNode);

            return firstSectionRemark;
        }

        private RemarkEx AppendAcceptedRemark(List<RemarkEx> list, Remark remark) {
            string sectionName = Session.CompilerInfo.NameProvider.GetSectionName(remark.Section);
            sectionName = $"({remark.Section.Number}) {sectionName}";
            var remarkEx = new RemarkEx(remark, sectionName, remark.Section == Section);
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

            var itemEx = e.AddedItems[0] as RemarkEx;
            var item = itemEx.Remark;
            string outputText = await Session.GetSectionPassOutputAsync(item.Section.OutputBefore, item.Section);

            TextView.Text = outputText;
            TextView.ScrollToLine(item.RemarkLocation.Line);
            TextView.Select(item.RemarkLocation.Offset, item.RemarkText.Length);
            SectionLabel.Content = itemEx.Description;
            ShowPreview = true;

            if (item.Context != null) {
                // Show context and children.
                var list = new List<RemarkEx>();
                CollectContextTreeRemarks(item.Context, list, outputText);
                //ContextRemarkList.ItemsSource = list;

                //if (list.Count > 0) {
                //    foreach (var contextRemark in list) {
                //        if (contextRemark.Remark == item) {
                //            ContextRemarkList.SelectedItem = contextRemark;
                //            ContextRemarkList.ScrollIntoView(contextRemark);
                //            break;
                //        }
                //    }
                //}
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
            var itemEx = RemarkList.SelectedItem as RemarkEx;

            if (itemEx != null) {
                var context = itemEx.Remark.Context;
                RemarkContextChanged?.Invoke(this, context);
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
    }
}
