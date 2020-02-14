// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Core;
using ProtoBuf;

namespace Client {
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

    }

    public enum OpenSectionKind {
        ReplaceCurrent,
        ReplaceLeft,
        ReplaceRight,
        NewTab,
        NewTabDockLeft,
        NewTabDockRight
    }

    public class OpenSectionEventArgs : EventArgs {
        public IRTextSection Section { get; set; }
        public OpenSectionKind OpenKind { get; set; }
        public IRDocumentHost TargetDocument { get; set; }

        public OpenSectionEventArgs(IRTextSection section, OpenSectionKind kind, 
                                    IRDocumentHost targetDocument = null) {
            Section = section;
            OpenKind = kind;
            TargetDocument = targetDocument;
        }
    }

    public class DiffModeEventArgs : EventArgs {
        public bool IsWithOtherDocument { get; set; }
        public OpenSectionEventArgs Left { get; set; }
        public OpenSectionEventArgs Right { get; set; }
    }

    public class IRTextSectionExtension : INotifyPropertyChanged {
        public IRTextSection Section { get; set; }
        public string Name { get; set; }

        private DiffKind sectionDiffKind_;
        public DiffKind SectionDiffKind {
            get { return sectionDiffKind_; }
            set {
                sectionDiffKind_ = value;
                OnPropertyChange(nameof(IsInsertionDiff));
                OnPropertyChange(nameof(IsDeletionDiff));
                OnPropertyChange(nameof(IsPlaceholderDiff));
                OnPropertyChange(nameof(HasDiffs));
            }
        }


        private bool isTagged_;
        public bool IsTagged {
            get { return isTagged_; }
            set { isTagged_ = value; OnPropertyChange("IsTagged"); }
        }

        private bool isSelected_;
        public bool IsSelected {
            get { return isSelected_; }
            set { isSelected_ = value; OnPropertyChange("IsSelected"); }
        }

        public bool IsMarked { get; set; }
        public bool IsInsertionDiff => SectionDiffKind == DiffKind.Insertion;
        public bool IsDeletionDiff => SectionDiffKind == DiffKind.Deletion;
        public bool IsPlaceholderDiff => SectionDiffKind == DiffKind.Placeholder;
        public bool HasDiffs => SectionDiffKind == DiffKind.Modification;

        public Brush TextColor { get; set; }
        public Brush BackColor { get; set; }

        public string NumberString => (Section != null) ? Section.Number.ToString() : "";
        public string BlockCountString => (Section != null) ? Section.BlockCount.ToString() : "";

        public int Number => (Section != null) ? Section.Number : 0;
        public int BlockCount => (Section != null) ? Section.BlockCount : 0;


        public IRTextSectionExtension(IRTextSection section) {
            Section = section;
            SectionDiffKind = DiffKind.None;
        }

        public IRTextSectionExtension(IRTextSection section, DiffKind diffKind, string name) {
            Section = section;
            SectionDiffKind = diffKind;
            Name = name;
        }

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
        public bool BottomSectionToolbar {
            get { return (bool)GetValue(BottomSectionToolbarProperty); }
            set { SetValue(BottomSectionToolbarProperty, value); }
        }

        public static readonly DependencyProperty BottomSectionToolbarProperty =
            DependencyProperty.Register(
                "BottomSectionToolbar",
                typeof(bool),
                typeof(SectionPanel),
                new PropertyMetadata(false, OnBottomSectionToolbarPropertyChanged));

        private static void OnBottomSectionToolbarPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            SectionPanel source = d as SectionPanel;
            bool placeBottom = (bool)e.NewValue;

            if (placeBottom) {
                source.FunctionPartVisible = false;

                var row0 = source.SectionGrid.RowDefinitions[0];
                var row1 = source.SectionGrid.RowDefinitions[1];
                var row0Value = row0.Height.Value;
                var row0Type = row0.Height.GridUnitType;

                source.SectionGrid.RowDefinitions[0].Height = new GridLength(row1.Height.Value, row1.Height.GridUnitType);
                source.SectionGrid.RowDefinitions[1].Height = new GridLength(row0Value, row0Type);
                Grid.SetRow(source.SectionList, 0);
                Grid.SetRow(source.SectionToolbarGrid, 1);
            }
        }

        public bool FunctionPartVisible {
            get { return (bool)GetValue(FunctionPartVisibleProperty); }
            set { SetValue(FunctionPartVisibleProperty, value); }
        }

        public static readonly DependencyProperty FunctionPartVisibleProperty =
            DependencyProperty.Register(
                "FunctionPartVisible",
                typeof(bool),
                typeof(SectionPanel),
                new PropertyMetadata(true, OnFunctionPartVisiblePropertyChanged));

        private static void OnFunctionPartVisiblePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            SectionPanel source = d as SectionPanel;
            bool visible = (bool)e.NewValue;

            if (visible) {
                source.FunctionPart.Visibility = Visibility.Visible;
            }
            else {
                //source.FixedToolbar.Visibility = Visibility.Collapsed;
                source.MainGrid.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Pixel);
                source.MainGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
                source.MainGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            }
        }

        public bool HideToolbars {
            get { return (bool)GetValue(HideToolbarsProperty); }
            set { SetValue(HideToolbarsProperty, value); }
        }

        public static readonly DependencyProperty HideToolbarsProperty =
            DependencyProperty.Register(
                "HideToolbars",
                typeof(bool),
                typeof(SectionPanel),
                new PropertyMetadata(false, OnHideToolbarsPropertyChanged));

        private static void OnHideToolbarsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            SectionPanel source = d as SectionPanel;
            bool hide = (bool)e.NewValue;

            if (hide) {
                source.SectionToolbarGrid.Visibility = Visibility.Collapsed;
                source.SectionGrid.RowDefinitions[0].Height = new GridLength(0, GridUnitType.Pixel);
            }
        }

        private bool isFunctionListVisible_;
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

                    OnPropertyChange("IsFunctionListVisible");
                }
            }
        }

        private string documentTitle_;
        public string DocumentTitle {
            get => documentTitle_;
            set {
                if (documentTitle_ != value) {
                    documentTitle_ = value;
                    OnPropertyChange("DocumentTitle");
                }
            }
        }

        private bool isDiffModeEnabled_;
        public bool IsDiffModeEnabled {
            get => isDiffModeEnabled_;
            set {
                if (isDiffModeEnabled_ != value) {
                    isDiffModeEnabled_ = value;
                    OnPropertyChange("IsDiffModeEnabled");
                }
            }
        }


        private ScrollViewer sectionsScrollViewer_;
        private List<IRTextSectionExtension> sections_;
        private Dictionary<IRTextSection, IRTextSectionExtension> sectionExtMap_;
        private HashSet<IRTextSectionExtension> annotatedSections_;
        private IRTextFunction currentFunction_;
        private IRTextSummary summary_;
        private IRTextSummary otherSummary_;
        private bool sectionExtensionComputed_;

        public IRTextSummary Summary {
            get { return summary_; }
            set {
                if (value != summary_) {
                    summary_ = value;
                    sectionExtensionComputed_ = false;
                    UpdateFunctionListBindings();
                }
            }
        }

        public IRTextSummary OtherSummary {
            get { return summary_; }
            set {
                if (value != otherSummary_) {
                    otherSummary_ = value;
                    RefreshFunctionList();
                }
            }
        }

        public IRTextFunction CurrentFunction => currentFunction_;
        public bool HasAnnotatedSections => annotatedSections_.Count > 0;
        public ICompilerIRInfoProvider CompilerInfo { get; set; }

        public void SetSectionAnnotationState(IRTextSection section, bool hasAnnotations) {
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
            var sectionEx = sections_.Find((item) => item.Section == section);
            SectionList.SelectedItem = sectionEx;

            if (sectionEx != null) {
                MarkCurrentSection(sectionEx);
                BringIntoViewSelectedListItem(SectionList, focus);
            }
        }

        private void BringIntoViewSelectedListItem(ListView list, bool focus) {
            list.ScrollIntoView(list.SelectedItem);

            if (focus) {
                ListViewItem item = list.ItemContainerGenerator.
                            ContainerFromItem(list.SelectedItem) as ListViewItem;
                item?.Focus();
            }
        }

        public event EventHandler<IRTextFunction> FunctionSwitched;
        public event EventHandler<OpenSectionEventArgs> OpenSection;
        public event EventHandler<DiffModeEventArgs> EnterDiffMode;
        public event EventHandler<double> SectionListScrollChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        public SectionPanel() {
            InitializeComponent();

            sections_ = new List<IRTextSectionExtension>();
            sectionExtMap_ = new Dictionary<IRTextSection, IRTextSectionExtension>();
            annotatedSections_ = new HashSet<IRTextSectionExtension>();
            IsFunctionListVisible = true;
            MainGrid.DataContext = this;
        }

        private void RegisterSectionListScrollEvent() {
            if (sectionsScrollViewer_ != null) {
                return;
            }

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
            functionFilter.Filter = new Predicate<object>(FilterFunctionList);
            FunctionList.ItemsSource = functionFilter;

            if (summary_.Functions.Count == 0) {
                SectionList.ItemsSource = null;
            }
        }

        private void SetupSectionExtension() {
            if(sectionExtensionComputed_) {
                return;
            }

            sectionExtMap_.Clear();
            annotatedSections_.Clear();

            foreach (var func in summary_.Functions) {
                foreach (var section in func.Sections) {
                    var sectionEx = new IRTextSectionExtension(section);
                    sectionExtMap_[section] = sectionEx;
                }
            }

            sectionExtensionComputed_ = true;
        }

        private void UpdateSectionListBindings(IRTextFunction function) {
            if (function == currentFunction_) {
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

        public List<IRTextSectionExtension> CreateSectionsExtension() {
            SetupSectionExtension();
            var sections = new List<IRTextSectionExtension>();

            if(currentFunction_ == null) {
                return sections;
            }

            currentFunction_.Sections.ForEach((section) => {
                var sectionEx = sectionExtMap_[section];
                sectionEx.Name = CompilerInfo.NameProvider.GetSectionName(section);

                if (CompilerInfo.StyleProvider.IsMarkedSection(section, out var markedName)) {
                    sectionEx.IsMarked = true;
                    sectionEx.TextColor = markedName.TextColor;
                }

                sectionEx.SectionDiffKind = DiffKind.None;
                sections.Add(sectionEx);
            });

            return sections;
        }

        private void UpdateSectionListView() {
            var functionFilter = new ListCollectionView(sections_);
            functionFilter.Filter = new Predicate<object>(FilterSectionList);
            SectionList.ItemsSource = functionFilter;

            if (SectionList.Items.Count > 0) {
                SectionList.SelectedItem = SectionList.Items[0];
                SectionList.ScrollIntoView(SectionList.SelectedItem);
            }

            RegisterSectionListScrollEvent();
        }

        private bool FilterFunctionList(object value) {
            var function = (Core.IRTextFunction)value;

            // In two-document diff mode, show only functions
            // that are common in the two summaries.
            if(otherSummary_ != null) {
                if(otherSummary_.FindFunction(function) == null) {
                    return false;
                }
            }

            string text = FunctionFilter.Text.Trim().ToLower();

            if (text.Length > 0) {
                return function.Name.ToLower().Contains(text);
            }

            return true;
        }

        private bool FilterSectionList(object value) {
            var section = (IRTextSectionExtension)value;

            if (section.IsSelected) {
                return true;
            }

            if (FilterTagged.IsChecked.HasValue &&
                FilterTagged.IsChecked.Value && !section.IsTagged) {
                return false;
            }

            string text = SectionFilter.Text.Trim().ToLower();

            if (text.Length > 0) {
                return section.Name.ToLower().Contains(text);
            }

            return true;
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

            var section = e.Parameter as IRTextSectionExtension;

            if (section != null) {
                OpenSectionExecute(section, OpenSectionKind.ReplaceCurrent);
            }
        }

        private void OpenInNewTabExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (SectionList.SelectedItems.Count == 2) {
                DiffSideBySideExecuted(sender, e);
                return;
            }

            var section = e.Parameter as IRTextSectionExtension;

            if (section != null) {
                OpenSectionExecute(section, OpenSectionKind.NewTab);
            }
        }

        private void OpenLeftExecuted(object sender, ExecutedRoutedEventArgs e) {
            var section = e.Parameter as IRTextSectionExtension;

            if (section != null) {
                OpenSectionExecute(section, OpenSectionKind.NewTabDockLeft);
            }
        }

        private void OpenRightExecuted(object sender, ExecutedRoutedEventArgs e) {
            var section = e.Parameter as IRTextSectionExtension;

            if (section != null) {
                OpenSectionExecute(section, OpenSectionKind.NewTabDockRight);
            }
        }

        private void OpenSideBySideExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (SectionList.SelectedItems.Count == 2) {
                var leftSection = SectionList.SelectedItems[0] as IRTextSectionExtension;
                var rightSection = SectionList.SelectedItems[1] as IRTextSectionExtension;
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

        private void DiffSideBySideExecuted(object sender, ExecutedRoutedEventArgs e) {
            var leftSectionEx = SectionList.SelectedItems[0] as IRTextSectionExtension;
            var rightSectionEx = SectionList.SelectedItems[1] as IRTextSectionExtension;
            var args = new DiffModeEventArgs() {
                Left = new OpenSectionEventArgs(leftSectionEx.Section, OpenSectionKind.NewTabDockLeft),
                Right = new OpenSectionEventArgs(rightSectionEx.Section, OpenSectionKind.NewTabDockRight)
            };

            EnterDiffMode?.Invoke(this, args);
        }

        private void DiffWithOtherDocumentExecuted(object sender, ExecutedRoutedEventArgs e) {
            var sectionEx = SectionList.SelectedItems[0] as IRTextSectionExtension;
            var args = new DiffModeEventArgs() {
                IsWithOtherDocument = true,
                Left = new OpenSectionEventArgs(sectionEx.Section, OpenSectionKind.NewTabDockLeft)
            };

            EnterDiffMode?.Invoke(this, args);
        }

        private void ToggleTagExecuted(object sender, ExecutedRoutedEventArgs e) {
            var section = e.Parameter as IRTextSectionExtension;

            if (section != null) {
                section.IsTagged = !section.IsTagged;
            }
        }

        private void FocusSearchExecuted(object sender, ExecutedRoutedEventArgs e) {
            SectionFilter.Focus();
            SectionFilter.SelectAll();
        }

        private void RefreshSectionList() {
            if(SectionList.ItemsSource == null) {
                return;
            }

            ((ListCollectionView)SectionList.ItemsSource).Refresh();
        }

        private void RefreshFunctionList() {
            if(FunctionList.ItemsSource == null) {
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
            var section = ((ListViewItem)sender).Content as IRTextSectionExtension;
            bool inNewTab = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ||
                            (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            OpenSectionExecute(section, inNewTab ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent);
        }

        private void OpenSectionExecute(IRTextSectionExtension value, OpenSectionKind kind,
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

        private void MarkCurrentSection(IRTextSectionExtension section) {
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
                else newIndex--;
            }

            return false;
        }

        public void SwitchToSection(IRTextSectionExtension section, IRDocumentHost targetDocument = null) {
            SectionList.SelectedItem = section;
            SectionList.ScrollIntoView(SectionList.SelectedItem);
            OpenSectionExecute(section, OpenSectionKind.ReplaceCurrent, targetDocument);
        }

        public void SwitchToSection(IRTextSection section, IRDocumentHost targetDocument = null) {
            SwitchToSection(Sections.Find((item) => item.Section == section), targetDocument);
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

        private sealed class SectionSorter : IComparer {
            public enum FieldKind {
                Number,
                Name,
                Blocks
            }

            private FieldKind sortingField_;
            private ListSortDirection direction_;

            public SectionSorter(FieldKind sortingField, ListSortDirection direction) {
                sortingField_ = sortingField;
                direction_ = direction;
            }

            public int Compare(object x, object y) {
                var sectionX = x as IRTextSectionExtension;
                var sectionY = y as IRTextSectionExtension;

                switch (sortingField_) {
                    case FieldKind.Number: {
                        var result = sectionY.Number - sectionX.Number;
                        return direction_ == ListSortDirection.Ascending ? -result : result;
                    }
                    case FieldKind.Name: {
                        var result = string.Compare(sectionY.Name, sectionX.Name, StringComparison.Ordinal);
                        return direction_ == ListSortDirection.Ascending ? -result : result;
                    }
                    case FieldKind.Blocks: {
                        var result = sectionY.BlockCount - sectionX.BlockCount;
                        return direction_ == ListSortDirection.Ascending ? -result : result;
                    }
                }

                return 0;
            }
        }

        public sealed class SortAdorner : Adorner {
            private static Geometry ascGeometry =
                Geometry.Parse("M 0 4 L 3.5 0 L 7 4 Z");

            private static Geometry descGeometry =
                Geometry.Parse("M 0 0 L 3.5 4 L 7 0 Z");

            public ListSortDirection Direction { get; private set; }

            public SortAdorner(UIElement element, ListSortDirection dir)
                : base(element) {
                this.Direction = dir;
            }

            protected override void OnRender(DrawingContext drawingContext) {
                base.OnRender(drawingContext);

                if (AdornedElement.RenderSize.Width < 20)
                    return;

                TranslateTransform transform = new TranslateTransform
                    (
                        AdornedElement.RenderSize.Width - 15,
                        (AdornedElement.RenderSize.Height - 5) / 2
                    );
                drawingContext.PushTransform(transform);

                Geometry geometry = ascGeometry;
                if (this.Direction == ListSortDirection.Descending)
                    geometry = descGeometry;
                drawingContext.DrawGeometry(Brushes.Black, null, geometry);

                drawingContext.Pop();
            }
        }

        private GridViewColumnHeader listViewSortCol = null;
        private SortAdorner listViewSortAdorner = null;


        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e) {
            GridViewColumnHeader column = (sender as GridViewColumnHeader);

            if (listViewSortCol != null) {
                AdornerLayer.GetAdornerLayer(listViewSortCol).Remove(listViewSortAdorner);
            }

            ListSortDirection sortingDirection = ListSortDirection.Ascending;
            if (listViewSortCol == column && listViewSortAdorner.Direction == sortingDirection)
                sortingDirection = ListSortDirection.Descending;

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
            else sortingField = SectionSorter.FieldKind.Name;

            var view = SectionList.ItemsSource as ListCollectionView;
            view.CustomSort = new SectionSorter(sortingField, sortingDirection);
            SectionList.Items.Refresh();
        }

        #region IToolPanel
        public override ToolPanelKind PanelKind => ToolPanelKind.Section;
        public override bool SavesStateToFile => true;

        public List<IRTextSectionExtension> Sections {
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

        public IRTextSectionExtension GetSectionExtension(IRTextSection section) {
            return sectionExtMap_[section];
        }

        public override void OnSessionStart() {
            var data = Session.LoadPanelState(this, null);

            if (data != null) {
                var state = StateSerializer.Deserialize<SectionPanelState>(data);

                foreach (var sectionId in state.AnnotatedSections) {
                    var section = summary_.GetSectionWithId(sectionId);
                    var sectionExt = sectionExtMap_[section];
                    sectionExt.IsTagged = true;
                    annotatedSections_.Add(sectionExt);
                }

                if (state.SelectedFunctionNumber > 0 &&
                    state.SelectedFunctionNumber < summary_.Functions.Count) {
                    SelectFunction(summary_.Functions[state.SelectedFunctionNumber]);
                    BringIntoViewSelectedListItem(FunctionList, focus: false);
                }
            }
        }

        public bool SelectFunction(IRTextFunction function) {
            if (function == currentFunction_) {
                return false;
            }

            FunctionList.SelectedItem = function;
            FunctionList.ScrollIntoView(FunctionList.SelectedItem);
            RefreshSectionList();
            return true;
        }

        public override void OnSessionSave() {
            var state = new SectionPanelState();
            state.AnnotatedSections = annotatedSections_.ToList((item) => item.Section.Id);
            state.SelectedFunctionNumber = FunctionList.SelectedItem != null ?
                ((IRTextFunction)FunctionList.SelectedItem).Number : 0;
            var data = StateSerializer.Serialize(state);
            Session.SavePanelState(data, this, null);
        }

        #endregion

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        internal void ScrollSectionList(double offset) {
            RegisterSectionListScrollEvent();

            if (sectionsScrollViewer_ != null) {
                sectionsScrollViewer_.ScrollToVerticalOffset(offset);
            }
        }
    }
}
