// Copyright (c) Microsoft Corporation
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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Document;
using IRExplorerUI.OptionsPanels;
using IRExplorerUI.Profile;
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
  private bool showHistoryButtons_;
  private OptionsPanelHostPopup optionsPanelPopup_;

  public IRDocumentPopup(Point position, UIElement owner, ISession session, PreviewPopupSettings settings) {
    Debug.Assert(settings != null);
    InitializeComponent();
    SetupEvents();

    settings_ = settings;
    double width = Math.Max(settings.PopupWidth, MinWidth);
    double height = Math.Max(settings.PopupHeight, MinHeight);
    Initialize(position, width, height, owner);

    PanelResizeGrip.ResizedControl = this;
    Session = session;
    owner_ = owner;
    DataContext = this;
  }

  private void SetupEvents() {
    ProfileTextView.PreviewMouseWheel += ProfileTextViewOnMouseWheel;
    ProfileTextView.TitlePrefixChanged += (sender, s) => {
      TitlePrefix = s;
      UpdatePopupTitle();
    };
    ProfileTextView.TitleSuffixChanged += (sender, s) => {
      TitleSuffix = s;
      UpdatePopupTitle();
    };
    ProfileTextView.DescriptionPrefixChanged += (sender, s) => {
      DescriptionPrefix = !string.IsNullOrEmpty(s) ? s + "\n\n" : "";
      UpdatePopupTitle();
    };
    ProfileTextView.DescriptionSuffixChanged += (sender, s) => {
      DescriptionSuffix = !string.IsNullOrEmpty(s) ? "\n\n" + s : "";
      UpdatePopupTitle();
    };
    ProfileTextView.LoadedFunctionChanged += (sender, s) => {
      parsedSection_ = s;
      UpdatePopupTitle();
    };
    ProfileTextView.FunctionHistoryChanged += (sender, args) => {
      ShowHistoryButtons = ProfileTextView.HasPreviousFunctions ||
                           ProfileTextView.HasNextFunctions;
      OnPropertyChanged(nameof(HasNextFunctions));
      OnPropertyChanged(nameof(HasPreviousFunctions));
    };
  }

  protected override void SetPanelAccentColor(Color color) {
    ToolbarPanel.Background = ColorBrushes.GetBrush(color);
    PanelBorder.BorderBrush = ColorBrushes.GetBrush(color);
  }

  public void SetPanelAccentColor(Brush color) {
    ToolbarPanel.Background = color;
    PanelBorder.BorderBrush = color;
  }

  public event PropertyChangedEventHandler PropertyChanged;
  public IRElement PreviewedElement { get; set; }
  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;

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

  public string TitlePrefix { get; set; }
  public string TitleSuffix { get; set; }
  public string DescriptionPrefix { get; set; }
  public string DescriptionSuffix { get; set; }

  public bool ShowHistoryButtons {
    get => showHistoryButtons_;
    set => SetField(ref showHistoryButtons_, value);
  }

  public bool HasPreviousFunctions => ProfileTextView.HasPreviousFunctions;
  public bool HasNextFunctions => ProfileTextView.HasNextFunctions;

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

  private async Task SetupInitialMode(ParsedIRTextSection parsedSection, bool showSourceCode) {
    parsedSection_ = parsedSection;
    await ReloadDocument(showSourceCode);
  }

  private async Task ReloadDocument(bool showSourceCode = false) {
    if (parsedSection_ != null) {
      ShowModeButtons = true;
      ShowAssembly = !showSourceCode && !settings_.ShowSourcePreviewPopup;
      await SwitchAssemblySourceMode();
    }
  }

  public static async Task<IRDocumentPopup> CreateNew(IRDocument document, IRElement previewedElement,
                                                      Point position, UIElement owner,
                                                      PreviewPopupSettings settings,
                                                      string titlePrefix = "") {
    var popup = CreatePopup(document.Section, previewedElement, position,
                            owner ?? document.TextArea.TextView, document.Session, settings, titlePrefix);
    await popup.InitializeFromDocument(document);
    SetupNewPopup(popup, settings);
    return popup;
  }

  public static async Task<IRDocumentPopup> CreateNew(ParsedIRTextSection parsedSection,
                                                      Point position, UIElement owner,
                                                      ISession session,
                                                      PreviewPopupSettings settings,
                                                      string titlePrefix = "",
                                                      bool showSourceCode = false,
                                                      ProfileSampleFilter profileFilter = null) {
    var popup = CreatePopup(parsedSection.Section, null, position,
                            owner, session, settings, titlePrefix);
    await popup.InitializeFromSection(parsedSection, profileFilter, showSourceCode);
    SetupNewPopup(popup, settings);
    return popup;
  }

  private static void SetupNewPopup(IRDocumentPopup popup, PreviewPopupSettings settings) {
    popup.PopupClosed += (sender, args) => {
      // Save resized popup dimension for next use.
      settings.PopupWidth = popup.Width;
      settings.PopupHeight = popup.Height;
    };

    popup.UpdatePopupTitle();
    popup.CaptureMouseWheel();
  }

  private async Task InitializeFromSection(ParsedIRTextSection parsedSection,
                                           ProfileSampleFilter filter,
                                           bool showSourceCode) {
    ReloadSettings();
    ProfileTextView.ProfileFilter = filter;
    ProfileTextView.Focus();
    await SetupInitialMode(parsedSection, showSourceCode);
  }

  private void ReloadSettings() {
    ProfileTextView.IsPreviewDocument = true;
    ProfileTextView.UseSmallerFontSize = settings_.UseSmallerFontSize;
    ProfileTextView.UseCompactProfilingColumns = settings_.UseCompactProfilingColumns;
    ProfileTextView.ShowPerformanceCounterColumns = settings_.ShowPerformanceCounterColumns;
    ProfileTextView.ShowPerformanceMetricColumns = settings_.ShowPerformanceMetricColumns;
    ProfileTextView.Initialize(App.Settings.DocumentSettings);
  }

  private static IRDocumentPopup CreatePopup(IRTextSection section, IRElement previewedElement,
                                             Point position, UIElement owner, ISession session,
                                             PreviewPopupSettings settings, string titlePrefix) {
    var popup = new IRDocumentPopup(position, owner, session, settings);
    popup.PreviewedElement = previewedElement;
    popup.TitlePrefix = titlePrefix;
    SetupNewPopup(popup, settings);
    return popup;
  }

  private void UpdatePopupTitle() {
    string title = GetFunctionName();

    if (PreviewedElement != null) {
      string elementText = Utils.MakeElementDescription(PreviewedElement);
      title = $"{title}: {elementText}";
    }

    if (!string.IsNullOrEmpty(TitlePrefix)) {
      title = $"{TitlePrefix}{title}";
    }

    if (!string.IsNullOrEmpty(TitleSuffix)) {
      title = $"{title}{TitleSuffix}";
    }

    string tooltip = GetTooltipFunctionName();

    if (!string.IsNullOrEmpty(DescriptionPrefix)) {
      tooltip = $"{DescriptionPrefix}{tooltip}";
    }

    if (!string.IsNullOrEmpty(DescriptionSuffix)) {
      tooltip = $"{tooltip}{DescriptionSuffix}";
    }

    PanelTitle = title;
    PanelToolTip = tooltip;
  }

  private string GetFunctionName() {
    if (parsedSection_ != null) {
      return parsedSection_.ParentFunction.FormatFunctionName(session, 80);
    }

    return "";
  }

  private string GetTooltipFunctionName() {
    if (parsedSection_ != null) {
      string funName = parsedSection_.ParentFunction.FormatFunctionName(session);
      return $"Module: {parsedSection_.Section.ModuleName}\nFunction: {DocumentUtils.FormatLongFunctionName(funName)}";
    }

    return "";
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

  public override void PopupOpened() {
    ProfileTextView.Focus();
  }

  public override void ClosePopup() {
    owner_.PreviewMouseWheel -= Owner_OnPreviewMouseWheel;
    Session.UnregisterDetachedPanel(this);
    base.ClosePopup();
  }

  private void MarkPreviewedElement(IRElement element, IRDocument document) {
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

  private void CaptureMouseWheel() {
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
    }, DispatcherPriority.Render);
  }

  private async void ModeToggleButton_Click(object sender, RoutedEventArgs e) {
    await SwitchAssemblySourceMode();
  }

  private async Task SwitchAssemblySourceMode() {
    // Save current profile filter to restore after switch.
    var filter = ProfileTextView.ProfileFilter;
    await ProfileTextView.Reset();

    if (showSourceFile_) {
      ProfileTextView.Initialize(App.Settings.SourceFileSettings);
      var function = parsedSection_.ParentFunction;
      var sourceFileFinder = new SourceFileFinder(Session);
      sourceFileFinder.LoadSettings(App.Settings.SourceFileSettings.FinderSettings);
      var (sourceInfo, debugInfo) =
        await sourceFileFinder.FindLocalSourceFile(function);
      bool loaded = false;

      if (!sourceInfo.IsUnknown) {
        loaded = await ProfileTextView.LoadSourceFile(sourceInfo, parsedSection_.Section, filter);
      }

      if (!loaded) {
        string failureText = $"Could not find debug info for function:\n{function.Name}";
        await ProfileTextView.HandleMissingSourceFile(failureText);
      }
    }
    else {
      // Show assembly.
      ProfileTextView.Initialize(App.Settings.DocumentSettings);
      await ProfileTextView.LoadAssembly(parsedSection_, filter);
    }
  }

  private async void NextButton_Click(object sender, RoutedEventArgs e) {
    await ProfileTextView.LoadNextSection();
  }

  private async void BackButton_Click(object sender, RoutedEventArgs e) {
    await ProfileTextView.LoadPreviousSection();
  }

  private void OptionButton_Click(object sender, RoutedEventArgs e) {
    ShowOptionsPanel();
  }

  private void ShowOptionsPanel() {
    if (optionsPanelPopup_ != null) {
      optionsPanelPopup_.ClosePopup();
      optionsPanelPopup_ = null;
      return;
    }

    FrameworkElement relativeControl = ProfileTextView;
    optionsPanelPopup_ = OptionsPanelHostPopup.Create<PreviewPopupOptionsPanel, PreviewPopupSettings>(
      settings_.Clone(), relativeControl, Session,
      async (newSettings, commit) => {
        if (!newSettings.Equals(settings_)) {
          settings_ = newSettings;
          App.Settings.PreviewPopupSettings = newSettings;
          ReloadSettings();
          await ReloadDocument();

          if (commit) {
            App.SaveApplicationSettings();
          }

          return settings_.Clone();
        }

        return null;
      },
      () => optionsPanelPopup_ = null);
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
    if (!Prepare()) {
      return;
    }

    var position = Mouse.GetPosition(args.RelativeElement).AdjustForMouseCursor();
    previewPopup_ = await IRDocumentPopup.CreateNew(args.LoadedSection, position,
                                                    args.RelativeElement, session_,
                                                    settings_, args.Title,
                                                    args.ShowSourceCode, args.ProfilerFilter);
    Complete();
  }

  private async Task ShowPreviewPopupForSection(PreviewPopupArgs args) {
    if (!Prepare()) {
      return;
    }

    var parsedSection = await session_.LoadAndParseSection(args.Section);

    if (parsedSection != null) {
      var position = Mouse.GetPosition(args.RelativeElement).AdjustForMouseCursor();
      previewPopup_ = await IRDocumentPopup.CreateNew(parsedSection, position,
                                                      args.RelativeElement, session_,
                                                      settings_, args.Title,
                                                      args.ShowSourceCode, args.ProfilerFilter);
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

  private async Task<IRDocumentPopup> ShowPreviewPopup(PreviewPopupArgs args) {
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

    return previewPopup_;
  }

  public static async Task ShowPreviewPopup(IRTextFunction function, string title,
                                            UIElement relativeElement, ISession session,
                                            ProfileSampleFilter profileFilter = null,
                                            bool showSourceCode = false,
                                            Brush titleBarColor = null) {
    var settings = App.Settings.PreviewPopupSettings;
    var instance = new IRDocumentPopupInstance(settings, session);
    var args = PreviewPopupArgs.ForFunction(function, relativeElement, title, profileFilter, showSourceCode);
    var popup = await instance.ShowPreviewPopup(args);

    if (titleBarColor != null) {
      popup.SetPanelAccentColor(titleBarColor);
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
                                            UIElement relativeElement,
                                            string title = "",
                                            ProfileSampleFilter profileFilter = null,
                                            bool showSourceCode = false) {
    return new PreviewPopupArgs {
      Section = section,
      RelativeElement = relativeElement,
      Title = title,
      ProfilerFilter = profileFilter,
      ShowSourceCode = showSourceCode,
    };
  }

  public static PreviewPopupArgs ForFunction(IRTextFunction function,
                                             UIElement relativeElement,
                                             string title = "",
                                             ProfileSampleFilter profileFilter = null,
                                             bool showSourceCode = false) {
    if (function == null || function.Sections.Count == 0) {
      return null;
    }

    return new PreviewPopupArgs {
      Section = function.Sections[0],
      RelativeElement = relativeElement,
      Title = title,
      ProfilerFilter = profileFilter,
      ShowSourceCode = showSourceCode,
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
  public bool ShowSourceCode { get; set; }
  public ProfileSampleFilter ProfilerFilter { get; set; }
}