// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ICSharpCode.AvalonEdit.Rendering;
using IRExplorerUI.Document;
using IRExplorerUI.OptionsPanels;
using IRExplorerCore;
using IRExplorerCore.IR;
using ProtoBuf;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using IRExplorerUI.Query;
using IRExplorerUI.Controls;
using IRExplorerUI.Compilers.ASM;
using IRExplorerCore.IR.Tags;
using ClosedXML.Excel;
using static IRExplorerUI.ModuleReportPanel;
using System.IO;
using IRExplorerUI.Profile;

namespace IRExplorerUI {
    public static class DocumentHostCommand {
        public static readonly RoutedUICommand ShowSearch =
            new RoutedUICommand("Untitled", "ShowSearch", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ToggleSearch =
            new RoutedUICommand("Untitled", "ToggleSearch", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ShowSectionList =
            new RoutedUICommand("Untitled", "ShowSectionList", typeof(IRDocumentHost));
        public static readonly RoutedUICommand PreviousSection =
            new RoutedUICommand("Untitled", "PreviousSection", typeof(IRDocumentHost));
        public static readonly RoutedUICommand NextSection =
            new RoutedUICommand("Untitled", "NextSection", typeof(IRDocumentHost));
        public static readonly RoutedUICommand SearchSymbol =
            new RoutedUICommand("Untitled", "SearchSymbol", typeof(IRDocumentHost));
        public static readonly RoutedUICommand SearchSymbolAllSections =
            new RoutedUICommand("Untitled", "SearchSymbolAllSections", typeof(IRDocumentHost));
        public static readonly RoutedUICommand JumpToProfiledElement =
            new RoutedUICommand("Untitled", "JumpToProfiledElement", typeof(IRDocumentHost));
        public static readonly RoutedUICommand JumpToNextProfiledElement =
            new RoutedUICommand("Untitled", "JumpToNextProfiledElement", typeof(IRDocumentHost));
        public static readonly RoutedUICommand JumpToPreviousProfiledElement =
            new RoutedUICommand("Untitled", "JumpToPreviousProfiledElement", typeof(IRDocumentHost));
        public static readonly RoutedUICommand JumpToNextProfiledBlock =
            new RoutedUICommand("Untitled", "JumpToNextProfiledBlock", typeof(IRDocumentHost));
        public static readonly RoutedUICommand JumpToPreviousProfiledBlock =
            new RoutedUICommand("Untitled", "JumpToPreviousProfiledBlock", typeof(IRDocumentHost));
        public static readonly RoutedUICommand ExportFunctionProfile =
            new RoutedUICommand("Untitled", "ExportFunctionProfile", typeof(IRDocumentHost));
    }

    [ProtoContract]
    public class IRDocumentHostState {
        [ProtoMember(1)]
        public IRDocumentState DocumentState;
        [ProtoMember(2)]
        public double HorizontalOffset;
        [ProtoMember(3)]
        public double VerticalOffset;

        public bool HasAnnotations => DocumentState.HasAnnotations;
    }

    class RemarksButtonState : INotifyPropertyChanged {
        private RemarkSettings remarkSettings_;

        public RemarksButtonState(RemarkSettings settings) {
            remarkSettings_ = settings.Clone();
        }

        public RemarkSettings Settings {
            get {
                return remarkSettings_;
            }
            set {
                if (!value.Equals(remarkSettings_)) {
                    NotifyPropertyChanged(nameof(ShowRemarks));
                    NotifyPropertyChanged(nameof(ShowPreviousSections));
                }

                remarkSettings_ = value.Clone();
            }
        }

        public bool ShowRemarks {
            get {
                return remarkSettings_.ShowRemarks;
            }
            set {
                if (value != remarkSettings_.ShowRemarks) {
                    remarkSettings_.ShowRemarks = value;
                    NotifyPropertyChanged(nameof(ShowRemarks));
                }
            }
        }

        public bool ShowPreviousSections {
            get {
                return ShowRemarks && remarkSettings_.ShowPreviousSections;
            }
            set {
                if (value != remarkSettings_.ShowPreviousSections) {
                    remarkSettings_.ShowPreviousSections = value;
                    NotifyPropertyChanged(nameof(ShowPreviousSections));
                }
            }
        }

        public void NotifyPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class DummyVirtualizingStackPanel : VirtualizingStackPanel {
        protected override Size MeasureOverride(Size constraint) {
            //Trace.WriteLine($"Measure {constraint}");
            //var sw = Stopwatch.StartNew();
             
            var result = base.MeasureOverride(constraint);
            
            //sw.Stop();
            //Trace.WriteLine($"Measured  {x} in {sw.ElapsedMilliseconds}");
            //Trace.Flush(;
           
            return result;
        }

        protected override void OnChildDesiredSizeChanged(UIElement el) {
            //? Workaround for WPF measuring bug, see link
            //? https://stackoverflow.com/questions/11696008/performance-issue-with-measure
            /* base.OnChildDesiredSizeChanged(el); */       // avoid rampant remeasuring
        }
    }

    public class ProfiledBlockEx : BindableObject {
        public ProfiledBlockEx(string text, long value = 0, double valueValuePercentage = 0.0) {
            Text = text;
            Value = value;
            ValuePercentage = valueValuePercentage;
            TextWeight = FontWeights.Normal;
            TextColor = Brushes.Black;
        }

        public static ElementColumnValue Empty => new ElementColumnValue(string.Empty);

        public IRElement Element { get; set; }
        public long Value { get; set; }
        public double ValuePercentage { get; set; }

        private Thickness borderThickness_;
        public Thickness BorderThickness {
            get => borderThickness_;
            set => SetAndNotify(ref borderThickness_, value);
        }

        private Brush borderBrush_;
        public Brush BorderBrush {
            get => borderBrush_;
            set => SetAndNotify(ref borderBrush_, value);
        }

        private string text_;
        public string Text {
            get => text_;
            set => SetAndNotify(ref text_, value);
        }

        private string prefixText_;
        public string PrefixText {
            get => prefixText_;
            set => SetAndNotify(ref prefixText_, value);
        }

        private double minTextWidth_;
        public double MinTextWidth {
            get => minTextWidth_;
            set => SetAndNotify(ref minTextWidth_, value);
        }

        private string toolTip_;
        public string ToolTip {
            get => toolTip_;
            set => SetAndNotify(ref toolTip_, value);
        }

        private Brush textColor_;

        public Brush TextColor {
            get => textColor_;
            set => SetAndNotify(ref textColor_, value);
        }

        private Brush backColor_;

        public Brush BackColor {
            get => backColor_;
            set => SetAndNotify(ref backColor_, value);
        }

        private ImageSource icon_;

        public ImageSource Icon {
            get => icon_;
            set {
                SetAndNotify(ref icon_, value);
                Notify(nameof(ShowIcon));
            }
        }

        public bool ShowIcon => icon_ != null;

        private bool showPercentageBar_;
        public bool ShowPercentageBar {
            get => showPercentageBar_;
            set => SetAndNotify(ref showPercentageBar_, value);
        }

        private Brush percentageBarBackColor__;
        public Brush PercentageBarBackColor {
            get => percentageBarBackColor__;
            set => SetAndNotify(ref percentageBarBackColor__, value);
        }

        private double percentageBarBorderThickness_;
        public double PercentageBarBorderThickness {
            get => percentageBarBorderThickness_;
            set => SetAndNotify(ref percentageBarBorderThickness_, value);
        }

        private Brush percentageBarBorderBrush_;
        public Brush PercentageBarBorderBrush {
            get => percentageBarBorderBrush_;
            set => SetAndNotify(ref percentageBarBorderBrush_, value);
        }

        private FontWeight textWeight_;

        public FontWeight TextWeight {
            get => textWeight_;
            set => SetAndNotify(ref textWeight_, value);
        }

        private double textSize_;
        public double TextSize {
            get => textSize_;
            set => SetAndNotify(ref textSize_, value);
        }

        private FontFamily textFont_;
        public FontFamily TextFont {
            get => textFont_;
            set => SetAndNotify(ref textFont_, value);
        }
    }

    public partial class IRDocumentHost : UserControl, INotifyPropertyChanged {
        private const double ActionPanelInitialOpacity = 0.5;
        private const int ActionPanelHeight = 20;
        private const double AnimationDuration = 0.1;
        private const int ActionPanelOffset = 15;

        private bool actionPanelHovered_;
        private bool actionPanelFromClick_;
        private bool actionPanelVisible_;
        private bool duringSwitchSearchResults_;
        private IRElement hoveredElement_;
        private Point hoverPoint_;
        private bool optionsPanelVisible_;
        private bool remarkOptionsPanelVisible_;
        private IRElement remarkElement_;
        private IRElement selectedElement_;
        private RemarkSettings remarkSettings_;
        private RemarkPreviewPanel remarkPanel_;
        private Point remarkPanelLocation_;
        private CancelableTaskInstance loadTask_;
        private bool remarkPanelVisible_;
        private bool searchPanelVisible_;
        private SectionSearchResult searchResult_;
        private IRElement selectedBlock_;
        private ISession session_;
        private DocumentSettings settings_;
        private List<Remark> remarkList_;
        private RemarkContext activeRemarkContext_;
        private List<QueryPanel> activeQueryPanels_;
        private QueryValue mainQueryInputValue_;
        private bool pasOutputVisible_;
        private bool columnsVisible_;
        private bool duringSectionSwitching_;
        private double previousVerticalOffset_;

        public IRDocumentHost(ISession session) {
            InitializeComponent();
            DataContext = this;
            PassOutput.DataContext = this;

            Session = session;
            remarkSettings_ = App.Settings.RemarkSettings;
            Settings = App.Settings.DocumentSettings;
            ActionPanel.Visibility = Visibility.Collapsed;

            // Initialize pass output panel.
            PassOutput.Session = session;
            PassOutput.HasPinButton = false;
            PassOutput.HasDuplicateButton = false;
            PassOutput.DiffModeButtonVisible = false;
            PassOutput.SectionNameVisible = false;
            PassOutput.ScrollChanged += PassOutput_ScrollChanged;
            PassOutput.ShowBeforeOutputChanged += PassOutput_ShowBeforeOutputChanged;

            PreviewKeyDown += IRDocumentHost_PreviewKeyDown;
            TextView.PreviewMouseRightButtonDown += TextView_PreviewMouseRightButtonDown;
            TextView.MouseDoubleClick += TextViewOnMouseDoubleClick;
            TextView.PreviewMouseMove += TextView_PreviewMouseMove;
            TextView.PreviewMouseDown += TextView_PreviewMouseDown;
            TextView.BlockSelected += TextView_BlockSelected;
            TextView.ElementSelected += TextView_ElementSelected;
            TextView.ElementUnselected += TextView_ElementUnselected;
            TextView.PropertyChanged += TextView_PropertyChanged;
            TextView.GotKeyboardFocus += TextView_GotKeyboardFocus;
            TextView.CaretChanged += TextViewOnCaretChanged;
            TextView.TextArea.TextView.ScrollOffsetChanged += TextViewOnScrollOffsetChanged;

            SectionPanel.OpenSection += SectionPanel_OpenSection;
            SearchPanel.SearchChanged += SearchPanel_SearchChanged;
            SearchPanel.NavigateToPreviousResult += SearchPanel_NaviateToPreviousResult;
            SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
            SearchPanel.CloseSearchPanel += SearchPanel_CloseSearchPanel;
            Unloaded += IRDocumentHost_Unloaded;

            var hover = new MouseHoverLogic(this);
            hover.MouseHover += Hover_MouseHover;
            loadTask_ = new CancelableTaskInstance();
            activeQueryPanels_ = new List<QueryPanel>();
        }

        private void TextViewOnMouseDoubleClick(object sender, MouseButtonEventArgs e) {
            if (!Utils.IsControlModifierActive()) {
                return;
            }

            SearchSymbolImpl(Utils.IsShiftModifierActive());
        }

        private void TextViewOnCaretChanged(object? sender, int offset) {
            if (columnsVisible_) {
                var line = TextView.Document.GetLineByOffset(offset);

                if (ColumnsList.Items.Count >= line.LineNumber) {
                    ColumnsList.SelectedIndex = line.LineNumber - 1;
                }
            }
        }

        private void TextViewOnScrollOffsetChanged(object? sender, EventArgs e) {
            HideActionPanel();
            DetachRemarkPanel(true);

            double offset = TextView.TextArea.TextView.VerticalOffset;
            double changeAmount = offset - previousVerticalOffset_;
            previousVerticalOffset_ = offset;

            // Sync scrolling with the optional columns.
            SyncColumnsVerticalScrollOffset(offset);
            VerticalScrollChanged?.Invoke(this, (offset, changeAmount));
        }

        private void SyncColumnsVerticalScrollOffset(double offset) {
            // Sync scrolling with the optional columns.
            if (columnsVisible_) {
                var columnScrollViewer = Utils.FindChild<ScrollViewer>(ColumnsList);

                if (columnScrollViewer != null) {
                    columnScrollViewer.ScrollToVerticalOffset(offset);
                }
            }
        }
        
        public double ColumnsListItemHeight {
            get => columnsListItemHeight_;
            set {
                if (columnsListItemHeight_ != value) {
                    columnsListItemHeight_ = value;
                    NotifyPropertyChanged(nameof(ColumnsListItemHeight));
                }
            }
        }

        private Brush selectedLineBrush_;
        public Brush SelectedLineBrush {
            get => selectedLineBrush_;
            set {
                selectedLineBrush_ = value;
                NotifyPropertyChanged(nameof(SelectedLineBrush));
            }
        }

        public void NotifyPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void HandleRemarkSettingsChange() {
            if (!remarkPanelVisible_ && !remarkOptionsPanelVisible_) {
                await HandleNewRemarkSettings(remarkSettings_, false, true);
            }
        }

        public ISession Session {
            get => session_;
            private set {
                session_ = value;
            }
        }

        public DocumentSettings Settings {
            get => settings_;
            set {
                settings_ = value;
                ReloadSettings();
            }
        }

        public RemarkSettings RemarkSettings {
            get => remarkSettings_;
            set {
                if (!value.Equals(remarkSettings_)) {
                    remarkSettings_ = value.Clone();

                    NotifyPropertyChanged(nameof(ShowRemarks));
                    NotifyPropertyChanged(nameof(ShowPreviousSections));
                }
            }
        }

        public async Task UpdateRemarkSettings(RemarkSettings newSettings) {
            RemarkSettings = newSettings;
            await HandleNewRemarkSettings(newSettings, false);
        }

        public bool ShowRemarks {
            get => remarkSettings_.ShowRemarks;
            set {
                if (value != remarkSettings_.ShowRemarks) {
                    remarkSettings_.ShowRemarks = value;
                    NotifyPropertyChanged(nameof(ShowRemarks));
                    HandleRemarkSettingsChange();
                }
            }
        }

        public bool ShowPreviousSections {
            get => ShowRemarks && remarkSettings_.ShowPreviousSections;
            set {
                if (value != remarkSettings_.ShowPreviousSections) {
                    remarkSettings_.ShowPreviousSections = value;
                    NotifyPropertyChanged(nameof(ShowPreviousSections));
                    HandleRemarkSettingsChange();
                }
            }
        }

        public IRTextSection Section => TextView.Section;
        public FunctionIR Function => TextView.Function;

        public event EventHandler<(double offset, double offsetChangeAmount)> VerticalScrollChanged;
        public event EventHandler<(double offset, double offsetChangeAmount)> PassOutputVerticalScrollChanged;
        public event EventHandler<bool> PassOutputShowBeforeChanged;
        public event EventHandler<bool> PassOutputVisibilityChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        private async void IRDocumentHost_Unloaded(object sender, RoutedEventArgs e) {
            await HideRemarkPanel(true);
        }

        private async void TextView_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            await HideRemarkPanel();

            var point = e.GetPosition(TextView.TextArea.TextView);
            var element = TextView.GetElementAt(point);

            if (element == null) {
                HideActionPanel(true);
                return;
            }
            
            if (element != hoveredElement_ && !actionPanelHovered_) {
                await ShowActionPanel(element, true);
            }

            // Middle-button click sets the input element in the active query panel.
            if(mainQueryInputValue_ != null && e.MiddleButton == MouseButtonState.Pressed) {
                mainQueryInputValue_.ForceValueUpdate(element);
                e.Handled = true;
            }
        }

        private async void TextView_PreviewMouseMove(object sender, MouseEventArgs e) {
            if (!actionPanelVisible_) {
                return;
            }

            var point = e.GetPosition(TextView.TextArea.TextView);
            var element = TextView.GetElementAt(point);

            if (!remarkPanelVisible_ && !actionPanelHovered_ && !actionPanelFromClick_) {
                if (element == null || element != hoveredElement_) {
                    HideActionPanel();
                    await HideRemarkPanel();
                }
            }
        }

        private async void Hover_MouseHover(object sender, MouseEventArgs e) {
            if (!remarkSettings_.ShowActionButtonOnHover ||
                (remarkSettings_.ShowActionButtonWithModifier && !Utils.IsKeyboardModifierActive())) {
                actionPanelHovered_ = false;
                return;
            }

            if (remarkPanelVisible_ || actionPanelHovered_) {
                return;
            }

            var point = e.GetPosition(TextView.TextArea.TextView);

            if (point.X <= 0 || point.Y <= 0) {
                // Don't consider the left margin and other elements outside the text view.
                return;
            }

            //? TODO: If other panels are opened over the document, don't consider their area.

            var element = TextView.GetElementAt(point);

            if (element != null) {
                // If the panel is already showing for this element, ignore the action
                // so that it doesn't move around after the mouse cursor.
                if (element != hoveredElement_) {
                    await ShowActionPanel(element);
                    hoveredElement_ = element;
                }
            }
            else {
                HideActionPanel();
                hoveredElement_ = null;
            }
        }

        private async void TextView_ElementUnselected(object sender, IRElementEventArgs e) {
            HideActionPanel(true);
            await HideRemarkPanel();
        }

        private async void TextView_ElementSelected(object sender, IRElementEventArgs e) {
            selectedElement_ = e.Element;
            await ShowActionPanel(e.Element);
        }

        private IRElement GetRemarkElement(IRElement element) {
            if (element.GetTag<RemarkTag>() != null) {
                return element;
            }

            // If it's an operand, check if the instr. has a remark instead.
            if (element is OperandIR op) {
                var instr = op.ParentTuple;

                if (instr.GetTag<RemarkTag>() != null) {
                    return instr;
                }
            }

            return null;
        }

        private async Task ShowActionPanel(IRElement element, bool fromClickEvent = false) {
            remarkElement_ = GetRemarkElement(element);
            var visualElement = remarkElement_;

            if (remarkElement_ == null) {
                await HideRemarkPanel();

                // If there are action buttons in the panel, keep showing it.
                if (!ActionPanel.HasActionButtons) {
                    HideActionPanel();
                    return;
                }

                visualElement = element;
                ActionPanel.ShowRemarksButton = false;
            }
            else {
                ActionPanel.ShowRemarksButton = true;
            }

            var visualLine = TextView.TextArea.TextView.GetVisualLine(visualElement.TextLocation.Line + 1);

            if (visualLine != null) {
                // If there is an ongoing hiding operation, cancel it since it would
                // likely hide the action panel being set up here.
                if (delayedHideActionPanel_ != null) {
                    delayedHideActionPanel_.Cancel();
                    delayedHideActionPanel_ = null;
                }

                var linePos = visualLine.GetVisualPosition(0, VisualYPosition.LineBottom);
                var t = Mouse.GetPosition(this);
                double x = Mouse.GetPosition(this).X + ActionPanelOffset;
                double y = linePos.Y + DocumentToolbar.ActualHeight -
                           1 - TextView.TextArea.TextView.ScrollOffset.Y;

                Canvas.SetLeft(ActionPanel, x);
                Canvas.SetTop(ActionPanel, y);
                ActionPanel.Opacity = 0.0;
                ActionPanel.Visibility = Visibility.Visible;

                var animation2 = new DoubleAnimation(ActionPanelInitialOpacity,
                                                     TimeSpan.FromSeconds(fromClickEvent ? 0 : AnimationDuration));
                ActionPanel.BeginAnimation(OpacityProperty, animation2,
                                            HandoffBehavior.SnapshotAndReplace);

                actionPanelFromClick_ = fromClickEvent;
                actionPanelVisible_ = true;
                remarkPanelLocation_ = new Point(x, y + ActionPanelHeight);
            }
        }

        private void HideActionPanel(bool force = false) {
            // Ignore if panel not visible or in process of being hidden.
            if (!actionPanelVisible_ || delayedHideActionPanel_ != null) {
                return;
            }

            if (force) {
                HideActionPanelImpl();
                return;
            }

            delayedHideActionPanel_ = DelayedAction.StartNew(() => {
                if (remarkPanelVisible_ || ActionPanel.IsMouseOver) {
                    return;
                }

                HideActionPanelImpl();
            });
        }

        private void HideActionPanelImpl() {
            var animation = new DoubleAnimation(0.0, TimeSpan.FromSeconds(AnimationDuration));
            animation.Completed += (s, e) => { ActionPanel.Visibility = Visibility.Collapsed; };
            ActionPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            actionPanelVisible_ = false;
            delayedHideActionPanel_ = null;
        }

        private void ShowRemarkPanel() {
            if (remarkPanelVisible_ || remarkElement_ == null) {
                return;
            }

            remarkPanel_ = new RemarkPreviewPanel();
            remarkPanel_.PopupClosed += RemarkPanel__PanelClosed;
            remarkPanel_.PopupDetached += RemarkPanel__PanelDetached;
            remarkPanel_.RemarkContextChanged += RemarkPanel__RemarkContextChanged;
            remarkPanel_.RemarkChanged += RemarkPanel__RemarkChanged;
            remarkPanel_.Opacity = 0.0;
            remarkPanel_.IsOpen = true;

            var animation = new DoubleAnimation(1.0, TimeSpan.FromSeconds(AnimationDuration));
            remarkPanel_.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            remarkPanelVisible_ = true;

            InitializeRemarkPanel(remarkElement_);
        }

        private void RemarkPanel__RemarkChanged(object sender, Remark e) {
            TextView.SelectDocumentRemark(e);
        }

        private void RemarkPanel__PanelDetached(object sender, EventArgs e) {
            // Keep the remark panel floating over the document.
            DetachRemarkPanel();
        }

        private void DetachRemarkPanel(bool notifyPanel = false) {
            if(remarkPanel_ == null) {
                return;
            }

            Session.RegisterDetachedPanel(remarkPanel_);
            HideActionPanel();

            if(notifyPanel) {
                remarkPanel_.PopupDetached -= RemarkPanel__PanelDetached;
                remarkPanel_.DetachPanel();
            }

            remarkPanelVisible_ = false;
            remarkPanel_ = null;
        }

        private async void RemarkPanel__PanelClosed(object sender, EventArgs e) {
            // If it's one of the detached panels, unregister it.
            var panel = (RemarkPreviewPanel)sender;

            if (panel.IsDetached) {
                Session.UnregisterDetachedPanel(panel);
                return;
            }

            await HideRemarkPanel();
        }

        private async void RemarkPanel__RemarkContextChanged(object sender, RemarkContextChangedEventArgs e) {
            activeRemarkContext_ = e.Context;

            if (e.Context != null && e.Remarks != null) {
                await UpdateDocumentRemarks(e.Remarks);
            }
            else {
                // Filtering of context remarks disabled.
                await UpdateDocumentRemarks(remarkList_);
            }
        }

        private void InitializeRemarkPanel(IRElement element) {
            remarkPanel_.Session = Session;
            remarkPanel_.Function = Function;
            remarkPanel_.Section = Section;
            remarkPanel_.Initialize(element, remarkPanelLocation_, this, remarkSettings_);
        }

        private async Task HideRemarkPanel(bool force = false) {
            if (!remarkPanelVisible_) {
                return;
            }

            if (force) {
                remarkPanel_.IsOpen = false;
                remarkPanel_ = null;
                return;
            }

            await ResetActiveRemarkContext();
            var animation = new DoubleAnimation(0.0, TimeSpan.FromSeconds(AnimationDuration));

            animation.Completed += (s, e) => {
                if (remarkPanel_ != null) { // When section unloads, can be before animation completes.
                    remarkPanel_.IsOpen = false;
                    remarkPanel_ = null;
                }
            };

            remarkPanel_.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            remarkPanelVisible_ = false;
        }

        private async Task ResetActiveRemarkContext() {
            if (activeRemarkContext_ != null) {
                activeRemarkContext_ = null;
                await UpdateDocumentRemarks(remarkList_);
            }
        }

        private void OptionsPanel_SettingsChanged(object sender, EventArgs e) {
            if (optionsPanelVisible_) {
                var newSettings = (DocumentSettings)optionsPanelWindow_.Settings;

                if (newSettings != null) {
                    LoadNewSettings(newSettings, optionsPanel_.SyntaxFileChanged, false);
                    optionsPanelWindow_.Settings = null;
                    optionsPanelWindow_.Settings = newSettings.Clone();
                }
            }
        }

        private void LoadNewSettings(DocumentSettings newSettings, bool force, bool commit) {
            if (force || newSettings.HasChanges(Settings)) {
                App.Settings.DocumentSettings = newSettings;
                Settings = newSettings;
            }

            if (commit) {
                Session.ReloadDocumentSettings(newSettings, TextView);
                App.SaveApplicationSettings();
            }
        }

        private void OptionsPanel_PanelReset(object sender, EventArgs e) {
            var newOptions = new DocumentSettings();
            LoadNewSettings(newOptions, true, false);
            optionsPanelWindow_.Settings = null;
            optionsPanelWindow_.Settings = newOptions;
        }

        private void OptionsPanel_PanelClosed(object sender, EventArgs e) {
            CloseOptionsPanel(optionsPanel_.SyntaxFileChanged);
        }

        public void ReloadSettings() {
            TextView.Initalize(settings_, session_);
            SelectedLineBrush = settings_.SelectedValueColor.AsBrush();
            UpdateColumnsList();
        }

        private void UpdateColumnsList() {
            ColumnsList.Background = ColorBrushes.GetBrush(settings_.BackgroundColor);
            double h = Utils.MeasureString("0123456789ABCFEFGH", settings_.FontName, settings_.FontSize).Height;

            ColumnsListItemHeight = h;

            //ColumnsList.InvalidateVisual();
            //ColumnsList.UpdateLayout();
        }

        private void TextView_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
            //CloseSectionPanel();
        }

        private void SearchPanel_CloseSearchPanel(object sender, SearchInfo e) {
            HideSearchPanel();
        }

        private void SearchPanel_NavigateToNextResult(object sender, SearchInfo e) {
            TextView.JumpToSearchResult(searchResult_.Results[e.CurrentResult], Colors.LightSkyBlue);
        }

        private void SearchPanel_NaviateToPreviousResult(object sender, SearchInfo e) {
            TextView.JumpToSearchResult(searchResult_.Results[e.CurrentResult], Colors.LightSkyBlue);
        }

        private void IRDocumentHost_PreviewKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                CloseSectionPanel();
                HideSearchPanel();
                e.Handled = true;
            }
        }

        private void SectionPanel_ClosePanel(object sender, EventArgs e) {
            CloseSectionPanel();
        }

        private async void SectionPanel_OpenSection(object sender, OpenSectionEventArgs e) {
            SectionPanelHost.Visibility = Visibility.Collapsed;
            e.TargetDocument = this;
            await Session.SwitchDocumentSectionAsync(e);
            TextView.Focus();
        }

        private void CloseSectionPanel() {
            if (SectionPanelHost.Visibility == Visibility.Visible) {
                SectionPanelHost.Visibility = Visibility.Collapsed;
            }
        }

        public async void UnloadSection(IRTextSection section, bool switchingActiveDocument) {
            if (!duringSwitchSearchResults_ && !switchingActiveDocument) {
                HideSearchPanel();
            }

            // Cancel any running tasks and hide panels.
            loadTask_.CancelTask();
            // await loadTask_.WaitForTaskAsync();
            await HideRemarkPanel();
            HideActionPanel();
            SaveSectionState(section);

            if (!switchingActiveDocument) {
                if (PassOutputVisible) {
                    await PassOutput.UnloadSection(section, TextView);
                }

                await RemoveRemarks();
                await HideProfile();

                // Clear references to IR objects that would keep the previous function alive.
                hoveredElement_ = null;
                selectedElement_ = null;
                remarkElement_ = null;
                selectedBlock_ = null;
                profileDataRows_ = null;
                profileColumnHeaders_ = null;
                ProfileVisible = false;
                PassOutputVisible = false;
                BlockSelector.SelectedItem = null;
                BlockSelector.ItemsSource = null;
            }
        }

        private async Task RemoveRemarks() {
            remarkList_ = null;
            activeRemarkContext_ = null;
            await UpdateDocumentRemarks(remarkList_);
        }

        private void SaveSectionState(IRTextSection section) {
            // Annotations made in diff mode are not saved right now,
            // since the text and function IR can be different than the original function.
            if (TextView.DiffModeEnabled) {
                return;
            }

            var state = new IRDocumentHostState();
            state.DocumentState = TextView.SaveState();
            state.HorizontalOffset = TextView.HorizontalOffset;
            state.VerticalOffset = TextView.VerticalOffset;
            var data = StateSerializer.Serialize(state, Function);

            Session.SaveDocumentState(data, section);
            Session.SetSectionAnnotationState(section, state.HasAnnotations);
        }

        public void OnSessionSave() {
            if (Section != null) {
                SaveSectionState(Section);
            }
        }

        public async Task SwitchSearchResultsAsync(SectionSearchResult searchResults, IRTextSection section,
                                                   SearchInfo searchInfo) {
            // Ensure the right section is being displayed.
            duringSwitchSearchResults_ = true;
            var openArgs = new OpenSectionEventArgs(section, OpenSectionKind.ReplaceCurrent, this);
            await Session.SwitchDocumentSectionAsync(openArgs);
            duringSwitchSearchResults_ = false;

            // Show the search panel and mark all results on the document.
            searchResult_ = searchResults;
            searchInfo.CurrentResult = 1;
            searchInfo.ResultCount = searchResults.Results.Count;
            ShowSearchPanel(searchInfo);
            TextView.MarkSearchResults(searchResults.Results, Colors.Khaki);
        }

        public bool HasSameSearchResultSection(IRTextSection section) {
            if (Section != section) {
                return false;
            }

            // Force the search panel to be displayed in case it was closed.
            return searchPanelVisible_;
        }

        public void JumpToSearchResult(TextSearchResult result, int index) {
            if (index >= SearchPanel.SearchInfo.ResultCount) {
                return;
            }

            SearchPanel.SearchInfo.CurrentResult = index;
            TextView.JumpToSearchResult(result, Colors.LightSkyBlue);
        }

        public void LoadSectionMinimal(ParsedIRTextSection parsedSection) {
            TextView.EarlyLoadSectionSetup(parsedSection);
        }

        public async Task LoadSection(ParsedIRTextSection parsedSection) {
            duringSectionSwitching_ = true;
            var data = Session.LoadDocumentState(parsedSection.Section);
            double horizontalOffset = 0;
            double verticalOffset = 0;

            if (data != null) {
                var state = StateSerializer.Deserialize<IRDocumentHostState>(data, parsedSection.Function);
                await TextView.LoadSavedSection(parsedSection, state.DocumentState);
                horizontalOffset = state.HorizontalOffset;
                verticalOffset = state.VerticalOffset;
            }
            else {
                await TextView.LoadSection(parsedSection);
            }

            if (PassOutputVisible) {
                await PassOutput.SwitchSection(parsedSection.Section, TextView);
            }

            await ReloadProfile();
            await ReloadRemarks();

            TextView.ScrollToHorizontalOffset(horizontalOffset);
            TextView.ScrollToVerticalOffset(verticalOffset);
            duringSectionSwitching_ = false;
        }

        public bool PassOutputVisible {
            get => pasOutputVisible_;
            set {
                if(pasOutputVisible_ != value) {
                    if(!pasOutputVisible_) {
                        PassOutput.SwitchSection(Section, TextView);
                    }

                    pasOutputVisible_ = value;
                    NotifyPropertyChanged(nameof(PassOutputVisible));
                    PassOutputVisibilityChanged?.Invoke(this, value);
                }
            }
        }
        
        public bool ColumnsVisible {
            get => columnsVisible_;
            set {
                if (columnsVisible_ != value) {
                    columnsVisible_ = value;
                    NotifyPropertyChanged(nameof(ColumnsVisible));
                }
            }
        }

        public bool ProfileVisible {
            get => profileVisible_;
            set {
                if (profileVisible_ != value) {
                    profileVisible_ = value;
                    NotifyPropertyChanged(nameof(ProfileVisible));
                }
            }
        }

        private List<Tuple<IRElement, TimeSpan>> profileElements_;
        private List<Tuple<BlockIR, TimeSpan>> profileBlocks_;
        private List<ElementRowValue> profileDataRows_;
        private List<(GridViewColumnHeader Header, GridViewColumn Column)> profileColumnHeaders_;
        private int profileElementIndex_;
        private int profileBlockIndex_;

        private static readonly OptionalColumn TIME_COLUMN = OptionalColumn.Template("[TimeHeader]", "TimeColumnValueTemplate",
            "TimeHeader", "Time (ms)", "Instruction time");

        public void UpdateProfileDataColumnWidths() {
            if (profileDataRows_ == null ||
                profileColumnHeaders_ == null) {
                return;
            }

            var maxColumnTextSize = new Dictionary<OptionalColumn, double>();
            var maxColumnExtraSize = new Dictionary<OptionalColumn, double>();
            var font = new FontFamily(settings_.FontName);
            var fontSize = settings_.FontSize;
            const double maxBarWidth = 50;
            const double columnMargin = 4;

            foreach (var rowValues in profileDataRows_) {
                foreach (var columnValue in rowValues.ColumnValues) {
                    columnValue.Value.TextFont = font;
                    columnValue.Value.TextSize = fontSize;
                    
                    // Remember the max text length for each column
                    // to later set the MinWidth for alignment.
                    maxColumnTextSize.CollectMaxValue(columnValue.Key, columnValue.Value.Text.Length);

                    if (columnValue.Value.ShowPercentageBar ||
                        columnValue.Value.ShowIcon) {
                        double extraColumnWidth = columnMargin;

                        if (columnValue.Value.ShowPercentageBar) {
                            extraColumnWidth += columnValue.Value.ValuePercentage * maxBarWidth;
                        }

                        if (columnValue.Value.ShowIcon) {
                            // Width of the icon is at most the height of the row.
                            extraColumnWidth += ColumnsListItemHeight + columnMargin;
                        }

                        maxColumnExtraSize.CollectMaxValue(columnValue.Key, extraColumnWidth);
                    }
                }
            }

            // Set the MinWidth of the text for each cell.
            foreach (var pair in maxColumnTextSize) {
                var columnContentSize = Utils.MeasureString((int)pair.Value, settings_.FontName, settings_.FontSize);
                double columnWidth = columnContentSize.Width + columnMargin;
                maxColumnTextSize[pair.Key] = Math.Ceiling(columnWidth);

                // Also set the initial width of each column header.
                // For the header, consider image and percentage bar too.
                if (maxColumnExtraSize.TryGetValue(pair.Key, out var extraSize)) {
                    columnWidth += extraSize;
                }

                //? TODO: Pass options and check RemoveEmptyColumns
                if (columnWidth == 0) {
                    pair.Key.IsVisible = false;
                }
                else {
                    var columnTitleSize = Utils.MeasureString(pair.Key.Title, settings_.FontName, settings_.FontSize);
                    var gridColumn = profileColumnHeaders_.Find(item => item.Header.Tag.Equals(pair.Key));

                    if (gridColumn.Column == null || gridColumn.Header == null) {
                        continue;
                    }

                    columnWidth = Math.Max(columnWidth, columnTitleSize.Width + columnMargin);
                    gridColumn.Header.Width = columnWidth;
                    gridColumn.Column.Width = columnWidth;
                }

                //Trace.WriteLine($"Column width {columnWidth} for {pair.Key.ColumnName}");
            }

            foreach (var row in profileDataRows_) {
                foreach (var columnValue in row.ColumnValues) {
                    columnValue.Value.MinTextWidth = maxColumnTextSize[columnValue.Key];
                }
            }
        }

        private async Task ReloadProfile() {
            if (Session.ProfileData == null) {
                return;
            }

            var funcProfile = Session.ProfileData.GetFunctionProfile(Section.ParentFunction);
            var metadataTag = Function.GetTag<AssemblyMetadataTag>();

            if (funcProfile == null || metadataTag == null) {
                return;
            }

            var result = await Task.Run(() => funcProfile.Process(Function, Session.CompilerInfo.IR));
            BuildProfileBlocksList(funcProfile, result);
            profileElements_ = result.SampledElements;
            ProfileVisible = true;

            // Show optional columns with timing, counters, etc.
            // First remove any previous columns.
            OptionalColumn.RemoveListViewColumns(ColumnsList);
            ColumnsList.ItemsSource = null;

            var columnData = TextView.ColumnData;
            ColumnsVisible = columnData.HasData;

            if (!columnData.HasData) {
                return;
            }

            int docLineCount = TextView.LineCount;

            var elementValueList  = await Task.Run(() => {
                var elementValueList = new List<ElementRowValue>(Function.TupleCount);
                var oddBackColor = settings_.AlternateBackgroundColor.AsBrush();
                var blockSeparatorColor = settings_.ShowBlockSeparatorLine ? settings_.BlockSeparatorColor.AsBrush() : null;
                var font = new FontFamily(settings_.FontName);
                var fontSize = settings_.FontSize;

                ElementColumnValue MakeDummyCell() {
                    var columnValue = ElementColumnValue.Empty;

                    //? if (showColumnSeparators) {
                    columnValue.BorderBrush = blockSeparatorColor;
                    columnValue.BorderThickness = new Thickness(0, 0, 1, 0);
                    columnValue.TextFont = font;
                    columnValue.TextSize = fontSize;
                    return columnValue;
                }

                ElementRowValue MakeDummyRow(Brush backColor = null) {
                    var row = new ElementRowValue(null) { BackColor = backColor, BorderBrush = blockSeparatorColor };

                    foreach (var column in columnData.Columns) {
                        row.ColumnValues[column] = MakeDummyCell();
                    }

                    return row;
                }

                var dummyValues = MakeDummyRow();
                var oddDummyValues = MakeDummyRow(oddBackColor);

                int prevLine = -1;
                bool prevIsOddBlock = false;

                void AddDummyRows(int count, bool isOddBlock) {
                    for (int i = 0; i < count; i++) {
                        elementValueList.Add(isOddBlock ? oddDummyValues : dummyValues);
                    }
                }

                const double maxBarWidth = 50;
                const double columnMargin = 4;
                profileDataRows_ = new List<ElementRowValue>();

                foreach (var block in Function.SortedBlocks) {
                    bool isOddBlock = block.HasOddIndexInFunction;

                    for (int i = 0; i < block.Tuples.Count; i++) {
                        var tuple = block.Tuples[i];
                        int currentLine = tuple.TextLocation.Line;
                        bool isSeparatorLine = settings_.ShowBlockSeparatorLine &&
                                               i == block.Tuples.Count - 1;

                        // Add dummy empty list view lines to match document text.
                        if (currentLine > prevLine + 1) {
                            AddDummyRows(currentLine - prevLine - 1, isOddBlock);
                        }

                        // Check if there is any data associated with the element.
                        // Have the row match the background color used in the doc.
                        var rowValues = columnData.GetValues(tuple);

                        if (rowValues != null) {
                            rowValues.BorderBrush = blockSeparatorColor;

                            if (rowValues.BackColor == null && isOddBlock) {
                                rowValues.BackColor = oddBackColor;
                            }

                            foreach (var columnValue in rowValues.ColumnValues) {
                                columnValue.Value.TextFont = font;
                                columnValue.Value.TextSize = fontSize;
                            }

                            // Add dummy cells for the missing ones, needed for column separators.
                            if (rowValues.ColumnValues.Count != columnData.Columns.Count) {
                                foreach (var column in columnData.Columns) {
                                    if (!rowValues.Columns.Contains(column)) {
                                        rowValues.ColumnValues[column] = MakeDummyCell();
                                    }
                                }
                            }

                            profileDataRows_.Add(rowValues);
                        }
                        else {
                            if (isSeparatorLine) {
                                rowValues = isOddBlock ? MakeDummyRow(oddBackColor) : MakeDummyRow();
                            }
                            else {
                                rowValues = isOddBlock ? oddDummyValues : dummyValues;
                            }
                        }

                        // Add a separator line at the bottom of the current row
                        // if the next instr. is found in another block.
                        if (isSeparatorLine) {
                            rowValues.BorderThickness = new Thickness(0, 0, 0, 1);
                            rowValues.BorderBrush = blockSeparatorColor;
                        }

                        //? if (showColumnSeparators) {
                        foreach (var value in rowValues.Values) {
                            value.BorderBrush = blockSeparatorColor;
                            value.BorderThickness = new Thickness(0, 0, 1, 0);
                        }

                        elementValueList.Add(rowValues);
                        prevLine = currentLine;
                    }

                    prevIsOddBlock = isOddBlock;
                }

                // Add empty lines at the end to match document,
                // otherwise scrolling can get out of sync.
                if (docLineCount != prevLine + 1) {
                    AddDummyRows(docLineCount - prevLine, prevIsOddBlock);
                }

                return elementValueList;
            });

            profileColumnHeaders_ = OptionalColumn.AddListViewColumns(ColumnsList, columnData.Columns);

            foreach (var columnHeader in profileColumnHeaders_) {
                columnHeader.Header.Click += ColumnHeaderOnClick;
                columnHeader.Header.MouseDoubleClick += ColumnHeaderOnDoubleClick;
            }

            UpdateProfileDataColumnWidths();
            ColumnsList.ItemsSource = new ListCollectionView(elementValueList);
            ColumnsList.Background = settings_.BackgroundColor.AsBrush();
        }

        private void BuildProfileBlocksList(FunctionProfileData funcProfile, 
                                             FunctionProfileData.ProcessingResult result) {
            profileBlocks_ = result.BlockSampledElements;
            var list = new List<ProfiledBlockEx>(result.BlockSampledElements.Count);
            double maxWidth = 0;

            var valueTemplate = (DataTemplate)Application.Current.FindResource("BlockPercentageValueTemplate");
            var valueStyle = ProfileDocumentMarkerOptions.Default; //? TODO: Use options
            int index = 0;

            foreach (var pair in result.BlockSampledElements) {
                var block = pair.Item1;
                var weight = pair.Item2;
                double weightPercentage = funcProfile.ScaleWeight(weight);
                var prefixText = $"B{block.Number}";
                var text = $"({weight.AsMillisecondsString()})";

                var value = new ProfiledBlockEx(text, weight.Ticks, weightPercentage) {
                    Element = block,
                    PrefixText = prefixText,
                    ShowPercentageBar = valueStyle.ShowPercentageBar(weightPercentage),
                    TextWeight = valueStyle.PickTextWeight(weightPercentage),
                    PercentageBarBackColor = valueStyle.PercentageBarBackColor
                };

                var item = new MenuItem() {
                    Header = value,
                    Tag = list.Count,
                    HeaderTemplate = valueTemplate
                };

                item.Click += (sender, args) => {
                    var menuItem = (MenuItem)sender;
                    profileBlockIndex_ = (int)menuItem.Tag;
                    JumpToProfiledBlock(profileBlocks_[profileBlockIndex_].Item1);
                };

                ProfileBlocksMenu.Items.Add(item);

                // Make sure percentage rects are aligned.
                double width = Utils.MeasureString(prefixText, settings_.FontName, settings_.FontSize).Width;
                maxWidth = Math.Max(width, maxWidth);
                list.Add(value);
            }

            foreach (var value in list) {
                value.MinTextWidth = maxWidth;
            }
        }

        private void ColumnHeaderOnClick(object sender, RoutedEventArgs e) {
            if (((GridViewColumnHeader)sender).Tag is OptionalColumn column && 
                column.HeaderClickHandler != null) {
                column.HeaderClickHandler(column);
                UpdateProfileDataColumnWidths();
            }
        }

        private void ColumnHeaderOnDoubleClick(object sender, RoutedEventArgs e) {
            if (((GridViewColumnHeader)sender).Tag is OptionalColumn column &&
                column.HeaderDoubleClickHandler != null) {
                column.HeaderDoubleClickHandler(column);
                UpdateProfileDataColumnWidths();
            }
        }

        private async Task HideProfile() {
            ProfileVisible = false;
            ProfileBlocksMenu.Items.Clear();
        }

        private async Task ReloadRemarks() {
            await RemoveRemarks();

            // Loading remarks can take several seconds for very large functions,
            // this makes it possible to cancel the work if section switches.
            using var cancelableTask = loadTask_.CreateTask();
            remarkList_ = await FindRemarks(cancelableTask);

            if(cancelableTask.IsCanceled) {
                return;
            }

            await AddRemarks(remarkList_);
        }
        
        private async Task<List<Remark>> FindRemarks(CancelableTask cancelableTask) {
            var remarkProvider = Session.CompilerInfo.RemarkProvider;

            return await Task.Run(() => {
                var sections = remarkProvider.GetSectionList(Section, remarkSettings_.SectionHistoryDepth,
                                                             remarkSettings_.StopAtSectionBoundaries);
                var document = Session.SessionState.FindLoadedDocument(Section);
                var options = new RemarkProviderOptions();
                var results = remarkProvider.ExtractAllRemarks(sections, Function, document, options, cancelableTask);
                loadTask_.CompleteTask(cancelableTask, Session.SessionState.UnregisterCancelableTask);
                return results;
            });
        }

        private async Task AddRemarks(List<Remark> remarks) {
            await AddRemarkTags(remarks);
            await UpdateDocumentRemarks(remarks);
        }

        private (List<Remark>, List<RemarkLineGroup>) FilterDocumentRemarks(List<Remark> remarks) {
            // Filter list based on selected options.
            var filteredList = new List<Remark>(remarks.Count);

            foreach (var remark in remarks) {
                if (IsAcceptedContextRemark(remark, Section, remarkSettings_)) {
                    filteredList.Add(remark);
                }
            }

            // Group remarks by element line number.
            var markerRemarksGroups = new List<RemarkLineGroup>(remarks.Count);

            if (remarkSettings_.ShowMarginRemarks) {
                var markerRemarksMap = new Dictionary<int, RemarkLineGroup>(remarks.Count);

                foreach (var remark in filteredList) {
                    if (!remark.Category.AddLeftMarginMark) {
                        continue;
                    }

                    if (remark.Section != Section) {
                        // Remark is from previous section. Accept only if user wants
                        // to see previous optimization remarks on the left margin.
                        bool isAccepted = (remark.Category.Kind == RemarkKind.Optimization &&
                                           remarkSettings_.ShowPreviousOptimizationRemarks) ||
                                           (remark.Category.Kind == RemarkKind.Analysis &&
                                            remarkSettings_.ShowPreviousAnalysisRemarks);
                        if (!isAccepted) {
                            continue;
                        }
                    }

                    bool handled = false;
                    int elementLine = -1;

                    foreach (var element in remark.ReferencedElements) {
                        elementLine = element.TextLocation.Line;

                        if (markerRemarksMap.TryGetValue(elementLine, out var remarkGroup)) {
                            remarkGroup.Add(remark, Section);
                            handled = true;
                            break;
                        }
                    }

                    if (!handled) {
                        var remarkGroup = new RemarkLineGroup(elementLine, remark);
                        markerRemarksMap[elementLine] = remarkGroup;
                        markerRemarksGroups.Add(remarkGroup);
                    }
                }
            }

            return (remarkSettings_.ShowDocumentRemarks ? filteredList : null,
                    remarkSettings_.ShowMarginRemarks ? markerRemarksGroups : null);
        }

        private async Task UpdateDocumentRemarks(List<Remark> remarks) {
            if (remarks == null || !remarkSettings_.ShowRemarks ||
                (!remarkSettings_.ShowMarginRemarks &&
                 !remarkSettings_.ShowDocumentRemarks)) {
                TextView.RemoveRemarks(); // No remarks or disabled.
                return;
            }

            var (allRemarks, markerRemarksGroups) = await Task.Run(() => FilterDocumentRemarks(remarks));
            TextView.UpdateRemarks(allRemarks, markerRemarksGroups, activeRemarkContext_ != null);
        }

        //? TODO: Create a new class to do the remark finding/filtering work
        public bool IsAcceptedRemark(Remark remark, IRTextSection section, RemarkSettings remarkSettings) {
            if (!remarkSettings.ShowPreviousSections && remark.Section != section) {
                return false;
            }

            //? TODO: Move SearchText into a state object
            if (!string.IsNullOrEmpty(remarkSettings.SearchedText)) {
                if (!remark.RemarkText.Contains(remarkSettings.SearchedText, StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }
            }

            var kindResult = remark.Kind switch
            {
                RemarkKind.Analysis => remarkSettings.Analysis,
                RemarkKind.Optimization => remarkSettings.Optimization,
                RemarkKind.Default => remarkSettings.Default,
                RemarkKind.Verbose => remarkSettings.Verbose,
                RemarkKind.Trace => remarkSettings.Trace,
                _ => false
            };

            if (!kindResult) {
                return false;
            }

            if (remark.Category.HasTitle && remarkSettings.HasCategoryFilters) {
                if (remarkSettings.CategoryFilter.TryGetValue(remark.Category.Title, out bool isCategoryEnabled)) {
                    return isCategoryEnabled;
                }
            }

            return true;
        }

        public bool IsAcceptedContextRemark(Remark remark, IRTextSection section, RemarkSettings remarkSettings) {
            if (!IsAcceptedRemark(remark, section, remarkSettings)) {
                return false;
            }

            // Filter based on context, accept any context that is a child of the active context.
            //if (activeRemarkContext_ != null) {
            //    return IsActiveContextTreeRemark(remark);
            //}

            return true;
        }

        public bool IsActiveContextTreeRemark(Remark remark) {
            var context = remark.Context;

            while (context != null) {
                if (context == activeRemarkContext_) {
                    Trace.TraceInformation($"=> Accept remark in context {remark.Context.Name}");
                    Trace.TraceInformation($"      text \"{remark.RemarkText}\"");
                    return true;
                }

                context = context.Parent;
            }

            return false;
        }

        private void RemoveRemarkTags() {
            Function.ForEachElement(element => {
                element.RemoveTag<RemarkTag>();
                return true;
            });
        }

        private Task AddRemarkTags(List<Remark> remarks) {
            return Task.Run(() => {
                RemoveRemarkTags();

                foreach (var remark in remarks) {
                    foreach (var element in remark.ReferencedElements) {
                        var remarkTag = element.GetOrAddTag<RemarkTag>();
                        remarkTag.Remarks.Add(remark);
                    }
                }
            });
        }

        public async Task EnterDiffMode() {
            if (Section != null) {
                SaveSectionState(Section);
            }

            await HideOptionalPanels();
            TextView.EnterDiffMode();
        }

        public async Task ExitDiffMode() {
            TextView.ExitDiffMode();

            if(PassOutputVisible) {
                await PassOutput.RestorePassOutput();
            }

            await HideOptionalPanels();
        }

        private async Task HideOptionalPanels() {
            HideSearchPanel();
            HideActionPanel();
            await HideRemarkPanel();
        }

        private void TextView_BlockSelected(object sender, IRElementEventArgs e) {
            if (e.Element != selectedBlock_) {
                selectedBlock_ = e.Element;
                BlockSelector.SelectedItem = e.Element;
            }
        }

        private void TextView_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            var blockList = new CollectionView(TextView.Blocks);
            BlockSelector.ItemsSource = blockList;
        }

        private void TextView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            hoverPoint_ = e.GetPosition(TextView.TextArea.TextView);
            TextView.SelectElementAt(hoverPoint_);
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e) {
            TextView.ClearMarkedElementAt(hoverPoint_);
        }

        private void ComboBox_Loaded(object sender, RoutedEventArgs e) {
            if (sender is ComboBox control) {
                Utils.PatchComboBoxStyle(control);
            }
        }

        private void BlockSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (e.AddedItems.Count != 1) {
                return;
            }

            var block = e.AddedItems[0] as BlockIR;

            // If the event triggers during loading the section, while the combobox is update,
            // ignore it, otherwise it selects the first block.
            if (block != selectedBlock_ && !TextView.DuringSectionLoading) {
                selectedBlock_ = block;
                TextView.GoToBlock(block);
            }
        }

        private void NextBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
            TextView.GoToNextBlock();
        }

        private void PreviousBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
            TextView.GoToPreviousBlock();
        }

        private void FocusBlockSelectorExecuted(object sender, ExecutedRoutedEventArgs e) {
            BlockSelector.Focus();
            BlockSelector.IsDropDownOpen = true;
        }

        private void ToggleSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
            ShowSearchPanel(true);
        }

        private void ShowSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
            ShowSearchPanel(false);
        }

        private void ShowSearchPanel(bool fromKeyboardShortcut) {
            // Use selected text as initial search input.
            var info = new SearchInfo();
            bool hasInitialText = false;

            if (TextView.SelectionLength > 1) {
                info.SearchedText = TextView.SelectedText;
                info.IsCaseInsensitive = true;
                hasInitialText = true;
            }

            if (!searchPanelVisible_) {
                ShowSearchPanel(info);
            }
            else if (fromKeyboardShortcut) {
                // For a subsequent keyboard shortcut press,
                // don't hide the visible panel, instead either use the new selected text,
                // or there is no selection, select the entire text in the search panel.
                SearchPanel.SearchInfo.SearchedText = info.SearchedText;
                SearchPanel.SearchInfo.IsCaseInsensitive = info.IsCaseInsensitive;
                SearchPanel.Show(SearchPanel.SearchInfo,
                                 SearchPanel.SearchInfo.SearchAll, !hasInitialText);
            }
            else {
                HideSearchPanel();
            }
        }

        private void HideSearchPanel() {
            if (!searchPanelVisible_) {
                return;
            }

            searchPanelVisible_ = false;
            SearchPanel.Hide();
            SearchPanel.Reset();
            SearchPanel.Visibility = Visibility.Collapsed;
            SearchButton.IsChecked = false;
        }

        private void ShowSearchPanel(SearchInfo searchInfo, bool searchAll = false) {
            SearchPanel.Visibility = Visibility.Visible;
            SearchPanel.Show(searchInfo, searchAll);
            SearchButton.IsChecked = true;
            searchPanelVisible_ = true;
        }

        private void ShowSectionListExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (SectionPanelHost.Visibility == Visibility.Visible) {
                SectionPanelHost.Visibility = Visibility.Collapsed;
            }
            else {
                SectionPanel.CompilerInfo = Session.CompilerInfo;
                SectionPanel.Session = Session;
                SectionPanel.Summary = Session.GetDocumentSummary(Section);
                SectionPanel.SelectSection(Section, true, true);
                SectionPanelHost.Visibility = Visibility.Visible;
                SectionPanelHost.Focus();
            }
        }

        private void PreviousSectionExecuted(object sender, ExecutedRoutedEventArgs e) {
            Session.SwitchToPreviousSection(Section, TextView);
            TextView.Focus();
        }

        private void NextSectionExecuted(object sender, ExecutedRoutedEventArgs e) {
            Session.SwitchToNextSection(Section, TextView);
            TextView.Focus();
        }

        private void SearchSymbolExecuted(object sender, ExecutedRoutedEventArgs e) {
            SearchSymbolImpl(false);
        }

        private void SearchSymbolAllSectionsExecuted(object sender, ExecutedRoutedEventArgs e) {
            SearchSymbolImpl(true);
        }
        
        private void JumpToProfiledElement(IRElement element) {
            TextView.SetCaretAtElement(element);
            TextView.BringElementIntoView(element);
            ColumnsList.InvalidateVisual();
            ColumnsList.UpdateLayout();

            double offset = TextView.TextArea.TextView.VerticalOffset;
            SyncColumnsVerticalScrollOffset(offset);
        }

        private void SearchSymbolImpl(bool searchAllSections) {
            var element = TextView.TryGetSelectedElement();

            if (element == null || !element.HasName) {
                return;
            }

            string symbolName = element.Name;
            var searchInfo = new SearchInfo();
            searchInfo.SearchedText = symbolName;
            searchInfo.SearchAll = searchAllSections;
            ShowSearchPanel(searchInfo);
        }

        private async void SearchPanel_SearchChanged(object sender, SearchInfo info) {
            string searchedText = info.SearchedText.Trim();

            if (searchedText.Length > 1) {
                searchResult_ = await Session.SearchSectionAsync(info, Section, TextView);

                if(!searchResult_.HasResults) {
                    // Nothing found in the current document.
                    info.ResultCount = 0;
                    TextView.ClearSearchResults();
                    return;
                }

                info.ResultCount = searchResult_.Results.Count;
                TextView.MarkSearchResults(searchResult_.Results, Colors.Khaki);

                if (searchResult_.Results.Count > 0) {
                    TextView.JumpToSearchResult(searchResult_.Results[0], Colors.LightSkyBlue);
                }
            }
            else if (searchedText.Length == 0) {
                // Reset search panel and markers.
                if (info.ResultCount > 0) {
                    SearchPanel.Reset();
                }

                await Session.SearchSectionAsync(info, Section, TextView);
                TextView.ClearSearchResults();
                searchResult_ = null;
            }
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        private void PanelToolbarTray_SettingsClicked(object sender, EventArgs e) {
            if (optionsPanelVisible_) {
                CloseOptionsPanel(false);
            }
            else {
                ShowOptionsPanel();
            }
        }

        private void ShowOptionsPanel() {
            if (optionsPanelVisible_) {
                return;
            }

            var width = Math.Max(DocumentOptionsPanel.MinimumWidth,
                    Math.Min(TextView.ActualWidth, DocumentOptionsPanel.DefaultWidth));
            var height = Math.Max(DocumentOptionsPanel.MinimumHeight,
                    Math.Min(TextView.ActualHeight, DocumentOptionsPanel.DefaultHeight));

            FrameworkElement relativeElement = ProfileVisible ? ColumnsList : TextView;
            var position = new Point(relativeElement.ActualWidth - width, 0);

            optionsPanel_ = new DocumentOptionsPanel();
            optionsPanelWindow_ = new OptionsPanelHostWindow(optionsPanel_, position, width, height, relativeElement);

            optionsPanelWindow_.PanelClosed += OptionsPanel_PanelClosed;
            optionsPanelWindow_.PanelReset += OptionsPanel_PanelReset;
            optionsPanelWindow_.SettingsChanged += OptionsPanel_SettingsChanged;
            optionsPanelWindow_.Settings = settings_.Clone();
            optionsPanelWindow_.IsOpen = true;
            optionsPanelVisible_ = true;
        }

        private void CloseOptionsPanel(bool syntaxFileChanged) {
            if (!optionsPanelVisible_) {
                return;
            }

            optionsPanelWindow_.IsOpen = false;
            optionsPanelWindow_.PanelClosed -= OptionsPanel_PanelClosed;
            optionsPanelWindow_.PanelReset -= OptionsPanel_PanelReset;
            optionsPanelWindow_.SettingsChanged -= OptionsPanel_SettingsChanged;

            var newSettings = (DocumentSettings)optionsPanelWindow_.Settings;
            LoadNewSettings(newSettings, syntaxFileChanged, true);

            optionsPanel_ = null;
            optionsPanelWindow_ = null;
            optionsPanelVisible_ = false;
        }

        private OptionsPanelHostWindow remarkOptionsPanelWindow_;

        private OptionsPanelHostWindow optionsPanelWindow_;
        private DocumentOptionsPanel optionsPanel_;
        private DelayedAction delayedHideActionPanel_;
        private bool profileVisible_;
        private double columnsListItemHeight_;

        private void ShowRemarkOptionsPanel() {
            if (remarkOptionsPanelVisible_) {
                return;
            }

            var width = Math.Max(RemarkOptionsPanel.MinimumWidth,
                    Math.Min(TextView.ActualWidth, RemarkOptionsPanel.DefaultWidth));
            var height = Math.Max(RemarkOptionsPanel.MinimumHeight,
                    Math.Min(TextView.ActualHeight, RemarkOptionsPanel.DefaultHeight));
            var position = new Point(RemarkOptionsPanel.LeftMargin, 0);

            remarkOptionsPanelWindow_ = new OptionsPanelHostWindow(new RemarkOptionsPanel(),
                                                                   position, width, height, TextView);
            remarkOptionsPanelWindow_.PanelClosed += RemarkOptionsPanel_PanelClosed;
            remarkOptionsPanelWindow_.PanelReset += RemarkOptionsPanel_PanelReset;
            remarkOptionsPanelWindow_.SettingsChanged += RemarkOptionsPanel_SettingsChanged;
            remarkOptionsPanelWindow_.Settings = remarkSettings_.Clone();
            remarkOptionsPanelWindow_.IsOpen = true;
            remarkOptionsPanelVisible_ = true;
        }

        private async Task CloseRemarkOptionsPanel() {
            if (!remarkOptionsPanelVisible_) {
                return;
            }

            remarkOptionsPanelWindow_.IsOpen = false;
            remarkOptionsPanelWindow_.PanelClosed -= RemarkOptionsPanel_PanelClosed;
            remarkOptionsPanelWindow_.PanelReset -= RemarkOptionsPanel_PanelReset;
            remarkOptionsPanelWindow_.SettingsChanged -= RemarkOptionsPanel_SettingsChanged;

            var newSettings = (RemarkSettings)remarkOptionsPanelWindow_.Settings;
            await HandleNewRemarkSettings(newSettings, true);

            remarkOptionsPanelWindow_ = null;
            remarkOptionsPanelVisible_ = false;
        }

        private async Task HandleNewRemarkSettings(RemarkSettings newSettings, bool commit, bool force = false) {
            if (commit) {
                await Session.ReloadRemarkSettings(newSettings, TextView);
                App.Settings.RemarkSettings = newSettings;
                App.SaveApplicationSettings();
            }

            // Toolbar remark buttons change remarkSettings_ directly,
            // force an update in this case since newSettings is remarkSettings_.
            if (!force && newSettings.Equals(remarkSettings_)) {
                return;
            }

            await ApplyRemarkSettings(newSettings);
        }

        private async Task ApplyRemarkSettings(RemarkSettings newSettings) {
            // If only the remark filters changed, don't recompute the list of remarks.
            bool rebuildRemarkList = remarkList_ == null ||
                                    (newSettings.ShowPreviousSections &&
                                    (newSettings.StopAtSectionBoundaries != remarkSettings_.StopAtSectionBoundaries ||
                                     newSettings.SectionHistoryDepth != remarkSettings_.SectionHistoryDepth));
            App.Settings.RemarkSettings = newSettings;
            await UpdateRemarkSettings(newSettings);

            if (rebuildRemarkList) {
                Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Find and load remarks");
                await ReloadRemarks();
            }
            else {
                Trace.TraceInformation($"Document {ObjectTracker.Track(this)}: Load remarks");
                await UpdateDocumentRemarks(remarkList_);
            }
        }

        private async void RemarkOptionsPanel_SettingsChanged(object sender, EventArgs e) {
            if (remarkOptionsPanelVisible_) {
                var newSettings = (RemarkSettings)remarkOptionsPanelWindow_.Settings;

                if (newSettings != null) {
                    await HandleNewRemarkSettings(newSettings, false);
                    remarkOptionsPanelWindow_.Settings = null;
                    remarkOptionsPanelWindow_.Settings = remarkSettings_.Clone();
                }
            }
        }

        private async void RemarkOptionsPanel_PanelReset(object sender, EventArgs e) {
            await HandleNewRemarkSettings(new RemarkSettings(), true);
            remarkOptionsPanelWindow_.Settings = null;
            remarkOptionsPanelWindow_.Settings = remarkSettings_.Clone();
        }

        private async void RemarkOptionsPanel_PanelClosed(object sender, EventArgs e) {
            await CloseRemarkOptionsPanel();
        }
        
        private void PassOutput_ScrollChanged(object sender, ScrollChangedEventArgs e) {
            PassOutputVerticalScrollChanged?.Invoke(this, (e.VerticalOffset, e.VerticalChange));
        }

        private void PassOutput_ShowBeforeOutputChanged(object sender, bool e) {
            PassOutputShowBeforeChanged?.Invoke(this, e);
        }

        private void ActionPanel_MouseEnter(object sender, MouseEventArgs e) {
            var animation = new DoubleAnimation(1, TimeSpan.FromSeconds(AnimationDuration));
            ActionPanel.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
            actionPanelHovered_ = true;
        }

        private void ActionPanel_MouseLeave(object sender, MouseEventArgs e) {
            actionPanelHovered_ = false;
        }

        private async void MenuItem_Click_1(object sender, RoutedEventArgs e) {
            if (remarkOptionsPanelVisible_) {
                await CloseRemarkOptionsPanel();
            }
            else {
                ShowRemarkOptionsPanel();
            }
        }

        public async Task LoadDiffedFunction(DiffMarkingResult diffResult, IRTextSection newSection) {
            await TextView.LoadDiffedFunction(diffResult, newSection);
            
            if (PassOutputVisible) {
                await PassOutput.SwitchSection(newSection, TextView);
            }

            await ReloadRemarks();
        }

        public async Task LoadDiffedPassOutput(DiffMarkingResult diffResult) {
            if (PassOutputVisible) {
                await PassOutput.LoadDiffedPassOutput(diffResult);
            }
        }

        private void QueryMenuItem_SubmenuOpened(object sender, RoutedEventArgs e) {
            var defaultItems = SaveDefaultMenuItems(QueryMenuItem);
            QueryMenuItem.Items.Clear();

            // Append the available queries.
            var queries = Session.CompilerInfo.BuiltinQueries;

            foreach (var query in queries) {
                var item = new MenuItem() {
                    Header = query.Name,
                    ToolTip = query.Description,
                    Tag = query
                };

                item.Click += QueryMenuItem_Click;
                QueryMenuItem.Items.Add(item);
            }

            // Add back the default menu items.
            RestoreDefaultMenuItems(QueryMenuItem, defaultItems);
        }

        private List<object> SaveDefaultMenuItems(MenuItem menu) {
            // Save the menu items that are always present, they are either
            // separators or menu items without an object tag.
            var defaultItems = new List<object>();

            foreach (var item in menu.Items) {
                if (item is MenuItem menuItem) {
                    if (menuItem.Tag == null) {
                        defaultItems.Add(item);
                    }
                }
                else if (item is Separator) {
                    defaultItems.Add(item);
                }
            }

            return defaultItems;
        }

        private void RestoreDefaultMenuItems(MenuItem menu, List<object> defaultItems) {
            defaultItems.ForEach(item => menu.Items.Add(item));
        }

        private void QueryMenuItem_Click(object sender, System.Windows.RoutedEventArgs e) {
            var menuItem = (MenuItem)sender;
            var query = (QueryDefinition)menuItem.Tag;
            var queryPanel = CreateQueryPanel();
            queryPanel.AddQuery(query);

            CreateQueryActionButtons(query.Data);
        }

        private QueryPanel CreateQueryPanel() {
            //? TODO: Create panel over the document
            var documentHost = this;
            var position = new Point();

            if (documentHost != null) {
                var left = documentHost.ActualWidth - QueryPanel.DefaultWidth - 32;
                var top = documentHost.ActualHeight - QueryPanel.DefaultHeight - 32;
                position = new Point(left, top);
            }

            var queryPanel = new QueryPanel(position, QueryPanel.DefaultWidth, QueryPanel.DefaultHeight, 
                                            documentHost, Session);
            queryPanel.PanelActivated += QueryPanel_PanelActivated;
            queryPanel.PanelTitle = "Queries";
            queryPanel.ShowAddButton = true;
            queryPanel.PopupClosed += QueryPanel_Closed;
            queryPanel.IsOpen = true;
            queryPanel.StaysOpen = true;

            SwitchActiveQueryPanel(queryPanel);
            Session.RegisterDetachedPanel(queryPanel);
            return queryPanel;
        }

        private void QueryPanel_PanelActivated(object sender, EventArgs e) {
            // Change action buttons when another query is activated.
            var panel = (QueryPanel)sender;
            SwitchActiveQueryPanel(panel);
        }

        private void SwitchActiveQueryPanel(QueryPanel panel) {
            if (activeQueryPanels_.Count > 0) {
                // Deactivate the currently active panel.
                var currentPanel = activeQueryPanels_[^1];

                if (currentPanel != panel) {
                    currentPanel.IsActivePanel = false;
                    mainQueryInputValue_ = null;
                    SetActiveQueryPanel(panel);
                }
            }
            else {
                SetActiveQueryPanel(panel);
            }
        }

        private void SetActiveQueryPanel(QueryPanel panel) {
            // Bring to end of list, which is the top of the "stack" of panels.
            activeQueryPanels_.Remove(panel);
            activeQueryPanels_.Add(panel);
            panel.IsActivePanel = true;

            if (panel.QueryCount > 0) {
                // Update the action panel buttons.
                CreateQueryActionButtons(panel.GetQueryAt(0).Data);
            }
        }

        private void QueryPanel_Closed(object sender, EventArgs e) {
            var queryPanel = (QueryPanel)sender;
            CloseQueryPanel(queryPanel);
        }

        private void CloseQueryPanel(QueryPanel queryPanel) {
            queryPanel.PopupClosed -= QueryPanel_Closed;
            queryPanel.IsOpen = false;
            Session.UnregisterDetachedPanel(queryPanel);

            // Update the active query.
            activeQueryPanels_.Remove(queryPanel);

            if (activeQueryPanels_.Count > 0) {
                SetActiveQueryPanel(activeQueryPanels_[^1]);
            }
            else {
                RemoveQueryActionButtons();
            }
        }

        private async void TaskMenuItem_SubmenuOpened(object sender, RoutedEventArgs e) {
            var defaultItems = SaveDefaultMenuItems(TaskMenuItem);
            TaskMenuItem.Items.Clear();

            foreach (var action in Session.CompilerInfo.BuiltinFunctionTasks) {
                AddFunctionTaskDefinitionMenuItem(action);
            }

            // Since first loading the scripts takes 1-2 sec,
            // temporarily add a menu entry to show initially in the menu.
            var item = new MenuItem() {
                Header = "Loading scripts...",
                IsEnabled = false,
            };

            TaskMenuItem.Items.Add(item);

            var scriptTasks = await Task.Run(() => Session.CompilerInfo.ScriptFunctionTasks);

            foreach (var action in scriptTasks) {
                AddFunctionTaskDefinitionMenuItem(action);
            }

            RestoreDefaultMenuItems(TaskMenuItem, defaultItems);
            TaskMenuItem.Items.Remove(item);
        }

        private void AddFunctionTaskDefinitionMenuItem(FunctionTaskDefinition action) {
            var item = new MenuItem() {
                Header = action.TaskInfo.Name,
                ToolTip = action.TaskInfo.Description,
                Tag = action
            };

            item.Click += TaskActionMenuItem_Click;
            TaskMenuItem.Items.Add(item);
        }

        private async void TaskActionMenuItem_Click(object sender, System.Windows.RoutedEventArgs e) {
            var menuItem = (MenuItem)sender;
            var task = (FunctionTaskDefinition)menuItem.Tag;

            if (!await LoadDocumentTask(task)) {
                //? TODO: Error handling, message box
            }
        }

        class DummyQuery : IElementQuery {
            public ISession Session { get; }

            public bool Initialize(ISession session) {
                return true;
            }

            public bool Execute(QueryData data) {
                return true;
            }
        }

        private QueryPanel CreateFunctionTaskQueryPanel() {
            var documentHost = this;
            var position = new Point();

            if (documentHost != null) {
                var left = documentHost.ActualWidth - QueryPanel.DefaultWidth - 32;
                var top = documentHost.ActualHeight - QueryPanel.DefaultHeight - 32;
                position = documentHost.PointToScreen(new Point(left, top));
            }

            var queryPanel = new QueryPanel(position, QueryPanel.DefaultWidth, QueryPanel.DefaultHeight, documentHost, Session);
            Session.RegisterDetachedPanel(queryPanel);

            queryPanel.PanelTitle = "Function Tasks";
            queryPanel.ShowAddButton = false;
            queryPanel.PopupClosed += FunctionTaskPanel_PopupClosed;
            queryPanel.IsOpen = true;
            queryPanel.StaysOpen = true;
            return queryPanel;
        }

        private void AddFunctionTaskPanelButtons(QueryPanel queryPanel, IFunctionTask taskInstance, QueryData optionsData) {
            optionsData.AddButton("Execute", async (sender, value) => {
                taskInstance.LoadOptionsFromValues(optionsData);
                taskInstance.SaveOptions();
                await ExecuteFunctionTask(taskInstance, optionsData, queryPanel);
            });

            optionsData.AddButton("Reset", (sender, value) => {
                taskInstance.ResetOptions();
                taskInstance.SaveOptions();

                // Force a refresh by recreating the query panel.
                var dummyQuery = queryPanel.GetQueryAt(0);
                dummyQuery.Data = taskInstance.GetOptionsValues();
                AddFunctionTaskPanelButtons(queryPanel, taskInstance, dummyQuery.Data);
            });
        }

        private async Task ExecuteFunctionTask(IFunctionTask taskInstance, QueryData optionsData, QueryPanel queryPanel) {
            var cancelableTask = new CancelableTask();
            optionsData.ResetOutputValues();

            if (!await taskInstance.Execute(Function, TextView, cancelableTask)) {
                string description = "";

                if (taskInstance is ScriptFunctionTask scriptTask) {
                    description = scriptTask.ScriptException != null ?
                                  scriptTask.ScriptException.Message : "";

                }

                optionsData.SetOutputWarning("Task failed to execute!", description);
            }
            else if (!string.IsNullOrEmpty(taskInstance.ResultMessage)) {
                if (taskInstance.Result) {
                    optionsData.SetOutputInfo(taskInstance.ResultMessage);
                }
                else {
                    optionsData.SetOutputWarning(taskInstance.ResultMessage);
                }
            }

            if (!string.IsNullOrEmpty(taskInstance.OutputText)) {
                optionsData.ReplaceButton("View Output", async (sender, value) => {
                    var view = new NotesPopup(new Point(queryPanel.HorizontalOffset,
                                                        queryPanel.VerticalOffset + queryPanel.Height),
                                                        500, 200, null);
                    Session.RegisterDetachedPanel(view);
                    var button = (QueryButton)sender;
                    button.IsEnabled = false;

                    view.PanelTitle = "Function Task Output";
                    view.IsOpen = true;
                    view.PopupClosed += (sender, value) => {
                        //? TODO: Should save size of panel and use it next time
                        view.IsOpen = false;
                        button.IsEnabled = true;
                        Session.UnregisterDetachedPanel(view);
                    };

                    view.DetachPopup();
                    await view.SetText(taskInstance.OutputText, Function, Section, TextView, Session);
                });
            }
        }

        private async Task<bool> LoadDocumentTask(FunctionTaskDefinition task) {
            var instance = task.CreateInstance(Session);

            if (instance == null) {
                using var centerForm = new DialogCenteringHelper(this);
                MessageBox.Show($"Failed to create function task instance for {task.TaskInfo.Name}", "IR Explorer",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            CreateFunctionTaskOptionsPanel(task, instance);
            return true;
        }

        private void CreateFunctionTaskOptionsPanel(FunctionTaskDefinition task, IFunctionTask instance) {
            QueryData optionsValues;

            if (task.TaskInfo.HasOptionsPanel) {
                optionsValues = instance.GetOptionsValues();
            }
            else {
                optionsValues = new QueryData();
            }

            var dummyQuery = new QueryDefinition(typeof(DummyQuery),
                                task.TaskInfo.Name, task.TaskInfo.Description);
            dummyQuery.Data = optionsValues;

            var queryPanel = CreateFunctionTaskQueryPanel();
            AddFunctionTaskPanelButtons(queryPanel, instance, optionsValues);
            queryPanel.AddQuery(dummyQuery);
        }

        private void CreateQueryActionButtons(QueryData optionsValues) {
            RemoveQueryActionButtons();
            int actionButtonIndex = 1;

            foreach (var inputValue in optionsValues.InputValues) {
                if (inputValue.IsElement) {
                    ActionPanel.AddActionButton($"{actionButtonIndex}", inputValue);

                    if(actionButtonIndex == 1) {
                        // Attach event only once if it's needed.
                        ActionPanel.ActionButtonClicked += ActionPanel_ActionButtonClicked;
                        mainQueryInputValue_ = inputValue;
                    }

                    actionButtonIndex++;
                }
            }
        }

        private void RemoveQueryActionButtons() {
            ActionPanel.ClearActionButtons();
            mainQueryInputValue_ = null;
        }

        private void ActionPanel_ActionButtonClicked(object sender, ActionPanelButton e) {
            var inputValue = (QueryValue)e.Tag;

            if (hoveredElement_ != null) {
                inputValue.ForceValueUpdate(hoveredElement_);
            }
            if (selectedElement_ != null) {
                inputValue.ForceValueUpdate(selectedElement_);
            }
        }

        private void FunctionTaskPanel_PopupClosed(object sender, EventArgs e) {
            var queryPanel = (QueryPanel)sender;
            queryPanel.PopupClosed -= FunctionTaskPanel_PopupClosed;
            queryPanel.IsOpen = false;
            Session.UnregisterDetachedPanel(queryPanel);
        }

        private async void ActionPanel_RemarksButtonClicked(object sender, EventArgs e) {
            if (remarkPanelVisible_) {
                await HideRemarkPanel();
            }
            else {
                ShowRemarkPanel();
            }
        }

        private void CloseAllQueryPanelsMenuItem_Click(object sender, RoutedEventArgs e) {
            while(activeQueryPanels_.Count > 0) {
                CloseQueryPanel(activeQueryPanels_[0]);
            }
        }

        private void ColumnsList_ScrollChanged(object sender, ScrollChangedEventArgs e) {
            if (duringSectionSwitching_ || Math.Abs(e.VerticalChange) < double.Epsilon) {
                return;
            }
            
            TextView.ScrollToVerticalOffset(e.VerticalOffset);
        }

        private void JumpToProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (!HasProfileElements()) {
                return;
            }

            profileElementIndex_ = 0;
            JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
        }

        private bool HasProfileElements() {
            return ProfileVisible && profileElements_ != null && profileElements_.Count > 0;
        }

        private bool HasProfileElement(int offset) {
            return ProfileVisible && profileElements_ != null &&
                   profileElementIndex_ + offset >= 0 &&
                   profileElementIndex_ + offset < profileElements_.Count;
        }

        private bool HasProfiledBlock(int offset) {
            return ProfileVisible && profileBlocks_ != null &&
                   profileBlockIndex_ + offset >= 0 &&
                   profileBlockIndex_ + offset < profileElements_.Count;
        }
        
        private void JumpToNextProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
            JumpToProfiledElement(-1);
        }

        private void JumpToProfiledElement(int offset) {
            if (!HasProfileElement(offset)) {
                return;
            }

            profileElementIndex_ += offset;
            JumpToProfiledElement(profileElements_[profileElementIndex_].Item1);
        }

        private void JumpToProfiledBlock(int offset) {
            if (!HasProfiledBlock(offset)) {
                return;
            }

            profileBlockIndex_ += offset;
            JumpToProfiledBlock(profileBlocks_[profileBlockIndex_].Item1);
        }

        private void JumpToProfiledBlock(BlockIR block) {
            TextView.GoToBlock(block);
        }

        private void JumpToPreviousProfiledElementExecuted(object sender, ExecutedRoutedEventArgs e) {
            JumpToProfiledElement(1);
        }

        private void JumpToNextProfiledBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
            JumpToProfiledBlock(-1);
        }

        private void JumpToPreviousProfiledBlockExecuted(object sender, ExecutedRoutedEventArgs e) {
            JumpToProfiledBlock(1);
        }

        private void JumpToNextProfiledElementCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = HasProfileElement(-1);
        }

        private void JumpToPreviousProfiledElementCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = HasProfileElement(1);
        }

        private void JumpToNextProfiledBlockCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = HasProfiledBlock(-1);
        }

        private void JumpToPreviousProfiledBlockCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = HasProfiledBlock(1);
        }

        private void ExportFunctionAsExcelFile(string filePath) {
            var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Function");
            var columnData = TextView.ColumnData;
            int rowId = 1; // First row is for the table column names.
            int maxColumn = 2 + (columnData != null ? columnData.Columns.Count : 0);
            int maxLineLength = 0;

            foreach (var block in Function.Blocks) {
                rowId++;
                ws.Cell(rowId, 1).Value = $"Block {block.Number}";
                ws.Cell(rowId, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(rowId, 1).Style.Font.Bold = true;
                ws.Cell(rowId, 1).Style.Font.FontColor = XLColor.DarkBlue;

                for (int i = 1; i <= maxColumn; i++) {
                    ws.Cell(rowId, i).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                }

                foreach (var tuple in block.Tuples) {
                    rowId++;
                    var line = TextView.Document.GetLineByNumber(tuple.TextLocation.Line +1);
                    var text = TextView.Document.GetText(line.Offset, line.Length);
                    ws.Cell(rowId, 1).Value = text;
                    ws.Cell(rowId, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    maxLineLength = Math.Max(text.Length, maxLineLength);

                    var sourceTag = tuple.GetTag<SourceLocationTag>();

                    if (sourceTag != null) {
                        ws.Cell(rowId, 2).Value = sourceTag.Line;
                        ws.Cell(rowId, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        ws.Cell(rowId, 2).Style.Font.FontColor = XLColor.DarkGreen;
                    }

                    if (columnData != null) {
                        IRDocumentColumnData.ExportColumnsToExcel(columnData, tuple, ws, rowId, 3);
                    }
                }
            }

            var firstCell = ws.Cell(1, 1);
            var lastCell = ws.LastCellUsed();
            var range = ws.Range(firstCell.Address, lastCell.Address);
            var table = range.CreateTable();
            table.Theme = XLTableTheme.None;

            foreach (var cell in table.HeadersRow().Cells()) {
                if (cell.Address.ColumnNumber == 1) {
                    cell.Value = "Instruction";
                }
                else if (cell.Address.ColumnNumber == 2) {
                    cell.Value = "Line";
                }
                else if(columnData != null && (cell.Address.ColumnNumber - 3) < columnData.Columns.Count) {
                    cell.Value = columnData.Columns[cell.Address.ColumnNumber - 3].Title;
                }

                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            }

            for (int i = 1; i <= 1; i++) {
                ws.Column(i).AdjustToContents((double)1, maxLineLength);
            }

            wb.SaveAs(filePath);
        }

        private void ExportFunctionProfileExecuted(object sender, ExecutedRoutedEventArgs e) {
            var path = Utils.ShowSaveFileDialog("Excel Worksheets|*.xlsx", "*.xlsx|All Files|*.*");

            if (!string.IsNullOrEmpty(path)) {
                try {
                    ExportFunctionAsExcelFile(path);
                }
                catch (Exception ex) {
                    using var centerForm = new DialogCenteringHelper(this);
                    MessageBox.Show($"Failed to save profiling results to {path}: {ex.Message}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
            }
        }
    }
}
