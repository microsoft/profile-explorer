using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorer.Document {
    public class RemarkExtension {
        public RemarkExtension(Remark remark, string sectionName, bool inCurrentSection) {
            Remark = remark;
            InCurrentSection = inCurrentSection;
            SectionName = sectionName;
        }

        public Remark Remark { get; set; }
        public bool InCurrentSection { get; set; }
        public string SectionName { get; set; }

        public bool IsOptimization => Remark.Kind == RemarkKind.Optimization;
        public bool IsAnalysis => Remark.Kind == RemarkKind.Analysis;
        public bool HasCustomBackground => Remark.Category.MarkColor != Colors.Black;
        public string Description => SectionName;

        public string Text {
            get {
                if (!string.IsNullOrEmpty(Remark.Category.Title)) {
                    return $"{Remark.Section.Number} | {Remark.RemarkText}";
                }

                return $"{Remark.Section.Number} | {Remark.RemarkText}";
            }
        }

        public Brush Background =>
            Utils.EstimateBrightness(Remark.Category.MarkColor) < 200 ?
            ColorBrushes.GetBrush(Utils.ChangeColorLuminisity(Remark.Category.MarkColor, 1.75)) :
            ColorBrushes.GetBrush(Utils.ChangeColorLuminisity(Remark.Category.MarkColor, 1.2));
    }

    public partial class RemarkPanel : Window, INotifyPropertyChanged {
        private static readonly double RemarkListTop = 48;
        private static readonly double RemarkPreviewWidth = 700;
        private static readonly double RemarkPreviewHeight = 200;
        private static readonly double RemarkListItemHeight = 20;
        private static readonly double MaxRemarkListItems = 10;
        private static readonly double MinRemarkListItems = 3;

        private IRElement element_;
        private RemarkSettings remarkFilter_;
        private bool showPreview_;

        public RemarkPanel() {
            InitializeComponent();
            DataContext = this; // Used for auto-resizing with ShowPreview.
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
        public ISessionManager Session { get; set; }

        public RemarkSettings RemarkFilter {
            get => remarkFilter_;
            set {
                remarkFilter_ = (RemarkSettings)value.Clone();
                ToolbarPanel.DataContext = remarkFilter_;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<RemarkContext> RemarkContextChanged;

        private void UpdateRemarkList() {
            RemarkList.ItemsSource = null;
            var remarkTag = element_.GetTag<RemarkTag>();

            if (remarkTag != null) {
                var list = new List<RemarkExtension>();
                RemarkExtension firstSectionRemark = null;

                foreach (var remark in remarkTag.Remarks) {
                    if (IRDocumentHost.IsAcceptedRemark(remark, Section, remarkFilter_)) {
                        string sectionName = Session.CompilerInfo.NameProvider.GetSectionName(remark.Section);
                        sectionName = $"({remark.Section.Number}) {sectionName}";

                        var remarkEx =
                            new RemarkExtension(remark, sectionName, remark.Section == Section);

                        list.Add(remarkEx);

                        // Find first remark in current section.
                        if (remark.Section == Section && firstSectionRemark == null) {
                            firstSectionRemark = remarkEx;
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

        public void NotifyPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateSize() {
            Width = RemarkPreviewWidth;

            Height = RemarkListTop +
                     RemarkListItemHeight *
                     Math.Clamp(RemarkList.Items.Count, MinRemarkListItems, MaxRemarkListItems);

            if (ShowPreview) {
                Height += RemarkPreviewHeight;
            }
        }

        private async void RemarkList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (e.AddedItems.Count != 1) {
                return;
            }

            var itemEx = e.AddedItems[0] as RemarkExtension;
            var item = itemEx.Remark;

            string outputText =
                await Session.GetSectionPassOutputAsync(item.Section.OutputBefore, item.Section);

            TextView.Text = outputText;
            TextView.ScrollToLine(item.RemarkLocation.Line);
            TextView.Select(item.RemarkLocation.Offset, item.RemarkText.Length);
            SectionLabel.Content = itemEx.Description;
            ShowPreview = true;
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            UpdateRemarkList();
        }

        internal void Initialize(double x, double y) {
            Left = x;
            Top = y;
            RemarkList.UnselectAll();
            SectionLabel.Content = "";
            ShowPreview = false;
            UpdateSize();
        }

        private void RemarkList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            var itemEx = RemarkList.SelectedItem as RemarkExtension;

            if (itemEx != null) {
                var context = itemEx.Remark.Context;
                RemarkContextChanged?.Invoke(this, context);
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e) {
            UpdateRemarkList();
        }
    }
}
