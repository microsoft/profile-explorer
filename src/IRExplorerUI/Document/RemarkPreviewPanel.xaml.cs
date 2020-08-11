using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI.Document {
    public class RemarkEx {
        public RemarkEx(Remark remark, string sectionName, bool inCurrentSection) {
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

    public partial class RemarkPreviewPanel : DraggablePopup, INotifyPropertyChanged {
        private const double RemarkListTop = 48;
        private const double RemarkPreviewWidth = 600;
        private const double RemarkPreviewHeight = 200;
        private const double RemarkListItemHeight = 20;
        private const double MaxRemarkListItems = 10;
        private const double MinRemarkListItems = 3;
        private const double ColorButtonLeft = 175;

        private IRElement element_;
        private RemarkSettings remarkFilter_;
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
                var list = new List<RemarkEx>();
                RemarkEx firstSectionRemark = null;

                foreach (var remark in remarkTag.Remarks) {
                    if (IRDocumentHost.IsAcceptedRemark(remark, Section, remarkFilter_)) {
                        string sectionName = Session.CompilerInfo.NameProvider.GetSectionName(remark.Section);
                        sectionName = $"({remark.Section.Number}) {sectionName}";

                        var remarkEx = new RemarkEx(remark, sectionName, remark.Section == Section);
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
            var width = RemarkPreviewWidth;
            var height = RemarkListTop + RemarkListItemHeight *
                         Math.Clamp(RemarkList.Items.Count, MinRemarkListItems, MaxRemarkListItems);

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
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            UpdateRemarkList();
        }

        public void Initialize(Point position, UIElement referenceElement) {
            UpdatePosition(position, referenceElement);
            RemarkList.UnselectAll();
            SectionLabel.Content = "";
            ShowPreview = false;
            UpdateSize();
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
