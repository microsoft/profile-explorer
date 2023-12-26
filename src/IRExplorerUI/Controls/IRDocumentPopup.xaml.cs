// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Utilities.UI;

namespace IRExplorerUI.Controls;

public partial class IRDocumentPopup : DraggablePopup, INotifyPropertyChanged {
  public static readonly double DefaultWidth = 500;
  public static readonly double DefaultHeight = 200;
  private string panelTitle_;
  private string panelToolTip_;
  private UIElement owner_;

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

  protected override void SetPanelAccentColor(Color color) {
    ToolbarPanel.Background = ColorBrushes.GetBrush(color);
    PanelBorder.BorderBrush = ColorBrushes.GetBrush(color);
  }

  public event PropertyChangedEventHandler PropertyChanged;
  public IRElement PreviewedElement { get; set; }
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

  public static async Task<IRDocumentPopup> CreateNew(IRDocument document, IRElement previewedElement,
                                                      Point position, double width, double height, UIElement owner,
                                                      string titlePrefix = "") {
    var popup = CreatePopup(document.Section, previewedElement, position, width, height,
                            owner ?? document.TextArea.TextView, document.Session, titlePrefix);
    await popup.InitializeFromDocument(document);
    popup.CaptureMouseWheel();
    return popup;
  }

  public static async Task<IRDocumentPopup> CreateNew(ParsedIRTextSection parsedSection,
                                                      Point position, double width, double height,
                                                      UIElement owner, ISession session, string titlePrefix = "") {
    var popup = CreatePopup(parsedSection.Section, null, position, width, height,
                            owner, session, titlePrefix);
    popup.TextView.TextView.Initalize(App.Settings.DocumentSettings, session);
    popup.TextView.TextView.EarlyLoadSectionSetup(parsedSection);
    await popup.TextView.TextView.LoadSection(parsedSection);

    //? TODO: UI option
    if (true) {
      await popup.TextView.ShowProfilingColumns();
    }

    popup.CaptureMouseWheel();
    return popup;
  }

  private static IRDocumentPopup CreatePopup(IRTextSection section, IRElement previewedElement,
                                             Point position, double width, double height,
                                             UIElement owner, ISession session, string titlePrefix) {
    var popup = new IRDocumentPopup(position, width, height, owner, session);

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

  private async Task InitializeFromDocument(IRDocument document, string text = null) {
    await TextView.TextView.InitializeFromDocument(document, false, text);
  }

  public override void ShowPopup() {
    base.ShowPopup();

    if (PreviewedElement != null) {
      MarkPreviewedElement(PreviewedElement, TextView.TextView);
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

  public override bool ShouldStartDragging(MouseButtonEventArgs e) {
    if (e.LeftButton == MouseButtonState.Pressed && ToolbarPanel.IsMouseOver) {
      if (!IsDetached) {
        DetachPopup();
        EnableVerticalScrollbar();
        Session.RegisterDetachedPanel(this);
      }

      return true;
    }

    return false;
  }

  public void AdjustVerticalPosition(double amount) {
    // Make scroll bar visible, it's not by default.
    EnableVerticalScrollbar();

    amount *= TextView.TextView.DefaultLineHeight;
    double newOffset = TextView.TextView.VerticalOffset + amount;
    TextView.TextView.ScrollToVerticalOffset(newOffset);
  }

  private void Owner_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
    if (Utils.IsControlModifierActive()) {
      double amount = Utils.IsShiftModifierActive() ? 3 : 1;
      AdjustVerticalPosition(e.Delta < 0 ? amount : -amount);
      e.Handled = true;
    }
  }

  private void OnPropertyChange(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    ClosePopup();
  }

  private void TextView_OnMouseWheel(object sender, MouseWheelEventArgs e) {
    EnableVerticalScrollbar();
  }

  private void EnableVerticalScrollbar() {
    TextView.TextView.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    ScrollViewer.SetVerticalScrollBarVisibility(TextView, ScrollBarVisibility.Auto);
  }

  private async void OpenButton_Click(object sender, RoutedEventArgs e) {
    var args = new OpenSectionEventArgs(TextView.TextView.Section, OpenSectionKind.NewTabDockRight);

    //? TODO: BeginInvoke prevents the "Dispatcher suspended" assert that happens
    // with profiling, when Source panel shows the open dialog.
    // The dialog code should rather invoke the dispatcher...
    await Dispatcher.BeginInvoke(async () => {
      var result = await Session.OpenDocumentSectionAsync(args);

      //? TODO: Mark the previewed elem in the new doc
      // var similarValueFinder = new SimilarValueFinder(function_);
      // refElement = similarValueFinder.Find(instr);
      result.TextView.ScrollToVerticalOffset(TextView.TextView.VerticalOffset);
    }, DispatcherPriority.Background);
  }
}

//? TODO: Replace all places using IRDocumentPopup with this,
//? removes lots of duplicate code this way
public class IRDocumentPopupInstance {
  public const double DefaultWidth = 600;
  public const double DefaultHeight = 200;
  private IRDocumentPopup previewPopup_;
  private DelayedAction removeHoveredAction_;
  private Func<PreviewPopupArgs> previewedElementFinder_;
  private double width_;
  private double height_;
  private ISession session_;

  public IRDocumentPopupInstance(double width, double height, ISession session) {
    width_ = width;
    height_ = height;
    session_ = session;
  }

  public void SetupHoverEvents(UIElement target, TimeSpan hoverDuration,
                               Func<PreviewPopupArgs> previewedElementFinder) {
    previewedElementFinder_ = previewedElementFinder;
    var hover = new MouseHoverLogic(target, hoverDuration);
    hover.MouseHover += Hover_MouseHover;
    hover.MouseHoverStopped += Hover_MouseHoverStopped;
  }

  private async Task ShowPreviewPopupForDocument(IRDocument document, IRElement element,
                                                 UIElement relativeElement, string title) {
    await ShowPreviewPopupForDocument(document, element, relativeElement, width_, height_, title);
  }

  private async Task ShowPreviewPopupForDocument(IRDocument document, IRElement element, UIElement relativeElement,
                                                 double width, double height, string titlePrefix) {
    if (!Prepare(element)) {
      return;
    }

    var position = Mouse.GetPosition(relativeElement).AdjustForMouseCursor();
    previewPopup_ = await IRDocumentPopup.CreateNew(document, element, position, width, height,
                                                    relativeElement, titlePrefix);
    Complete();
  }

  public async Task ShowPreviewPopupForSection(ParsedIRTextSection parsedSection,
                                               UIElement relativeElement, string title) {
    await ShowPreviewPopupForSection(parsedSection, relativeElement, width_, height_, title);
  }

  public async Task ShowPreviewPopupForSection(ParsedIRTextSection parsedSection, UIElement relativeElement,
                                               double width, double height, string title) {
    if (!Prepare()) {
      return;
    }

    var position = Mouse.GetPosition(relativeElement).AdjustForMouseCursor();
    previewPopup_ = await IRDocumentPopup.CreateNew(parsedSection, position, width, height,
                                                    relativeElement, session_, title);
    Complete();
  }

  public async Task ShowPreviewPopupForSection(IRTextSection section,
                                               UIElement relativeElement, string title) {
    await ShowPreviewPopupForSection(section, relativeElement, width_, height_, title);
  }

  public async Task ShowPreviewPopupForSection(IRTextSection section, UIElement relativeElement,
                                               double width, double height, string title = "") {
    if (!Prepare()) {
      return;
    }

    var parsedSection = await Task.Run(() => session_.LoadAndParseSection(section));

    if (parsedSection != null) {
      var position = Mouse.GetPosition(relativeElement).AdjustForMouseCursor();
      previewPopup_ = await IRDocumentPopup.CreateNew(parsedSection, position, width, height,
                                                      relativeElement, session_, title);
      Complete();
    }
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
    await ShowPreviewPopup(result);
  }

  public async Task ShowPreviewPopup(PreviewPopupArgs args) {
    if (args == null) {
      return;
    }

    if (args.Document != null) {
      await ShowPreviewPopupForDocument(args.Document, args.Element, args.RelativeElement, args.Title);
    }
    else if (args.Section != null) {
      await ShowPreviewPopupForSection(args.Section, args.RelativeElement, args.Title);
    }
    else if (args.LoadedSection != null) {
      await ShowPreviewPopupForSection(args.LoadedSection, args.RelativeElement, args.Title);
    }
    else {
      throw new InvalidOperationException();
    }
  }

  private void Hover_MouseHoverStopped(object sender, MouseEventArgs e) {
    HidePreviewPopupDelayed();
  }
}

public class PreviewPopupArgs {
  public static PreviewPopupArgs ForDocument(IRDocument document, IRElement element,
                                             UIElement relativeElement, string title = "") {
    return new PreviewPopupArgs {
      Element = element,
      Document = document,
      RelativeElement = relativeElement,
      Title = title
    };
  }

  public static PreviewPopupArgs ForSection(IRTextSection section,
                                            UIElement relativeElement, string title = "") {
    return new PreviewPopupArgs {
      Section = section,
      RelativeElement = relativeElement,
      Title = title
    };
  }

  public static PreviewPopupArgs ForFunction(IRTextFunction function,
                                             UIElement relativeElement, string title = "") {
    if (function == null || function.Sections.Count == 0) {
      return null;
    }

    return new PreviewPopupArgs {
      Section = function.Sections[0],
      RelativeElement = relativeElement,
      Title = title
    };
  }

  public static PreviewPopupArgs ForSLoadedSection(ParsedIRTextSection section,
                                                   UIElement relativeElement, string title = "") {
    return new PreviewPopupArgs {
      LoadedSection = section,
      RelativeElement = relativeElement,
      Title = title
    };
  }

  public string Title { get; set; }
  public IRElement Element { get; set; }
  public UIElement RelativeElement { get; set; }
  public IRDocument Document { get; set; }
  public ParsedIRTextSection LoadedSection { get; set; }
  public IRTextSection Section { get; set; }
}
