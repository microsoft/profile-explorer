﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DocumentFormat.OpenXml.Drawing.Charts;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Document;
using MouseHoverLogic = IRExplorerUI.Utilities.UI.MouseHoverLogic;

namespace IRExplorerUI.Controls;

public partial class IRDocumentPopup : DraggablePopup, INotifyPropertyChanged {
  public static readonly double MinWidth = 300;
  public static readonly double MinHeight = 200; // For ASM/source preview.
  private string panelTitle_;
  private string panelToolTip_;
  private UIElement owner_;
  private bool showSourceFile_;
  private bool showModeButtons_;
  private ISession session;
  private ParsedIRTextSection parsedSection_;
  private PreviewPopupSettings settings_;

  public IRDocumentPopup(Point position, UIElement owner, ISession session, PreviewPopupSettings settings) {
    Debug.Assert(settings != null);
    InitializeComponent();

    settings_ = settings;
    double width = Math.Max(settings.PopupWidth, MinWidth);
    double height = Math.Max(settings.PopupHeight, MinHeight);
    Initialize(position, width, height, owner);

    ProfileTextView.PreviewMouseWheel += ProfileTextViewOnMouseWheel;
    PanelResizeGrip.ResizedControl = this;
    Session = session;
    owner_ = owner;
    DataContext = this;
  }

  protected override void SetPanelAccentColor(Color color) {
    ToolbarPanel.Background = ColorBrushes.GetBrush(color);
    PanelBorder.BorderBrush = ColorBrushes.GetBrush(color);
  }

  public event PropertyChangedEventHandler PropertyChanged;
  public IRElement PreviewedElement { get; set; }

  public ISession Session {
    get => session;
    set {
      session = value;
      ProfileTextView.Session = value;
    }
  }

  public string PanelTitle {
    get => panelTitle_;
    set => SetField(ref panelTitle_, value);
  }

  public string PanelToolTip {
    get => panelToolTip_;
    set => SetField(ref panelToolTip_, value);
  }

  public bool ShowAssembly {
    get => !showSourceFile_;
    set {
      SetField(ref showSourceFile_, !value);
      OnPropertyChanged(nameof(ShowSourceFile));
    }
  }

  public bool ShowSourceFile {
    get => showSourceFile_;
    set {
      SetField(ref showSourceFile_, value);
      OnPropertyChanged(nameof(ShowAssembly));
    }
  }

  public bool ShowModeButtons {
    get => showModeButtons_;
    set => SetField(ref showModeButtons_, value);
  }

  private async Task SetupInitialMode(ParsedIRTextSection parsedSection) {
    parsedSection_ = parsedSection;
    ShowModeButtons = true;
    ShowAssembly = !settings_.ShowSourcePreviewPopup;

    if(!ShowAssembly) { // Be default assembly is displayed.
      await SwitchAssemblySourceMode();
    }

    if (settings_.JumpToHottestElement) {
      ProfileTextView.JumpToHottestProfiledElement();
    }
  }

  public static async Task<IRDocumentPopup> CreateNew(IRDocument document, IRElement previewedElement,
                                                      Point position, UIElement owner,
                                                      PreviewPopupSettings settings,
                                                      string titlePrefix = "") {
    var popup = CreatePopup(document.Section, previewedElement, position,
                            owner ?? document.TextArea.TextView, document.Session, settings, titlePrefix);
    await popup.InitializeFromDocument(document);
    popup.CaptureMouseWheel();
    return popup;
  }

  public static async Task<IRDocumentPopup> CreateNew(ParsedIRTextSection parsedSection,
                                                      Point position, 
                                                      UIElement owner, ISession session,
                                                      PreviewPopupSettings settings,
                                                      string titlePrefix = "") {
    var popup = CreatePopup(parsedSection.Section, null, position,
                            owner, session, settings, titlePrefix);
    await popup.InitializeFromSection(parsedSection);
    popup.PopupClosed += (sender, args) => {
      settings.PopupWidth = popup.Width;
      settings.PopupHeight = popup.Height;
    };

    popup.CaptureMouseWheel();
    return popup;
  }

  private async Task InitializeFromSection(ParsedIRTextSection parsedSection) {
    ProfileTextView.UseSmallerFontSize = settings_.UseSmallerFontSize;
    ProfileTextView.UseCompactProfilingColumns = settings_.UseCompactProfilingColumns;
    ProfileTextView.InitializeDocument(App.Settings.DocumentSettings);
    await ProfileTextView.LoadSection(parsedSection);
    await SetupInitialMode(parsedSection);
  }

  private static IRDocumentPopup CreatePopup(IRTextSection section, IRElement previewedElement,
                                             Point position,
                                             UIElement owner, ISession session, PreviewPopupSettings settings,
                                             string titlePrefix) {
    var popup = new IRDocumentPopup(position, owner, session, settings);

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
    await ProfileTextView.TextView.InitializeFromDocument(document, false, text);
  }

  public override void ShowPopup() {
    base.ShowPopup();

    if (PreviewedElement != null) {
      MarkPreviewedElement(PreviewedElement, ProfileTextView.TextView);
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

    amount *= ProfileTextView.TextView.DefaultLineHeight;
    double newOffset = ProfileTextView.TextView.VerticalOffset + amount;
    ProfileTextView.TextView.ScrollToVerticalOffset(newOffset);
  }

  private void Owner_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
    if (Utils.IsControlModifierActive()) {
      double amount = Utils.IsShiftModifierActive() ? 3 : 1;
      AdjustVerticalPosition(e.Delta < 0 ? amount : -amount);
      e.Handled = true;
    }
  }

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
    if (EqualityComparer<T>.Default.Equals(field, value)) return false;
    field = value;
    OnPropertyChanged(propertyName);
    return true;
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    ClosePopup();
  }

  private void ProfileTextViewOnMouseWheel(object sender, MouseWheelEventArgs e) {
    EnableVerticalScrollbar();
  }

  private void EnableVerticalScrollbar() {
    ProfileTextView.TextView.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    ScrollViewer.SetVerticalScrollBarVisibility(ProfileTextView, ScrollBarVisibility.Auto);
  }

  private async void OpenButton_Click(object sender, RoutedEventArgs e) {
    var args = new OpenSectionEventArgs(ProfileTextView.TextView.Section, OpenSectionKind.NewTabDockRight);

    //? TODO: BeginInvoke prevents the "Dispatcher suspended" assert that happens
    // with profiling, when Source panel shows the open dialog.
    // The dialog code should rather invoke the dispatcher...
    await Dispatcher.BeginInvoke(async () => {
      var result = await Session.OpenDocumentSectionAsync(args);

      //? TODO: Mark the previewed elem in the new doc
      // var similarValueFinder = new SimilarValueFinder(function_);
      // refElement = similarValueFinder.Find(instr);
      result.TextView.ScrollToVerticalOffset(ProfileTextView.TextView.VerticalOffset);
    }, DispatcherPriority.Background);
  }

  private async void ModeToggleButton_Click(object sender, RoutedEventArgs e) {
    await SwitchAssemblySourceMode();
  }

  private async Task SwitchAssemblySourceMode() {
    ProfileTextView.Reset();

    if (showSourceFile_) {
      ProfileTextView.InitializeDocument(App.Settings.SourceFileSettings);
      var function = parsedSection_.Section.ParentFunction;
      var (sourceInfo, debugInfo) = await Session.CompilerInfo.SourceFileFinder.FindLocalSourceFile(function);

      if (!sourceInfo.IsUnknown) {
        await ProfileTextView.LoadSourceFile(sourceInfo, parsedSection_.Section, debugInfo);
      }
      else {
        var failureText = $"Could not find debug info for function:\n{function.Name}";
        ProfileTextView.HandleMissingSourceFile(failureText);
      }
    }
    else {
      // Show assembly.
      ProfileTextView.InitializeDocument(App.Settings.DocumentSettings);
      await ProfileTextView.LoadSection(parsedSection_);
    }
  }
}

//? TODO: Replace all places using IRDocumentPopup with this,
//? removes lots of duplicate code this way
public class IRDocumentPopupInstance {
  public const double DefaultWidth = 600;
  public const double DefaultHeight = 200;
  private PreviewPopupSettings settings_;
  private IRDocumentPopup previewPopup_;
  private DelayedAction removeHoveredAction_;
  private Func<PreviewPopupArgs> previewedElementFinder_;
  private MouseHoverLogic hover_;
  private double width_;
  private double height_;
  private ISession session_;

  public IRDocumentPopupInstance(PreviewPopupSettings settings, ISession session) {
    settings_ = settings;
    width_ = settings != null ? Math.Max(settings.PopupWidth, DefaultWidth) : DefaultWidth;
    height_ = settings != null ? Math.Max(settings.PopupHeight, DefaultHeight) : DefaultHeight;
    session_ = session;
  }

  public void SetupHoverEvents(UIElement target, TimeSpan hoverDuration,
                               Func<PreviewPopupArgs> previewedElementFinder) {
    previewedElementFinder_ = previewedElementFinder;
    hover_ = new MouseHoverLogic(target, hoverDuration);
    hover_.MouseHover += Hover_MouseHover;
    hover_.MouseHoverStopped += Hover_MouseHoverStopped;
  }

  public void UnregisterHoverEvents() {
    hover_.MouseHover -= Hover_MouseHover;
    hover_.MouseHoverStopped -= Hover_MouseHoverStopped;
    hover_.Dispose();
    hover_ = null;
  }

  private async Task ShowPreviewPopupForDocument(PreviewPopupArgs args) {
    await ShowPreviewPopupForDocument(args.Document, args.Element, args.RelativeElement, args.Title);
  }

  private async Task ShowPreviewPopupForDocument(IRDocument document, IRElement element, UIElement relativeElement,
                                                 string titlePrefix) {
    if (!Prepare(element)) {
      return;
    }

    var position = Mouse.GetPosition(relativeElement).AdjustForMouseCursor();
    previewPopup_ = await IRDocumentPopup.CreateNew(document, element, position,
                                                    relativeElement, settings_, titlePrefix);
    Complete();
  }

  private async Task ShowPreviewPopupForLoadedSection(PreviewPopupArgs args) {
    await ShowPreviewPopupForSection(args.LoadedSection, args.RelativeElement, args.Title);
  }

  private async Task ShowPreviewPopupForSection(ParsedIRTextSection parsedSection, UIElement relativeElement,
                                                string title) {
    if (!Prepare()) {
      return;
    }

    var position = Mouse.GetPosition(relativeElement).AdjustForMouseCursor();
    previewPopup_ = await IRDocumentPopup.CreateNew(parsedSection, position,
                                                    relativeElement, session_, settings_, title);
    Complete();
  }

  private async Task ShowPreviewPopupForSection(PreviewPopupArgs args) {
    await ShowPreviewPopupForSection(args.Section, args.RelativeElement, args.Title);
  }

  private async Task ShowPreviewPopupForSection(IRTextSection section, UIElement relativeElement, string title) {
    if (!Prepare()) {
      return;
    }

    var parsedSection = await Task.Run(() => session_.LoadAndParseSection(section));

    if (parsedSection != null) {
      var position = Mouse.GetPosition(relativeElement).AdjustForMouseCursor();
      previewPopup_ = await IRDocumentPopup.CreateNew(parsedSection, position,
                                                      relativeElement, session_, settings_, title);
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

    if (result != null) {
      await ShowPreviewPopup(result);
    }
  }

  private async Task ShowPreviewPopup(PreviewPopupArgs args) {
    if (args.Document != null) {
      await ShowPreviewPopupForDocument(args);
    }
    else if (args.Section != null) {
      await ShowPreviewPopupForSection(args);
    }
    else if (args.LoadedSection != null) {
      await ShowPreviewPopupForLoadedSection(args);
    }
    else {
      throw new InvalidOperationException();
    }
  }

  public static async Task ShowPreviewPopup(IRTextFunction function, string title,
                                            UIElement relativeElement, ISession session) {
    var settings = App.Settings.PreviewPopupSettings;
    var instance = new IRDocumentPopupInstance(settings, session);
    var args = PreviewPopupArgs.ForFunction(function, relativeElement, title);
    await instance.ShowPreviewPopup(args);
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
                                            UIElement relativeElement, string title = "",
                                            bool showAssembly = true) {
    return new PreviewPopupArgs {
      Section = section,
      RelativeElement = relativeElement,
      Title = title,
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
      Title = title,
    };
  }

  public static PreviewPopupArgs ForLoadedSection(ParsedIRTextSection section,
                                                   UIElement relativeElement, string title = "") {
    return new PreviewPopupArgs {
      LoadedSection = section,
      RelativeElement = relativeElement,
      Title = title,
    };
  }

  public string Title { get; set; }
  public IRElement Element { get; set; }
  public UIElement RelativeElement { get; set; }
  public IRDocument Document { get; set; }
  public ParsedIRTextSection LoadedSection { get; set; }
  public IRTextSection Section { get; set; }
}