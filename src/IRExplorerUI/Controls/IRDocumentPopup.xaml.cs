// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Utilities.UI;

namespace IRExplorerUI.Controls {
    /// <summary>
    /// Interaction logic for IRDocumentPopup.xaml
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
            var popup = CreatePopup(document.Section, previewedElement, position, width, height,
                                    owner ?? document.TextArea.TextView, document.Session, titlePrefix);
            popup.InitializeFromDocument(document);
            popup.CaptureMouseWheel();
            return popup;
        }

        public static async Task<IRDocumentPopup> CreateNew(ParsedIRTextSection parsedSection,
                                                            Point position, double width, double height,
                                                            UIElement owner, ISession session, string titlePrefix = "") {
            var popup = CreatePopup(parsedSection.Section, null, position, width, height,
                                    owner, session, titlePrefix);
            popup.TextView.Initalize(App.Settings.DocumentSettings, session);
            popup.TextView.EarlyLoadSectionSetup(parsedSection);
            await popup.TextView.LoadSection(parsedSection);
            popup.CaptureMouseWheel();
            return popup;
        }

        private static IRDocumentPopup CreatePopup(IRTextSection section, IRElement previewedElement,
                                                   Point position, double width, double height,
                                                   UIElement owner, ISession session, string titlePrefix) {
            var popup = new IRDocumentPopup(position, 500, 150, owner, session);

            if (previewedElement != null) {
                string elementText = Utils.MakeElementDescription(previewedElement);
                popup.PanelTitle = !string.IsNullOrEmpty(titlePrefix) ? $"{titlePrefix}{elementText}" : elementText;
            }
            else {
                popup.PanelTitle = titlePrefix;
            }

            popup.PanelToolTip = popup.Session.CompilerInfo.NameProvider.GetSectionName(section);
            popup.PreviewedElement = previewedElement;
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

            if (PreviewedElement != null) {
                MarkPreviewedElement(PreviewedElement, TextView);
            }
        }

        public override void ClosePopup() {
            owner_.PreviewMouseWheel -= Owner_OnPreviewMouseWheel;
            Session.UnregisterDetachedPanel(this);
            base.ClosePopup();
        }

        public void MarkPreviewedElement(IRElement element, IRDocument document) {
            if (PreviewedElement is BlockIR block) {
                if (block.HasLabel) {
                    document.MarkElementWithDefaultStyle(block.Label);
                    return;
                }
            }
            else {
                document.MarkElementWithDefaultStyle(PreviewedElement);
                return;
            }
            
            document.BringElementIntoView(PreviewedElement, BringIntoViewStyle.FirstLine);
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

        private async void OpenButton_Click(object sender, RoutedEventArgs e) {
            var args = new OpenSectionEventArgs(TextView.Section, OpenSectionKind.NewTabDockRight);

            //? TODO: BeginInvoke prevents the "Dispatcher suspended" assert that happens
            // with profiling, when Source panel shows the open dialog.
            // The dialog code should rather invoke the dispatcher...
            await Dispatcher.BeginInvoke(async () => {
                var result = await Session.OpenDocumentSectionAsync(args);

                //? TODO: Mark the previewed elem in the new doc
                // var similarValueFinder = new SimilarValueFinder(function_);
                // refElement = similarValueFinder.Find(instr);
                result.TextView.ScrollToVerticalOffset(TextView.VerticalOffset);
            }, DispatcherPriority.Background);
        }
    }

    public class IRDocumentPopupInstance {
        public class PreviewPopupArgs {
            public PreviewPopupArgs(IRElement element, UIElement relativeElement,
                                    IRDocument associatedDocument = null,
                                    ParsedIRTextSection associatedSection = null) {
                Element = element;
                RelativeElement = relativeElement;
                AssociatedDocument = associatedDocument;
                AssociatedSection = associatedSection;
            }

            public IRElement Element { get; set; }
            public UIElement RelativeElement { get; set; }
            public IRDocument AssociatedDocument { get; set; }
            public ParsedIRTextSection AssociatedSection { get; set; }
        }

        private IRDocumentPopup previewPopup_;
        private DelayedAction removeHoveredAction_;
        private Func<PreviewPopupArgs> previewedElementFinder_;
        private  double width_;
        private  double height_;
        private ISession session_;
        private string title_;

        public const double DefaultWidth = 600;
        public const double DefaultHeigth = 200;

        public IRDocumentPopupInstance(double width, double height, string title, ISession session) {
            width_ = width;
            height_ = height;
            session_ = session;
            title_ = title;
        }

        public void SetupHoverEvents(UIElement target, Func<PreviewPopupArgs> previewedElementFinder) {
            previewedElementFinder_ = previewedElementFinder;
            var hover = new MouseHoverLogic(target);
            hover.MouseHover += Hover_MouseHover;
            hover.MouseHoverStopped += Hover_MouseHoverStopped;
        }

        public void ShowPreviewPopupForDocument(IRDocument document, IRElement element, UIElement relativeElement) {
            ShowPreviewPopupForDocument(document, element, relativeElement, width_, height_, title_);
        }

        public void ShowPreviewPopupForDocument(IRDocument document, IRElement element, UIElement relativeElement,
                                            double width, double height, string titlePrefix) {
            if (!Prepare(element)) {
                return;
            }

            var position = Mouse.GetPosition(relativeElement).AdjustForMouseCursor();
            previewPopup_ = IRDocumentPopup.CreateNew(document, element, position, width, height,
                                                      relativeElement, titlePrefix);
            Complete();
        }

        public async Task ShowPreviewPopupForSection(ParsedIRTextSection parsedSection, UIElement relativeElement) {
            await ShowPreviewPopupForSection(parsedSection, relativeElement, width_, height_, title_);
        }

        public async Task ShowPreviewPopupForSection(ParsedIRTextSection parsedSection, UIElement relativeElement,
                                                     double width, double height, string title = "") {
            if (!Prepare()) {
                return;
            }

            var position = Mouse.GetPosition(relativeElement).AdjustForMouseCursor();
            previewPopup_ = await IRDocumentPopup.CreateNew(parsedSection, position, width, height,
                                                            relativeElement, session_, title);
            Complete();
        }

        public void HidePreviewPopup(bool force = false) {
            if (previewPopup_ != null && (force || !previewPopup_.IsMouseOver)) {
                previewPopup_.ClosePopup();
                previewPopup_ = null;
            }
        }

        private void HidePreviewPopupDelayed() {
            removeHoveredAction_ = DelayedAction.StartNew(() => {
                if (removeHoveredAction_ != null) {
                    removeHoveredAction_ = null;
                    HidePreviewPopup();
                }
            });
        }

        private bool Prepare(IRElement element = null) {
            if (previewPopup_ != null) {
                if (element != null && previewPopup_.PreviewedElement == element) {
                    return false; // Right preview already displayed.
                }

                HidePreviewPopup(true);
            }

            if (removeHoveredAction_ != null) {
                removeHoveredAction_.Cancel();
                removeHoveredAction_ = null;
            }

            return true;
        }

        private void Complete() {
            previewPopup_.PopupDetached += Popup_PopupDetached;
            previewPopup_.ShowPopup();
        }

        private void Popup_PopupDetached(object sender, EventArgs e) {
            var popup = (IRDocumentPopup)sender;

            if (popup == previewPopup_) {
                previewPopup_ = null; // Prevent automatic closing.
            }
        }

        private async void Hover_MouseHover(object sender, MouseEventArgs e) {
            var result = previewedElementFinder_();

            if (result.Element != null) {
                if (result.AssociatedDocument != null) {
                    ShowPreviewPopupForDocument(result.AssociatedDocument, result.Element, result.RelativeElement);
                }
                else if(result.AssociatedSection != null) {
                    await ShowPreviewPopupForSection(result.AssociatedSection, result.RelativeElement);
                }
                else {
                    throw new InvalidOperationException();
                }
            }
        }

        private void Hover_MouseHoverStopped(object sender, MouseEventArgs e) {
            HidePreviewPopupDelayed();
        }
    }
}
