// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerUI.OptionsPanels;
using IRExplorerCore;
using ProtoBuf;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;

namespace IRExplorerUI {
    public static class Command {
        public static readonly RoutedUICommand ClearTextbox =
            new RoutedUICommand("Clear text", "ClearTextbox", typeof(SectionPanel));
        public static readonly RoutedUICommand Open =
            new RoutedUICommand("Open section", "Open", typeof(SectionPanel));
        public static readonly RoutedUICommand OpenInNewTab =
            new RoutedUICommand("Open section in new tab", "OpenInNewTab", typeof(SectionPanel));
        public static readonly RoutedUICommand OpenLeft =
            new RoutedUICommand("Open section in new tab", "OpenLeft", typeof(SectionPanel));
        public static readonly RoutedUICommand OpenRight =
            new RoutedUICommand("Open section in new tab", "OpenRight", typeof(SectionPanel));
        public static readonly RoutedUICommand OpenSideBySide =
            new RoutedUICommand("Open section in new tab", "OpenSideBySide", typeof(SectionPanel));
        public static readonly RoutedUICommand DiffSideBySide =
            new RoutedUICommand("Open section in new tab", "DiffSideBySide", typeof(SectionPanel));
        public static readonly RoutedUICommand DiffWithOtherDocument =
            new RoutedUICommand("Open section in new tab", "DiffWithOtherDocument", typeof(SectionPanel));
        public static readonly RoutedUICommand SyncDiffedDocuments =
            new RoutedUICommand("Untitled", "SyncDiffedDocuments", typeof(SectionPanel));
        public static readonly RoutedUICommand ToggleTag =
            new RoutedUICommand("Toggle tag", "ToggleTag", typeof(SectionPanel));
        public static readonly RoutedUICommand PreviousSection =
            new RoutedUICommand("Untitled", "PreviousSection", typeof(SectionPanel));
        public static readonly RoutedUICommand NextSection =
            new RoutedUICommand("Untitled", "NextSection", typeof(SectionPanel));
        public static readonly RoutedUICommand FocusSearch =
            new RoutedUICommand("Untitled", "FocusSearch", typeof(SectionPanel));
        public static readonly RoutedUICommand ShowFunctions =
            new RoutedUICommand("Untitled", "ShowFunctions", typeof(SectionPanel));
        public static readonly RoutedUICommand CopySectionText =
            new RoutedUICommand("Untitled", "CopySectionText", typeof(SectionPanel));
        public static readonly RoutedUICommand SaveSectionText =
            new RoutedUICommand("Untitled", "SaveSectionText", typeof(SectionPanel));
        public static readonly RoutedUICommand SaveAllSectionText =
            new RoutedUICommand("Untitled", "SaveAllSectionText", typeof(SectionPanel));
        public static readonly RoutedUICommand DisplayCallGraph =
           new RoutedUICommand("Untitled", "DisplayCallGraph", typeof(SectionPanel));
        public static readonly RoutedUICommand DisplayPartialCallGraph =
           new RoutedUICommand("Untitled", "DisplayPartialCallGraph", typeof(SectionPanel));
    }

    public enum OpenSectionKind {
        ReplaceCurrent,
        ReplaceLeft,
        ReplaceRight,
        NewTab,
        NewTabDockLeft,
        NewTabDockRight,
        UndockedWindow
    }

    public class OpenSectionEventArgs : EventArgs {
        public OpenSectionEventArgs(IRTextSection section, OpenSectionKind kind,
                                    IRDocumentHost targetDocument = null) {
            Section = section;
            OpenKind = kind;
            TargetDocument = targetDocument;
        }

        public IRTextSection Section { get; set; }
        public OpenSectionKind OpenKind { get; set; }
        public IRDocumentHost TargetDocument { get; set; }
    }

    public class DiffModeEventArgs : EventArgs {
        public bool IsWithOtherDocument { get; set; }
        public OpenSectionEventArgs Left { get; set; }
        public OpenSectionEventArgs Right { get; set; }
    }

    public class DisplayCallGraphEventArgs : EventArgs {
        public DisplayCallGraphEventArgs(IRTextSummary summary, IRTextSection section, bool buildPartialGraph) {
            Summary = summary;
            Section = section;
            BuildPartialGraph = buildPartialGraph;
        }

        public IRTextSummary Summary { get; set; }
        public IRTextSection Section { get; set; }
        public bool BuildPartialGraph { get; set; }
    }

    public class IRTextSectionEx : INotifyPropertyChanged {
        private bool isSelected_;
        private bool isTagged_;
        private DiffKind sectionDiffKind_;

        public IRTextSectionEx(IRTextSection section, int index) {
            Section = section;
            SectionDiffKind = DiffKind.None;
            Index = index;
        }

        public IRTextSectionEx(IRTextSection section, DiffKind diffKind, string name, int index) {
            Section = section;
            SectionDiffKind = diffKind;
            Name = name;
            Index = index;
        }

        public int Index { get; set; }
        public IRTextSection Section { get; set; }

        private Thickness borderThickness_;
        public Thickness BorderThickness {
            get => borderThickness_;
            set {
                borderThickness_ = value;
                OnPropertyChange(nameof(BorderThickness));
            }
        }

        public bool HasBeforeBorder => BorderThickness.Top != 0;
        public bool HasAfterBorder => BorderThickness.Bottom != 0;

        private Brush borderBrush_;
        public Brush BorderBrush {
            get => borderBrush_;
            set {
                borderBrush_ = value;
                OnPropertyChange(nameof(BorderBrush));
            }
        }

        private string name_;
        public string Name {
            get => name_;
            set {
                name_ = value;
                OnPropertyChange(nameof(Name));
            }
        }

        public DiffKind SectionDiffKind {
            get => sectionDiffKind_;
            set {
                sectionDiffKind_ = value;
                OnPropertyChange(nameof(IsInsertionDiff));
                OnPropertyChange(nameof(IsDeletionDiff));
                OnPropertyChange(nameof(IsPlaceholderDiff));
                OnPropertyChange(nameof(HasDiffs));
            }
        }

        public bool IsTagged {
            get => isTagged_;
            set {
                isTagged_ = value;
                OnPropertyChange(nameof(IsTagged));
            }
        }

        public bool IsSelected {
            get => isSelected_;
            set {
                isSelected_ = value;
                OnPropertyChange(nameof(IsSelected));
            }
        }

        private bool isMarked_;
        public bool IsMarked {
            get => isMarked_;
            set {
                isMarked_ = value;
                OnPropertyChange(nameof(IsMarked));
            }
        }

        public bool IsInsertionDiff => SectionDiffKind == DiffKind.Insertion;
        public bool IsDeletionDiff => SectionDiffKind == DiffKind.Deletion;
        public bool IsPlaceholderDiff => SectionDiffKind == DiffKind.Placeholder;
        public bool HasDiffs => SectionDiffKind == DiffKind.Modification;

        public Brush NewSectionBrush => ColorBrushes.GetBrush(App.Settings.SectionSettings.NewSectionColor);
        public Brush MissingSectionBrush => ColorBrushes.GetBrush(App.Settings.SectionSettings.MissingSectionColor);
        public Brush ChangedSectionBrush => ColorBrushes.GetBrush(App.Settings.SectionSettings.ChangedSectionColor);

        public Brush TextColor { get; set; }

        public string NumberString => Section != null ? Section.Number.ToString() : "";
        public string BlockCountString => Section != null ? Section.BlockCount.ToString() : "";

        public int Number => Section?.Number ?? 0;
        public int BlockCount => Section?.BlockCount ?? 0;

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }
    }

    [ProtoContract]
    public class SectionPanelState {
        [ProtoMember(1)]
        public List<ulong> AnnotatedSections;
        [ProtoMember(2)]
        public int SelectedFunctionNumber;
        [ProtoMember(3)]
        public int SelectedSectionNumber;

        public SectionPanelState() {
            AnnotatedSections = new List<ulong>();
        }
    }

    public partial class SectionPanel : ToolPanelControl, INotifyPropertyChanged {
        public static readonly DependencyProperty BottomSectionToolbarProperty =
            DependencyProperty.Register("BottomSectionToolbar", typeof(bool), typeof(SectionPanel),
                                        new PropertyMetadata(false, OnBottomSectionToolbarPropertyChanged));

        public static readonly DependencyProperty FunctionPartVisibleProperty =
            DependencyProperty.Register("FunctionPartVisible", typeof(bool), typeof(SectionPanel),
                                        new PropertyMetadata(true, OnFunctionPartVisiblePropertyChanged));

        public static readonly DependencyProperty HideToolbarsProperty =
            DependencyProperty.Register("HideToolbars", typeof(bool), typeof(SectionPanel),
                                        new PropertyMetadata(false, OnHideToolbarsPropertyChanged));
        private HashSet<IRTextSectionEx> annotatedSections_;
        private IRTextFunction currentFunction_;

        private string documentTitle_;
        private bool isDiffModeEnabled_;
        private bool syncDiffedDocuments_;
        private bool isFunctionListVisible_;
        private SortAdorner listViewSortAdorner;
        private GridViewColumnHeader listViewSortCol;
        private IRTextSummary otherSummary_;
        private bool sectionExtensionComputed_;
        private Dictionary<IRTextSection, IRTextSectionEx> sectionExtMap_;
        private List<IRTextSectionEx> sections_;

        private ScrollViewer sectionsScrollViewer_;
        private IRTextSummary summary_;

        public SectionPanel() {
            InitializeComponent();
            sections_ = new List<IRTextSectionEx>();
            sectionExtMap_ = new Dictionary<IRTextSection, IRTextSectionEx>();
            annotatedSections_ = new HashSet<IRTextSectionEx>();
            IsFunctionListVisible = true;
            SyncDiffedDocuments = true;
            MainGrid.DataContext = this;
            sectionSettings_ = App.Settings.SectionSettings;
        }

        public bool BottomSectionToolbar {
            get => (bool)GetValue(BottomSectionToolbarProperty);
            set => SetValue(BottomSectionToolbarProperty, value);
        }

        public bool FunctionPartVisible {
            get => (bool)GetValue(FunctionPartVisibleProperty);
            set => SetValue(FunctionPartVisibleProperty, value);
        }

        public bool HideToolbars {
            get => (bool)GetValue(HideToolbarsProperty);
            set => SetValue(HideToolbarsProperty, value);
        }

        public bool IsFunctionListVisible {
            get => isFunctionListVisible_;
            set {
                if (isFunctionListVisible_ != value) {
                    isFunctionListVisible_ = value;

                    if (isFunctionListVisible_) {
                        FunctionList.Visibility = Visibility.Visible;
                        FunctionToolbarTray.Visibility = Visibility.Visible;
                        MainGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                        MainGrid.ColumnDefinitions[1].Width = new GridLength(2, GridUnitType.Pixel);
                        MainGrid.ColumnDefinitions[2].Width = new GridLength(2, GridUnitType.Star);
                    }
                    else {
                        FunctionList.Visibility = Visibility.Collapsed;
                        FunctionToolbarTray.Visibility = Visibility.Collapsed;
                        MainGrid.ColumnDefinitions[0].Width = new GridLength(24, GridUnitType.Pixel);
                        MainGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
                        MainGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                    }

                    OnPropertyChange(nameof(IsFunctionListVisible));
                }
            }
        }

        public string DocumentTitle {
            get => documentTitle_;
            set {
                if (documentTitle_ != value) {
                    documentTitle_ = value;
                    OnPropertyChange(nameof(DocumentTitle));
                }
            }
        }

        public bool IsDiffModeEnabled {
            get => isDiffModeEnabled_;
            set {
                if (isDiffModeEnabled_ != value) {
                    isDiffModeEnabled_ = value;
                    OnPropertyChange(nameof(IsDiffModeEnabled));
                }
            }
        }

        public bool SyncDiffedDocuments {
            get => syncDiffedDocuments_;
            set {
                if (syncDiffedDocuments_ != value) {
                    syncDiffedDocuments_ = value;
                    OnPropertyChange(nameof(SyncDiffedDocuments));
                    SyncDiffedDocumentsChanged?.Invoke(this, value);
                }
            }
        }

        public IRTextSummary Summary {
            get => summary_;
            set {
                if (value != summary_) {
                    summary_ = value;
                    sectionExtensionComputed_ = false;
                    UpdateFunctionListBindings();
                }
            }
        }

        public IRTextSummary OtherSummary {
            get => summary_;
            set {
                if (value != otherSummary_) {
                    otherSummary_ = value;
                    RefreshFunctionList();
                }
            }
        }

        public IRTextFunction CurrentFunction => currentFunction_;
        public bool HasAnnotatedSections => annotatedSections_.Count > 0;
        public ICompilerInfoProvider CompilerInfo { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        private static void OnBottomSectionToolbarPropertyChanged(
            DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var source = d as SectionPanel;
            bool placeBottom = (bool)e.NewValue;

            if (placeBottom) {
                source.FunctionPartVisible = false;
                var row0 = source.SectionGrid.RowDefinitions[0];
                var row1 = source.SectionGrid.RowDefinitions[1];
                double row0Value = row0.Height.Value;
                var row0Type = row0.Height.GridUnitType;

                source.SectionGrid.RowDefinitions[0].Height =
                    new GridLength(row1.Height.Value, row1.Height.GridUnitType);

                source.SectionGrid.RowDefinitions[1].Height = new GridLength(row0Value, row0Type);
                Grid.SetRow(source.SectionList, 0);
                Grid.SetRow(source.SectionToolbarGrid, 1);

                // Hide the settings button.
                source.FixedToolbar.Visibility = Visibility.Collapsed;
                source.SectionToolbarGrid.ColumnDefinitions[1].Width = new GridLength(0);
            }
        }

        private static void OnFunctionPartVisiblePropertyChanged(DependencyObject d,
                                                                 DependencyPropertyChangedEventArgs e) {
            var source = d as SectionPanel;
            bool visible = (bool)e.NewValue;

            if (visible) {
                source.FunctionPart.Visibility = Visibility.Visible;
            }
            else {
                //? TODO: Can be moved to XAML, similar to RemarkPanel.xaml
                source.MainGrid.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Pixel);
                source.MainGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
                source.MainGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }

        private static void OnHideToolbarsPropertyChanged(DependencyObject d,
                                                          DependencyPropertyChangedEventArgs e) {
            var source = d as SectionPanel;
            bool hide = (bool)e.NewValue;

            if (hide) {
                source.SectionToolbarGrid.Visibility = Visibility.Collapsed;
                source.SectionGrid.RowDefinitions[0].Height = new GridLength(0, GridUnitType.Pixel);
            }
        }

        public void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations) {
            if (!sectionSettings_.MarkAnnotatedSections) {
                return;
            }

            if (!sectionExtMap_.ContainsKey(section)) {
                return;
            }

            var sectionExt = sectionExtMap_[section];

            if (hasAnnotations) {
                sectionExt.IsTagged = true;
                annotatedSections_.Add(sectionExt);
            }
            else if (annotatedSections_.Contains(sectionExt)) {
                sectionExt.IsTagged = false;
                annotatedSections_.Remove(sectionExt);
            }
        }

        public void SelectSection(IRTextSection section, bool focus = true) {
            UpdateSectionListBindings(section.ParentFunction);
            var sectionEx = sections_.Find(item => item.Section == section);
            SectionList.SelectedItem = sectionEx;

            if (sectionEx != null) {
                MarkCurrentSection(sectionEx);
                BringIntoViewSelectedListItem(SectionList, focus);
            }
        }

        private void BringIntoViewSelectedListItem(ListView list, bool focus) {
            list.ScrollIntoView(list.SelectedItem);

            if (focus) {
                var item = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as ListViewItem;
                item?.Focus();
            }
        }

        public event EventHandler<IRTextFunction> FunctionSwitched;
        public event EventHandler<OpenSectionEventArgs> OpenSection;
        public event EventHandler<DiffModeEventArgs> EnterDiffMode;
        public event EventHandler<double> SectionListScrollChanged;
        public event EventHandler<bool> SyncDiffedDocumentsChanged;
        public event EventHandler<DisplayCallGraphEventArgs> DisplayCallGraph;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        public void RegisterSectionListScrollEvent() {
            if (sectionsScrollViewer_ != null) {
                return;
            }

            // The internal ScrollViewer is not created until items are added,
            // this is the reason the event is registered in the constructor.
            sectionsScrollViewer_ = Utils.FindChild<ScrollViewer>(SectionList);

            if (sectionsScrollViewer_ != null) {
                sectionsScrollViewer_.ScrollChanged += SectionsScrollViewer__ScrollChanged;
            }
        }

        private void SectionsScrollViewer__ScrollChanged(object sender, ScrollChangedEventArgs e) {
            SectionListScrollChanged?.Invoke(this, e.VerticalOffset);
        }

        private void UpdateFunctionListBindings() {
            if (summary_ == null) {
                otherSummary_ = null;
                currentFunction_ = null;
                sections_.Clear();
                sectionExtMap_.Clear();
                annotatedSections_.Clear();
                SectionList.ItemsSource = null;
                FunctionList.ItemsSource = null;
                SectionList.UpdateLayout();
                FunctionList.UpdateLayout();
                return;
            }

            SetupSectionExtension();
            var functionFilter = new ListCollectionView(summary_.Functions);
            functionFilter.Filter = FilterFunctionList;
            FunctionList.ItemsSource = functionFilter;
            SectionList.ItemsSource = null;

            if (summary_.Functions.Count == 1) {
                SelectFunction(summary_.Functions[0]);
            }
        }

        private void SetupSectionExtension() {
            if (sectionExtensionComputed_) {
                return;
            }

            sectionExtMap_.Clear();
            annotatedSections_.Clear();

            foreach (var func in summary_.Functions) {
                int index = 0;

                foreach (var section in func.Sections) {
                    var sectionEx = new IRTextSectionEx(section, index++);
                    sectionExtMap_[section] = sectionEx;
                }
            }

            sectionExtensionComputed_ = true;
        }

        private void UpdateSectionListBindings(IRTextFunction function, bool force = false) {
            if (function == currentFunction_ && !force) {
                return;
            }

            Summary = function.ParentSummary;
            currentFunction_ = function;
            FunctionList.SelectedItem = function;

            if (function == null) {
                return;
            }

            Sections = CreateSectionsExtension();
            FunctionSwitched?.Invoke(this, currentFunction_);
        }

        public List<IRTextSectionEx> CreateSectionsExtension() {
            SetupSectionExtension();
            var sections = new List<IRTextSectionEx>();
            int sectionIndex = 0;

            if (currentFunction_ == null) {
                return sections;
            }

            foreach (var section in currentFunction_.Sections) {
                var sectionEx = sectionExtMap_[section];
                sectionEx = new IRTextSectionEx(section, sectionEx.Index);
                sectionEx.Name = CompilerInfo.NameProvider.GetSectionName(section);

                sectionExtMap_[section] = sectionEx;
                sections.Add(sectionEx);

                if (CompilerInfo.SectionStyleProvider.IsMarkedSection(section, out var markedName)) {
                    // Section name has a custom style, based on settings apply it.
                    if (sectionSettings_.ColorizeSectionNames) {
                        sectionEx.IsMarked = true;
                        sectionEx.TextColor = ColorBrushes.GetBrush(markedName.TextColor);
                    }
                    else {
                        sectionEx.IsMarked = false;
                    }

                    if (sectionSettings_.ShowSectionSeparators) {
                        ApplySectionBorder(sectionEx, sectionIndex, markedName, sections);
                    }
                    else {
                        sectionEx.BorderThickness = new Thickness();
                    }

                    if (sectionSettings_.UseNameIndentation &&
                        markedName.IndentationLevel > 0) {
                        ApplySectionNameIndentation(sectionEx, markedName);
                    }
                }
                else {
                    sectionEx.IsMarked = false;
                    sectionEx.BorderThickness = new Thickness();
                }

                sectionEx.SectionDiffKind = DiffKind.None;
                sectionIndex++;
            }

            return sections;
        }

        private void ApplySectionBorder(IRTextSectionEx sectionEx, int sectionIndex,
                                        MarkedSectionName markedName,
                                        List<IRTextSectionEx> sections) {
            // Don't show the border for the first and last sections in the list,
            // and if there is a before-border following an after-border.
            bool useBeforeBorder = sectionIndex > 0 && !sections[sectionIndex - 1].HasAfterBorder;
            bool useAfterBorder = sectionIndex < (currentFunction_.Sections.Count - 1);

            sectionEx.BorderThickness = new Thickness(0, useBeforeBorder ? markedName.BeforeSeparatorWeight : 0,
                                                      0, useAfterBorder ? markedName.AfterSeparatorWeight : 0);
            sectionEx.BorderBrush = ColorBrushes.GetBrush(markedName.SeparatorColor);
        }

        private void ApplySectionNameIndentation(IRTextSectionEx sectionEx, MarkedSectionName markedName) {
            int level = markedName.IndentationLevel;
            var builder = new StringBuilder(sectionEx.Name.Length + level * sectionSettings_.IndentationAmount);

            while (level > 0) {
                builder.Append(' ', sectionSettings_.IndentationAmount);
                level--;
            }

            builder.Append(sectionEx.Name);
            sectionEx.Name = builder.ToString();
        }

        private void UpdateSectionListView() {
            var sectionFilter = new ListCollectionView(sections_);
            sectionFilter.Filter = FilterSectionList;
            SectionList.ItemsSource = sectionFilter;

            if (SectionList.Items.Count > 0) {
                SectionList.SelectedItem = SectionList.Items[0];
                SectionList.ScrollIntoView(SectionList.SelectedItem);
            }

            RegisterSectionListScrollEvent();
        }

        private bool FilterFunctionList(object value) {
            var function = (IRTextFunction)value;

            // In two-document diff mode, show only functions
            // that are common in the two summaries.
            if (otherSummary_ != null) {
                if (otherSummary_.FindFunction(function) == null) {
                    return false;
                }
            }

            string text = FunctionFilter.Text.Trim();
            return text.Length <= 0 ||
                (App.Settings.SectionSettings.FunctionSearchCaseSensitive ?
                function.Name.Contains(text, StringComparison.Ordinal) :
                function.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        private bool FilterSectionList(object value) {
            var section = (IRTextSectionEx)value;

            if (section.IsSelected) {
                return true;
            }

            if (FilterTagged.IsChecked.HasValue && FilterTagged.IsChecked.Value && !section.IsTagged) {
                return false;
            }

            string text = SectionFilter.Text.Trim();
            return text.Length <= 0 ||
                (App.Settings.SectionSettings.SectionSearchCaseSensitive ?
                section.Name.Contains(text, StringComparison.Ordinal) :
                section.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        private void ShowFunctionsExecuted(object sender, ExecutedRoutedEventArgs e) {
            IsFunctionListVisible = true;
        }

        private void ExecuteClearTextbox(object sender, ExecutedRoutedEventArgs e) {
            ((TextBox)e.Parameter).Text = string.Empty;
        }

        private void OpenExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (SectionList.SelectedItems.Count == 2) {
                OpenSideBySideExecuted(sender, e);
                return;
            }

            if (e.Parameter is IRTextSectionEx section) {
                OpenSectionExecute(section, OpenSectionKind.ReplaceCurrent);
            }
        }

        private void OpenInNewTabExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (SectionList.SelectedItems.Count == 2) {
                DiffSideBySideExecuted(sender, e);
                return;
            }

            var section = e.Parameter as IRTextSectionEx;

            if (section != null) {
                OpenSectionExecute(section, OpenSectionKind.NewTab);
            }
        }

        private void OpenLeftExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is IRTextSectionEx section) {
                OpenSectionExecute(section, OpenSectionKind.NewTabDockLeft);
            }
        }

        private void OpenRightExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is IRTextSectionEx section) {
                OpenSectionExecute(section, OpenSectionKind.NewTabDockRight);
            }
        }

        private void OpenSideBySideExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (SectionList.SelectedItems.Count == 2) {
                var leftSection = SectionList.SelectedItems[0] as IRTextSectionEx;
                var rightSection = SectionList.SelectedItems[1] as IRTextSectionEx;
                OpenSectionExecute(leftSection, OpenSectionKind.NewTabDockLeft);
                OpenSectionExecute(rightSection, OpenSectionKind.NewTabDockRight);
            }
        }

        private void OpenSideBySideCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = SectionList.SelectedItems.Count == 2;
            e.Handled = true;
        }

        private void DiffWithOtherDocumentCanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = IsDiffModeEnabled;
            e.Handled = true;
        }

        private void SyncDiffedDocumentsExecuted(object sender, ExecutedRoutedEventArgs e) {
            e.Handled = true;
        }

            private void DiffSideBySideExecuted(object sender, ExecutedRoutedEventArgs e) {
            var leftSectionEx = SectionList.SelectedItems[0] as IRTextSectionEx;
            var rightSectionEx = SectionList.SelectedItems[1] as IRTextSectionEx;

            var args = new DiffModeEventArgs {
                Left = new OpenSectionEventArgs(leftSectionEx.Section, OpenSectionKind.NewTabDockLeft),
                Right = new OpenSectionEventArgs(rightSectionEx.Section, OpenSectionKind.NewTabDockRight)
            };

            EnterDiffMode?.Invoke(this, args);
        }

        private void DiffWithOtherDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            var sectionEx = SectionList.SelectedItems[0] as IRTextSectionEx;

            var args = new DiffModeEventArgs {
                IsWithOtherDocument = true,
                Left = new OpenSectionEventArgs(sectionEx.Section, OpenSectionKind.NewTabDockLeft)
            };

            EnterDiffMode?.Invoke(this, args);
        }

        public void DiffSelectedSection() {
            if (SectionList.SelectedItem != null) {
                var sectionEx = SectionList.SelectedItem as IRTextSectionEx;

                var args = new DiffModeEventArgs {
                    IsWithOtherDocument = true,
                    Left = new OpenSectionEventArgs(sectionEx.Section, OpenSectionKind.NewTabDockLeft)
                };

                EnterDiffMode?.Invoke(this, args);
            }
        }

        private void ToggleTagExecuted(object sender, ExecutedRoutedEventArgs e) {
            var section = e.Parameter as IRTextSectionEx;

            if (section != null) {
                section.IsTagged = !section.IsTagged;
            }
        }

        private void FocusSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
            SectionFilter.Focus();
            SectionFilter.SelectAll();
        }

        private void RefreshSectionList() {
            if (SectionList.ItemsSource == null) {
                return;
            }

            ((ListCollectionView)SectionList.ItemsSource).Refresh();
        }

        private void RefreshFunctionList() {
            if (FunctionList.ItemsSource == null) {
                return;
            }

            ((ListCollectionView)FunctionList.ItemsSource).Refresh();
        }

        private void FunctionList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (e.AddedItems.Count == 1) {
                UpdateSectionListBindings((IRTextFunction)e.AddedItems[0]);
            }
        }

        private void SectionFilter_TextChanged(object sender, TextChangedEventArgs e) {
            SectionList.Focus();
            RefreshSectionList();
            SectionFilter.Focus();
        }

        private void FunctionFilter_TextChanged(object sender, TextChangedEventArgs e) {
            FunctionList.Focus();
            RefreshFunctionList();
            FunctionFilter.Focus();
        }

        private void SectionDoubleClick(object sender, MouseButtonEventArgs e) {
            var section = ((ListViewItem)sender).Content as IRTextSectionEx;

            bool inNewTab = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ||
                            (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            OpenSectionExecute(section, inNewTab ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent);
        }

        private void OpenSectionExecute(IRTextSectionEx value, OpenSectionKind kind,
                                        IRDocumentHost targetDocument = null) {
            if (OpenSection != null && value.Section != null) {
                MarkCurrentSection(value);
                OpenSection(this, new OpenSectionEventArgs(value.Section, kind, targetDocument));
            }
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e) {
            SwitchToSection(-1);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e) {
            SwitchToSection(1);
        }

        private void MarkCurrentSection(IRTextSectionEx section) {
            foreach (var item in sections_) {
                item.IsSelected = false;
            }

            section.IsSelected = true;
        }

        public bool SwitchToSection(int offset, IRDocumentHost targetDocument = null) {
            int newIndex = SectionList.SelectedIndex + offset;

            while (newIndex >= 0 && newIndex < SectionList.Items.Count) {
                var section = Sections[newIndex];

                if (!section.IsPlaceholderDiff) {
                    // Not a diff mode placeholder, switch.
                    SwitchToSection(section, targetDocument);
                    return true;
                }

                // Go left or right to find first real section.
                if (offset > 0) {
                    newIndex++;
                }
                else {
                    newIndex--;
                }
            }

            return false;
        }

        public void SwitchToSection(IRTextSectionEx section, IRDocumentHost targetDocument = null) {
            SectionList.SelectedItem = section;
            SectionList.ScrollIntoView(SectionList.SelectedItem);
            OpenSectionExecute(section, OpenSectionKind.ReplaceCurrent, targetDocument);
        }

        public void SwitchToSection(IRTextSection section, IRDocumentHost targetDocument = null) {
            SwitchToSection(Sections.Find(item => item.Section == section), targetDocument);
        }

        private void FilterTagged_Checked(object sender, RoutedEventArgs e) {
            ((ListCollectionView)SectionList.ItemsSource).Refresh();
        }

        private void FilterTagged_Unchecked(object sender, RoutedEventArgs e) {
            ((ListCollectionView)SectionList.ItemsSource).Refresh();
        }

        private void CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
            e.CanExecute = true;
            e.Handled = true;
        }

        private void PreviousSectionExecuted(object sender, ExecutedRoutedEventArgs e) {
            SwitchToSection(-1);
            e.Handled = true;
        }

        private void NextSectionExecuted(object sender, ExecutedRoutedEventArgs e) {
            SwitchToSection(1);
            e.Handled = true;
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e) {
            var column = sender as GridViewColumnHeader;

            if (listViewSortCol != null) {
                AdornerLayer.GetAdornerLayer(listViewSortCol).Remove(listViewSortAdorner);
            }

            var sortingDirection = ListSortDirection.Ascending;

            if (listViewSortCol == column && listViewSortAdorner.Direction == sortingDirection) {
                sortingDirection = ListSortDirection.Descending;
            }

            listViewSortCol = column;
            listViewSortAdorner = new SortAdorner(listViewSortCol, sortingDirection);
            AdornerLayer.GetAdornerLayer(listViewSortCol).Add(listViewSortAdorner);
            SectionSorter.FieldKind sortingField;

            if (sender == NumberColumnHeader) {
                sortingField = SectionSorter.FieldKind.Number;
            }
            else if (sender == BlocksColumnHeader) {
                sortingField = SectionSorter.FieldKind.Blocks;
            }
            else {
                sortingField = SectionSorter.FieldKind.Name;
            }

            if (!(SectionList.ItemsSource is ListCollectionView view)) {
                return; // No function selected yet.
            }

            view.CustomSort = new SectionSorter(sortingField, sortingDirection);
            SectionList.Items.Refresh();
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        public void ScrollSectionList(double offset) {
            RegisterSectionListScrollEvent();
            sectionsScrollViewer_?.ScrollToVerticalOffset(offset);
        }

        private sealed class SectionSorter : IComparer {
            public enum FieldKind {
                Number,
                Name,
                Blocks
            }

            private ListSortDirection direction_;

            private FieldKind sortingField_;

            public SectionSorter(FieldKind sortingField, ListSortDirection direction) {
                sortingField_ = sortingField;
                direction_ = direction;
            }

            public int Compare(object x, object y) {
                var sectionX = x as IRTextSectionEx;
                var sectionY = y as IRTextSectionEx;

                switch (sortingField_) {
                    case FieldKind.Number: {
                        int result = sectionY.Number - sectionX.Number;
                        return direction_ == ListSortDirection.Ascending ? -result : result;
                    }
                    case FieldKind.Name: {
                        int result = string.Compare(sectionY.Name, sectionX.Name, StringComparison.Ordinal);
                        return direction_ == ListSortDirection.Ascending ? -result : result;
                    }
                    case FieldKind.Blocks: {
                        int result = sectionY.BlockCount - sectionX.BlockCount;
                        return direction_ == ListSortDirection.Ascending ? -result : result;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return 0;
            }
        }

        public sealed class SortAdorner : Adorner {
            private static Geometry ascGeometry = Geometry.Parse("M 0 4 L 3.5 0 L 7 4 Z");

            private static Geometry descGeometry = Geometry.Parse("M 0 0 L 3.5 4 L 7 0 Z");

            public SortAdorner(UIElement element, ListSortDirection dir) : base(element) {
                Direction = dir;
            }

            public ListSortDirection Direction { get; private set; }

            protected override void OnRender(DrawingContext drawingContext) {
                base.OnRender(drawingContext);

                if (AdornedElement.RenderSize.Width < 20) {
                    return;
                }

                var transform = new TranslateTransform(AdornedElement.RenderSize.Width - 15,
                                                       (AdornedElement.RenderSize.Height - 5) / 2);

                drawingContext.PushTransform(transform);
                var geometry = ascGeometry;

                if (Direction == ListSortDirection.Descending) {
                    geometry = descGeometry;
                }

                drawingContext.DrawGeometry(Brushes.Black, null, geometry);
                drawingContext.Pop();
            }
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Section;
        public override bool SavesStateToFile => true;

        public List<IRTextSectionEx> Sections {
            get => sections_;
            set {
                if (sections_ != value) {
                    sections_ = value;

                    foreach (var sectionEx in sections_) {
                        if (sectionEx.Section != null) {
                            sectionExtMap_[sectionEx.Section] = sectionEx;
                        }
                    }

                    UpdateSectionListView();
                }
            }
        }

        public IRTextSectionEx GetSectionExtension(IRTextSection section) {
            return sectionExtMap_[section];
        }

        public override void OnSessionStart() {
            base.OnSessionStart();
            var data = Session.LoadPanelState(this, null);

            if (data != null) {
                var state = StateSerializer.Deserialize<SectionPanelState>(data);

                foreach (ulong sectionId in state.AnnotatedSections) {
                    var section = summary_.GetSectionWithId(sectionId);
                    var sectionExt = sectionExtMap_[section];
                    sectionExt.IsTagged = true;
                    annotatedSections_.Add(sectionExt);
                }

                if (state.SelectedFunctionNumber > 0 &&
                    state.SelectedFunctionNumber < summary_.Functions.Count) {
                    SelectFunction(summary_.Functions[state.SelectedFunctionNumber]);
                    BringIntoViewSelectedListItem(FunctionList, false);
                }
            }

            // Reset search filters.
            FunctionFilter.Text = "";
            SectionFilter.Text = "";
        }

        public void SelectFunction(IRTextFunction function) {
            if (function == currentFunction_) {
                return;
            }

            FunctionList.SelectedItem = function;
            FunctionList.ScrollIntoView(FunctionList.SelectedItem);
            RefreshSectionList();
        }

        public override void OnSessionSave() {
            base.OnSessionStart();
            var state = new SectionPanelState();
            state.AnnotatedSections = annotatedSections_.ToList(item => item.Section.Id);

            state.SelectedFunctionNumber = FunctionList.SelectedItem != null
                ? ((IRTextFunction)FunctionList.SelectedItem).Number : 0;

            var data = StateSerializer.Serialize(state);
            Session.SavePanelState(data, this, null);
        }

        #endregion

        private OptionsPanelHostWindow optionsPanelWindow_;
        private bool optionsPanelVisible_;
        private SectionSettings sectionSettings_;

        private void FixedToolbar_SettingsClicked(object sender, EventArgs e) {
            if (optionsPanelVisible_) {
                CloseOptionsPanel();
            }
            else {
                ShowOptionsPanel();
            }
        }

        private void ShowOptionsPanel() {
            if (optionsPanelVisible_ || currentFunction_ == null) {
                return;
            }

            var width = Math.Max(SectionOptionsPanel.MinimumWidth,
                    Math.Min(SectionList.ActualWidth, SectionOptionsPanel.DefaultWidth));
            var height = Math.Max(SectionOptionsPanel.MinimumHeight,
                    Math.Min(SectionList.ActualHeight, SectionOptionsPanel.DefaultHeight));
            var position = SectionList.PointToScreen(new Point(SectionList.ActualWidth - width, 0));

            optionsPanelWindow_ = new OptionsPanelHostWindow(new SectionOptionsPanel(CompilerInfo),
                                                             position, width, height, this);
            optionsPanelWindow_.PanelClosed += OptionsPanel_PanelClosed;
            optionsPanelWindow_.PanelReset += OptionsPanel_PanelReset;
            optionsPanelWindow_.SettingsChanged += OptionsPanel_SettingsChanged;
            optionsPanelWindow_.Settings = (SectionSettings)sectionSettings_.Clone();
            optionsPanelWindow_.IsOpen = true;
            optionsPanelVisible_ = true;
        }

        private void OptionsPanel_SettingsChanged(object sender, bool force) {
            var newSettings = (SectionSettings)optionsPanelWindow_.Settings;
            HandleNewDiffSettings(newSettings, false, force);
            optionsPanelWindow_.Settings = null;
            optionsPanelWindow_.Settings = (SectionSettings)sectionSettings_.Clone();
        }

        private void OptionsPanel_PanelReset(object sender, EventArgs e) {
            HandleNewDiffSettings(new SectionSettings(), true);

            //? TODO: Setting to null should be part of OptionsPanelBase and remove it in all places
            optionsPanelWindow_.Settings = null;
            optionsPanelWindow_.Settings = (SectionSettings)sectionSettings_.Clone();
        }

        private void OptionsPanel_PanelClosed(object sender, EventArgs e) {
            CloseOptionsPanel();
        }

        private void CloseOptionsPanel() {
            if (!optionsPanelVisible_) {
                return;
            }

            optionsPanelWindow_.IsOpen = false;
            optionsPanelWindow_.PanelClosed -= OptionsPanel_PanelClosed;
            optionsPanelWindow_.PanelReset -= OptionsPanel_PanelReset;
            optionsPanelWindow_.SettingsChanged -= OptionsPanel_SettingsChanged;

            var newSettings = (SectionSettings)optionsPanelWindow_.Settings;
            HandleNewDiffSettings(newSettings, true);

            optionsPanelWindow_ = null;
            optionsPanelVisible_ = false;
        }

        private void HandleNewDiffSettings(SectionSettings newSettings, bool commit, bool force = false) {
            if (commit) {
                App.Settings.SectionSettings = newSettings;
                App.SaveApplicationSettings();
            }

            if (newSettings.Equals(sectionSettings_) && !force) {
                return;
            }

            App.Settings.SectionSettings = newSettings;
            sectionSettings_ = newSettings;
            UpdateSectionListBindings(currentFunction_, true);
            RefreshSectionList();
        }

        private async void CopySectionTextExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is IRTextSectionEx sectionEx) {
                var text = await Session.GetSectionTextAsync(sectionEx.Section);
                Clipboard.SetText(text);
            }
        }

        private async void SaveSectionTextExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is IRTextSectionEx sectionEx) {
                var fileDialog = new SaveFileDialog {
                    DefaultExt = "*.txt|All Files|*.*",
                    Filter = "IR text|*.txt"
                };

                var result = fileDialog.ShowDialog();

                if (result.HasValue && result.Value) {
                    var path = fileDialog.FileName;

                    try {
                        var text = await Session.GetSectionTextAsync(sectionEx.Section);
                        await File.WriteAllTextAsync(path, text);
                    }
                    catch (Exception ex) {
                        using var centerForm = new DialogCenteringHelper(this);
                        MessageBox.Show($"Failed to save IR text file {path}: {ex.Message}", "IR Explorer",
                                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    }
                }
            }
        }

        private async void SaveAllSectionTextExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is IRTextSectionEx sectionEx) {
                var fileDialog = new SaveFileDialog {
                    DefaultExt = "*.txt|All Files|*.*",
                    Filter = "IR text|*.txt"
                };

                var result = fileDialog.ShowDialog();

                if (result.HasValue && result.Value) {
                    var path = fileDialog.FileName;

                    try {
                        var text = await Session.GetDocumentTextAsync(sectionEx.Section.ParentFunction.ParentSummary);
                        await File.WriteAllTextAsync(path, text);
                    }
                    catch (Exception ex) {
                        using var centerForm = new DialogCenteringHelper(this);
                        MessageBox.Show($"Failed to save IR text file {path}: {ex.Message}", "IR Explorer",
                                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    }
                }
            }
        }

        private async void DisplayCallGraphExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is IRTextSectionEx sectionEx) {
                DisplayCallGraph?.Invoke(this, new DisplayCallGraphEventArgs(Summary, sectionEx.Section, false));
            }
        }

        private async void DisplayPartialCallGraphExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is IRTextSectionEx sectionEx) {
                DisplayCallGraph?.Invoke(this, new DisplayCallGraphEventArgs(Summary, sectionEx.Section, true));
            }
        }

        private void ToolBar_SizeChanged(object sender, SizeChangedEventArgs e) {
            ResizeFunctionFilter(e.NewSize.Width);
        }

        private void ResizeFunctionFilter(double width) {
            //? TODO: Hacky way to resize the function search textbox in the toolbar
            //? when the toolbar gets smaller - couldn't find another way to do this in WPF...
            FunctionFilterGrid.Width = Math.Max(1, width - 60);
        }

        public override void OnThemeChanged() {
            HandleNewDiffSettings(App.Settings.SectionSettings, false, true);
        }
    }
}