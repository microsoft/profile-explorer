using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using IRExplorerCore;
using IRExplorerCore.IR;

namespace IRExplorerUI.Controls {
    /// <summary>
    /// Interaction logic for NotesPopup.xaml
    /// </summary>
    public partial class IRDocumentPopup : DraggablePopup, INotifyPropertyChanged {
        private string panelTitle_;
        private string panelToolTip_;
        private UIElement owner_;

        public event PropertyChangedEventHandler PropertyChanged;
        
        public IRDocumentPopup(Point position, double width, double height,
                               UIElement owner, ISession session) {
            InitializeComponent();
            Initialize(position, width, height, owner);
            TextView.PreviewMouseWheel += TextView_OnMouseWheel;
            PanelResizeGrip.ResizedControl = this;
            DataContext = this;
            Session = session;
            owner_ = owner;
        }

        public static IRDocumentPopup CreateNew(IRDocument document, IRElement previewedElement,
            Point position, double width, double height, UIElement owner, string titlePrefix = "") {
            var popup = CreatePopup(document.Section, previewedElement, position,
                                    owner ?? document.TextArea.TextView, document.Session, titlePrefix);
            popup.InitializeFromDocument(document);
            popup.CaptureMouseWheel();
            return popup;
        }

        public static async Task<IRDocumentPopup> CreateNew(ParsedIRTextSection parsedSection, IRElement previewedElement,
                                                            Point position, double width, double height,
                                                            UIElement owner, ISession session, string titlePrefix) {
            var popup = CreatePopup(parsedSection.Section, previewedElement, position, 
                                    owner, session, titlePrefix);
            popup.TextView.Initalize(App.Settings.DocumentSettings, session);
            popup.TextView.EarlyLoadSectionSetup(parsedSection);
            await popup.TextView.LoadSection(parsedSection);
            popup.CaptureMouseWheel();
            return popup;
        }

        private static IRDocumentPopup CreatePopup(IRTextSection section, IRElement previewedElement,
                                                   Point position, UIElement owner, ISession session, string titlePrefix) {
            var popup = new IRDocumentPopup(position, 500, 150, owner, session);
            string elementText = Utils.MakeElementDescription(previewedElement);
            popup.PanelTitle = !string.IsNullOrEmpty(titlePrefix) ? $"{titlePrefix}{elementText}" : elementText;
            popup.PanelToolTip = popup.Session.CompilerInfo.NameProvider.GetSectionName(section);
            return popup;
        }

        public IRElement PreviewedElement { get; set; }

        public void InitializeFromDocument(IRDocument document, string text = null) {
            TextView.InitializeFromDocument(document, false, text);
        }

        public void InitializeBasedOnDocument(string text, IRDocument document) {
            TextView.InitializeBasedOnDocument(text, document);
        }

        public ISession Session { get; set; }

        public string PanelTitle {
            get => panelTitle_;
            set {
                if (panelTitle_ != value) {
                    panelTitle_ = value;
                    OnPropertyChange(nameof(PanelTitle));
                }
            }
        }

        public string PanelToolTip {
            get => panelToolTip_;
            set {
                if (panelToolTip_ != value) {
                    panelToolTip_ = value;
                    OnPropertyChange(nameof(PanelToolTip));
                }
            }
        }

        public override void ShowPopup() {
            base.ShowPopup();
            UpdateView();
        }

        public override void ClosePopup() {
            owner_.PreviewMouseWheel -= Owner_OnPreviewMouseWheel;
            Session.UnregisterDetachedPanel(this);
            base.ClosePopup();
        }

        public void UpdateView(bool highlight = true) {
            if (PreviewedElement == null) {
                return;
            }

            if (highlight) {
                if (PreviewedElement is BlockIR block) {
                    if (block.HasLabel) {
                        TextView.MarkElementWithDefaultStyle(block.Label);
                        return;
                    }
                }
                else {
                    TextView.MarkElementWithDefaultStyle(PreviewedElement);
                    return;
                }
            }
                
            TextView.BringElementIntoView(PreviewedElement, BringIntoViewStyle.FirstLine);
        }

        public void CaptureMouseWheel() {
            owner_.PreviewMouseWheel += Owner_OnPreviewMouseWheel;
        }

        private void Owner_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            if (Utils.IsControlModifierActive()) {
                double amount = Utils.IsShiftModifierActive() ? 3 : 1;
                AdjustVerticalPosition(e.Delta < 0 ? amount : -amount);
                e.Handled = true;
            }
        }

        public override bool ShouldStartDragging(MouseButtonEventArgs e) {
            if (e.LeftButton == MouseButtonState.Pressed && ToolbarPanel.IsMouseOver) {
                if (!IsDetached) {
                    DetachPopup();
                    EnableVerticalScrollbar();
                    SetPanelAccentColor(ColorUtils.GenerateRandomPastelColor());
                    Session.RegisterDetachedPanel(this);
                }

                return true;
            }

            return false;
        }

        private void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            ClosePopup();
        }

        public void AdjustVerticalPosition(double amount) {
            // Make scroll bar visible, it's not by default.
            EnableVerticalScrollbar();

            amount *= TextView.TextArea.TextView.DefaultLineHeight;
            double newOffset = TextView.VerticalOffset + amount;
            TextView.ScrollToVerticalOffset(newOffset);
        }

        private void TextView_OnMouseWheel(object sender, MouseWheelEventArgs e) {
            EnableVerticalScrollbar();
        }

        private void EnableVerticalScrollbar() {
            TextView.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            ScrollViewer.SetVerticalScrollBarVisibility(TextView, ScrollBarVisibility.Auto);
        }

        private void SetPanelAccentColor(Color color) {
            ToolbarPanel.Background = ColorBrushes.GetBrush(color);
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e) {

        }
    }
}
