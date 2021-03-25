// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Globalization;
using System.Threading.Tasks;
using Aga.Controls.Tree;
using IRExplorerUI.Profile;

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
        public static readonly RoutedUICommand CopyFunctionName =
            new RoutedUICommand("Untitled", "CopyFunctionName", typeof(SectionPanel));
        public static readonly RoutedUICommand CopyDemangledFunctionName =
            new RoutedUICommand("Untitled", "CopyDemangledFunctionName", typeof(SectionPanel));
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

        private bool isDiffFromPrevious_;
        public bool IsDiffFromPrevious {
            get => isDiffFromPrevious_;
            set {
                isDiffFromPrevious_ = value;
                OnPropertyChange(nameof(IsDiffFromPrevious));
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
        public Brush BackColor { get; set; }
        public bool LowerIdenticalToPreviousOpacity { get; set; }

        public string NumberString => Section != null ? Section.Number.ToString() : "";
        public string BlockCountString => Section != null ? Section.BlockCount.ToString() : "";

        public int Number => Section?.Number ?? 0;
        public int BlockCount => Section?.BlockCount ?? 0;

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }
    }

    public class IRTextFunctionEx : INotifyPropertyChanged {
        public IRTextFunctionEx(IRTextFunction function, int index) {
            Function = function;
            Index = index;
        }

        public int Index { get; set; }
        public IRTextFunction Function { get; set; }
        public object OptionalData { get; set; }
        public object OptionalData2 { get; set; }
        public string OptionalDataText { get; set; }
        public string OptionalDataText2 { get; set; }
        public string AlternateName { get; set; }

        private bool isMarked_;
        public bool IsMarked {
            get => isMarked_;
            set {
                isMarked_ = value;
                OnPropertyChange(nameof(IsMarked));
            }
        }
        
        public Brush TextColor { get; set; }
        public Brush BackColor { get; set; }
        public Brush BackColor2 { get; set; }

        public string Name => Function.Name;
        public int SectionCount => Function.SectionCount;

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }
    }

    public class ModuleEx {
        public string Name { get; set; }
        public long Time { get; set; }
        public string Text { get; set; }
        public Brush BackColor { get; set; }
    }

    public class ChildFunctionEx : ITreeModel {
        public IRTextFunction Function { get; set; }
        public long Time { get; set; }
        public string Name { get; set; }
        public string AlternateName { get; set; }
        public string Text { get; set; }
        public Brush BackColor { get; set; }
        public List<ChildFunctionEx> Children { get; set; }
        public int ChildCount { get; set; }
        public bool IsSelf { get; set; }

        public IEnumerable GetChildren(object node) {
            if (node == null) {
                return Children;
            }
            
            var parentNode = (ChildFunctionEx)node;
            return parentNode.Children;
        }

        public bool HasChildren(object node) {
            if (node == null) return false;
            var parentNode = (ChildFunctionEx)node;
            return parentNode.Children != null && parentNode.Children.Count > 0;
        }
    }

    public enum SectionFieldKind {
        Number,
        Name,
        Blocks
    }

    public enum FunctionFieldKind {
        Name,
        AlternateName,
        Sections,
        Optional,
        Optional2
    }
    
    public enum ChildFunctionFieldKind {
        Name,
        AlternateName,
        Time,
        Children
    }

    public enum ModuleFieldKind {
        Name,
        Time
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

        private SectionSettings sectionSettings_;
        private IRTextSummary summary_;
        private IRTextSummary otherSummary_;
        private List<IRTextSectionEx> sections_;

        private bool sectionExtensionComputed_;
        private Dictionary<IRTextSection, IRTextSectionEx> sectionExtMap_;
        private Dictionary<IRTextFunction, IRTextFunctionEx> functionExtMap_;
        private ScrollViewer sectionsScrollViewer_;

        private OptionsPanelHostWindow optionsPanelWindow_;
        private bool optionsPanelVisible_;

        private GridViewColumnValueSorter<FunctionFieldKind> functionValueSorter_;
        private GridViewColumnValueSorter<SectionFieldKind> sectionValueSorter_;
        private GridViewColumnValueSorter<ChildFunctionFieldKind> childFunctionValueSorter_;
        private GridViewColumnValueSorter<ModuleFieldKind> moduleValueSorter_;

        public SectionPanel() {
            InitializeComponent();
            sections_ = new List<IRTextSectionEx>();
            sectionExtMap_ = new Dictionary<IRTextSection, IRTextSectionEx>();
            functionExtMap_ = new Dictionary<IRTextFunction, IRTextFunctionEx>();
            annotatedSections_ = new HashSet<IRTextSectionEx>();
            IsFunctionListVisible = true;
            SyncDiffedDocuments = true;
            MainGrid.DataContext = this;
            sectionSettings_ = App.Settings.SectionSettings;

            functionValueSorter_ = 
                new GridViewColumnValueSorter<FunctionFieldKind>(FunctionList,
                name => name switch {
                    "FunctionColumnHeader" => FunctionFieldKind.Name,
                    "AlternateNameColumnHeader" => FunctionFieldKind.AlternateName,
                    "SectionsColumnHeader" => FunctionFieldKind.Sections,
                    "OptionalColumnHeader" => FunctionFieldKind.Optional,
                    "OptionalColumnHeader2" => FunctionFieldKind.Optional2
                }, 
                (x, y, field, direction) => {
                    var sectionX = x as IRTextFunctionEx;
                    var sectionY = y as IRTextFunctionEx;

                    switch (field) {
                        case FunctionFieldKind.Sections: {
                            int result = sectionY.SectionCount - sectionX.SectionCount;
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.Name: {
                            int result = string.Compare(sectionY.Name, sectionX.Name, StringComparison.Ordinal);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.AlternateName: {
                            int result = string.Compare(sectionY.AlternateName, sectionX.AlternateName, StringComparison.Ordinal);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.Optional: {
                            int result = 0;

                            if (sectionX.OptionalData != null && sectionY.OptionalData != null) {
                                result = ((long)sectionY.OptionalData).CompareTo((long)sectionX.OptionalData);
                            }

                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.Optional2: {
                            int result = 0;

                            if (sectionX.OptionalData2 != null && sectionY.OptionalData2 != null) {
                                result = ((long)sectionY.OptionalData2).CompareTo((long)sectionX.OptionalData2);
                            }

                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                });

            sectionValueSorter_ =
                new GridViewColumnValueSorter<SectionFieldKind>(SectionList,
                name => name switch {
                    "NumberColumnHeader" => SectionFieldKind.Number,
                    "NameColumnHeader" => SectionFieldKind.Name,
                    "BlocksColumnHeader" => SectionFieldKind.Blocks,
                },
                (x, y, field, direction) => {
                    var sectionX = x as IRTextSectionEx;
                    var sectionY = y as IRTextSectionEx;

                    switch (field) {
                        case SectionFieldKind.Number: {
                            int result = sectionY.Number - sectionX.Number;
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case SectionFieldKind.Name: {
                            int result = string.Compare(sectionY.Name, sectionX.Name, StringComparison.Ordinal);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case SectionFieldKind.Blocks: {
                            int result = sectionY.BlockCount - sectionX.BlockCount;
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                });
            
            
            childFunctionValueSorter_ = 
                new GridViewColumnValueSorter<ChildFunctionFieldKind>(ChildFunctionList,
                name => name switch {
                    "ChildColumnHeader" => ChildFunctionFieldKind.Name,
                    "ChildAlternateNameColumnHeader" => ChildFunctionFieldKind.AlternateName,
                    "ChildTimeColumnHeader" => ChildFunctionFieldKind.Time,
                    "ChildCountColumnHeader" => ChildFunctionFieldKind.Children
                }, 
                (x, y, field, direction) => {
                    var childX = x as ChildFunctionEx;
                    var childY = y as ChildFunctionEx;

                    switch (field) {
                        case ChildFunctionFieldKind.Name: {
                            int result = string.Compare(childY.Name, childX.Name, StringComparison.Ordinal);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case ChildFunctionFieldKind.AlternateName: {
                             int result = string.Compare(childY.AlternateName, childX.AlternateName, StringComparison.Ordinal);
                             return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case ChildFunctionFieldKind.Time: {
                            int result = childY.Time.CompareTo(childX.Time);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case ChildFunctionFieldKind.Children: {
                            int result = childY.ChildCount.CompareTo(childX.ChildCount);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                });
            
            moduleValueSorter_ = 
                new GridViewColumnValueSorter<ModuleFieldKind>(ModulesList,
                    name => name switch {
                        "ModuleColumnHeader" => ModuleFieldKind.Name,
                        "ModuleTimeColumnHeader" => ModuleFieldKind.Time
                    }, 
                    (x, y, field, direction) => {
                        var moduleX = x as ModuleEx;
                        var moduleY = y as ModuleEx;

                        switch (field) {
                            case ModuleFieldKind.Name: {
                                int result = string.Compare(moduleY.Name, moduleX.Name, StringComparison.Ordinal);
                                return direction == ListSortDirection.Ascending ? -result : result;
                            }
                            case ModuleFieldKind.Time: {
                                int result = moduleY.Time.CompareTo(moduleX.Time);
                                return direction == ListSortDirection.Ascending ? -result : result;
                            }
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    });
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

        private bool filterTagged_;
        public bool FilterTagged {
            get => filterTagged_;
            set {
                if (filterTagged_ != value) {
                    filterTagged_ = value;
                    OnPropertyChange(nameof(FilterTagged));
                    RefreshSectionList();
                }
            }
        }

        private bool filterDiffFromPrevious_;
        public bool FilterDiffFromPrevious {
            get => filterDiffFromPrevious_;
            set {
                if (filterDiffFromPrevious_ != value) {
                    filterDiffFromPrevious_ = value;
                    OnPropertyChange(nameof(FilterDiffFromPrevious));
                    RefreshSectionList();
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

        private string optionalDataColumnName_;
        public string OptionalDataColumnName {
            get => optionalDataColumnName_;
            set {
                if (optionalDataColumnName_ != value) {
                    optionalDataColumnName_ = value;
                    OnPropertyChange(nameof(OptionalDataColumnName));
                }
            }
        }
        
        private bool optionalDataColumnVisible_;
        public bool OptionalDataColumnVisible {
            get => optionalDataColumnVisible_;
            set {
                if (optionalDataColumnVisible_ != value) {
                    optionalDataColumnVisible_ = value;
                    OnPropertyChange(nameof(OptionalDataColumnVisible));
                }
            }
        }
        
        private string optionalDataColumnName2_;
        public string OptionalDataColumnName2 {
            get => optionalDataColumnName2_;
            set {
                if (optionalDataColumnName2_ != value) {
                    optionalDataColumnName2_ = value;
                    OnPropertyChange(nameof(OptionalDataColumnName2));
                }
            }
        }
        
        private bool optionalDataColumnVisible2_;
        public bool OptionalDataColumnVisible2 {
            get => optionalDataColumnVisible2_;
            set {
                if (optionalDataColumnVisible2_ != value) {
                    optionalDataColumnVisible2_ = value;
                    OnPropertyChange(nameof(OptionalDataColumnVisible2));
                }
            }
        }

        private bool alternateNameColumnVisible_;
        public bool AlternateNameColumnVisible {
            get => alternateNameColumnVisible_;
            set {
                if (alternateNameColumnVisible_ != value) {
                    alternateNameColumnVisible_ = value;
                    OnPropertyChange(nameof(AlternateNameColumnVisible));
                }
            }
        }

        private bool showModules_;

        public bool ShowModules {
            get => showModules_;
            set {
                if (showModules_ != value) {
                    showModules_ = value;
                    OnPropertyChange(nameof(ShowModules));
                    OnPropertyChange(nameof(ShowFunctions));
                }
            }
        }

        public bool ShowFunctions {
            get => !showModules_;
            set {
                if (showModules_ == value) {
                    showModules_ = !value;
                    OnPropertyChange(nameof(ShowModules));
                    OnPropertyChange(nameof(ShowFunctions));
                }
            }
        }

        private bool showChildren_;

        public bool ShowChildren {
            get => showChildren_;
            set {
                if (showChildren_ != value) {
                    showChildren_ = value;
                    OnPropertyChange(nameof(ShowSections));
                    OnPropertyChange(nameof(ShowChildren));
                }
            }
        }

        public bool ShowSections {
            get => !showChildren_;
            set {
                if (showChildren_ == value) {
                    showChildren_ = !value;
                    OnPropertyChange(nameof(ShowSections));
                    OnPropertyChange(nameof(ShowChildren));
                }
            }
        }

        private bool profileControlsVisible_;

        public bool ProfileControlsVisible {
            get => profileControlsVisible_;
            set {
                if (profileControlsVisible_ != value) {
                    profileControlsVisible_ = value;
                    OnPropertyChange(nameof(ProfileControlsVisible));
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

        public void SelectSection(IRTextSection section, bool focus = true, bool force = false) {
            UpdateSectionListBindings(section.ParentFunction, force);
            var sectionEx = GetSectionExtension(section);
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

        private void SetDemangledFunctionNames(List<IRTextFunctionEx> functions) {
            var nameProvider = Session.CompilerInfo.NameProvider;

            if (!sectionSettings_.ShowDemangledNames || !nameProvider.IsDemanglingSupported) {
                AlternateNameColumnVisible = false;
                return;
            }

            var demanglingOptions = nameProvider.DemanglingOptions;

            foreach (var funcEx in functions) {
                funcEx.AlternateName = nameProvider.DemangleFunctionName(funcEx.Function, demanglingOptions);
            }

            AlternateNameColumnVisible = true;
        }

        private void SetDemangledChildFunctionNames(List<ChildFunctionEx> functions) {
            var nameProvider = Session.CompilerInfo.NameProvider;

            if (!sectionSettings_.ShowDemangledNames || !nameProvider.IsDemanglingSupported) {
                AlternateNameColumnVisible = false;
                return;
            }

            var demanglingOptions = nameProvider.DemanglingOptions;

            foreach (var funcEx in functions) {
                funcEx.AlternateName = nameProvider.DemangleFunctionName(funcEx.Function, demanglingOptions);
            }

            AlternateNameColumnVisible = true;
        }

        private void SetFunctionProfileInfo(List<IRTextFunctionEx> functions) {
            var profile = Session.ProfileData;

            if (profile == null) {
                OptionalDataColumnVisible = false;
                OptionalDataColumnVisible2 = false;
                return;
            }

            const int lightSteps = 10;
            List<Color> colors = ColorUtils.MakeColorPallete(1, 1, 0.85f, 0.95f, lightSteps);

            var modulesEx = new List<ModuleEx>();

            foreach (var pair in profile.ModuleWeights) {
                var moduleWeight = pair.Value;
                double weightPercentage = profile.ScaleModuleWeight(pair.Value);

                var moduleInfo = new ModuleEx() {
                    Name = pair.Key,
                    Time = moduleWeight.Ticks,
                    Text = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(moduleWeight.TotalMilliseconds, 2)} ms)"
                };
                modulesEx.Add(moduleInfo);
            }

            var modulesFilter = new ListCollectionView(modulesEx);
            //functionFilter.Filter = FilterFunctionList;
            ModulesList.ItemsSource = modulesFilter;
            moduleValueSorter_.SortByField(ModuleFieldKind.Time, ListSortDirection.Descending);

            OptionalDataColumnVisible = true;
            OptionalDataColumnName = "Time (self)";
            OptionalDataColumnVisible2 = true;
            OptionalDataColumnName2 = "Time (total)";

            foreach (var funcEx in functions) {
                var funcProfile = profile.GetFunctionProfile(funcEx.Function);

                if(funcProfile != null) {
                    funcEx.OptionalData2 = funcProfile.Weight.Ticks;
                    double weightPercentage = profile.ScaleFunctionWeight(funcProfile.Weight);
                    funcEx.OptionalDataText2 =
                        $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(funcProfile.Weight.TotalMilliseconds, 2)} ms)";

                    funcEx.OptionalData = funcProfile.ExclusiveWeight.Ticks;
                    double weightPercentage2 = profile.ScaleFunctionWeight(funcProfile.ExclusiveWeight);

                    funcEx.OptionalDataText =
                        $"{Math.Round(weightPercentage2 * 100, 2)}% ({Math.Round(funcProfile.ExclusiveWeight.TotalMilliseconds, 2)} ms)";
                    
                    int colorIndex = (int)Math.Floor(lightSteps * (1.0 - weightPercentage));
                    int colorIndex2 = (int)Math.Floor(lightSteps * (1.0 - weightPercentage2));

                    if (colorIndex < 0) {
                        Trace.WriteLine($"Negative color {colorIndex}");
                        colorIndex = 0;
                    }

                    Debug.Assert(colors != null);
                    funcEx.BackColor = ColorBrushes.GetBrush(colors[colorIndex2]);
                    funcEx.BackColor2 = ColorBrushes.GetBrush(colors[colorIndex]);
                }
                else {
                    funcEx.OptionalData = TimeSpan.Zero.Ticks;
                    funcEx.OptionalData2 = TimeSpan.Zero.Ticks;
                }
            }

            functionValueSorter_.SortByField(FunctionFieldKind.Optional, ListSortDirection.Descending);
            ProfileControlsVisible = true;
            
            ResizeFunctionFilter(FunctionToolbar.RenderSize.Width);
        }

        private async Task UpdateFunctionListBindings() {
            if (summary_ == null) {
                ResetSectionPanel();
                return;
            }

            SetupSectionExtension();
            
            // Create for each function a wrapper with more properties for the UI.
            int index = 0;
            var functionsEx = new List<IRTextFunctionEx>();

            foreach (var func in summary_.Functions) {
                var funcEx = new IRTextFunctionEx(func, index++);
                functionExtMap_[func] = funcEx;
                functionsEx.Add(funcEx);
            }

            // Set up the filter used to search the list.
            var functionFilter = new ListCollectionView(functionsEx);
            functionFilter.Filter = FilterFunctionList;
            FunctionList.ItemsSource = functionFilter;
            SectionList.ItemsSource = null;

            // Attach additional data to the UI.
            SetDemangledFunctionNames(functionsEx);
            SetFunctionProfileInfo(functionsEx);

            if (summary_.Functions.Count == 1) {
                await SelectFunction(summary_.Functions[0]);
            }
        }

        private void ResetSectionPanel() {
            otherSummary_ = null;
            currentFunction_ = null;
            sections_.Clear();
            sectionExtMap_.Clear();
            annotatedSections_.Clear();
            SectionList.ItemsSource = null;
            FunctionList.ItemsSource = null;
            SectionList.UpdateLayout();
            FunctionList.UpdateLayout();
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

            currentFunction_ = function;
            FunctionList.SelectedItem = function;
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
                    else
                        sectionEx.BorderThickness = new Thickness();

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
                sectionEx.LowerIdenticalToPreviousOpacity = sectionIndex > 0 &&
                    sectionSettings_.LowerIdenticalToPreviousOpacity;
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
            var functionEx = (IRTextFunctionEx)value;
            var function = functionEx.Function;

            // Don't filter with less than 2 letters.
            //? TODO: FunctionFilter change should rather set a property with the trimmed text
            string text = FunctionFilter.Text.Trim();

            if (text.Length < 2) {
                return true;
            }

            // In two-document diff mode, show only functions
            // that are common in the two summaries.
            if (otherSummary_ != null) {
                if (otherSummary_.FindFunction(function) == null) {
                    return false;
                }
            }

            // Search the function name.
            if ((App.Settings.SectionSettings.FunctionSearchCaseSensitive
                ? function.Name.Contains(text, StringComparison.Ordinal)
                : function.Name.Contains(text, StringComparison.OrdinalIgnoreCase))) {
                return true;
            }

            // Search the demangled name.
            if (!string.IsNullOrEmpty(functionEx.AlternateName)) {
                return (App.Settings.SectionSettings.FunctionSearchCaseSensitive
                    ? functionEx.AlternateName.Contains(text, StringComparison.Ordinal)
                    : functionEx.AlternateName.Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        private bool FilterSectionList(object value) {
            var section = (IRTextSectionEx)value;

            if (section.IsSelected) {
                return true;
            }

            if (FilterTagged && !section.IsTagged) {
                return false;
            }

            if (FilterDiffFromPrevious && !section.IsDiffFromPrevious) {
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

        private async void FunctionList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (e.AddedItems.Count == 1) {
                await SelectFunction(((IRTextFunctionEx)FunctionList.SelectedItem).Function);
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
        
        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        public void ScrollSectionList(double offset) {
            RegisterSectionListScrollEvent();
            sectionsScrollViewer_?.ScrollToVerticalOffset(offset);
        }

        private async Task ComputeConsecutiveSectionDiffs() {
            if(!sectionSettings_.MarkSectionsIdenticalToPrevious || sections_.Count < 2) {
                return;
            }

            // Make a list of all section pairs like [i - 1, i] and diff each one.
            // Note that when comparing two documents side-by-side, some of the sections
            // may be placeholders that don't have a real section behind, those must be ignored.
            var comparedSections = new List<Tuple<IRTextSection, IRTextSection>>();
            int prevIndex = -1;

            for (int i = 0; i < sections_.Count; i++) {
                if (sections_[i].Section == null) {
                    continue;
                }

                if (prevIndex != -1) {
                    comparedSections.Add(new Tuple<IRTextSection, IRTextSection>(
                        sections_[prevIndex].Section, sections_[i].Section));
                }

                prevIndex = i;
            }

            //? TODO: Pass the LoadedDocument to the panel, not Summary.
            var loader = Session.SessionState.FindLoadedDocument(Summary).Loader;
            var diffBuilder = new DocumentDiffBuilder(App.Settings.DiffSettings);
            var results = await diffBuilder.ComputeSectionDiffs(comparedSections, loader, loader, true);
            
            foreach (var result in results) {
                if (result.HasDiffs) {
                    var diffSection = GetSectionExtension(result.RightSection);
                    diffSection.IsDiffFromPrevious = true;
                }
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
        
        public override async void OnSessionStart() {
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
                    await SelectFunction(summary_.Functions[state.SelectedFunctionNumber]);
                    BringIntoViewSelectedListItem(FunctionList, false);
                }
            }

            // Reset search filters.
            FunctionFilter.Text = "";
            SectionFilter.Text = "";
        }

        public async Task SelectFunction(IRTextFunction function) {
            if (function == currentFunction_ ||
                function.ParentSummary != Summary) {
                return;
            }

            UpdateSectionListBindings(function);
            var funcEx = functionExtMap_[function];
            FunctionList.SelectedItem = funcEx;
            FunctionList.ScrollIntoView(FunctionList.SelectedItem);
            RefreshSectionList();


            if(profileControlsVisible_) {
                var funcProfile = Session.ProfileData.GetFunctionProfile(function);

                if(funcProfile != null) {
                    //? TODO:SetDemangledChildFunctionNames(childrenEx); - walk tree
                    //? sorting doesn't work, pre-sort descending
                    ChildFunctionList.Model = CreateProfileCallTree(function);
                }
            }

            await ComputeConsecutiveSectionDiffs();
        }

        private ChildFunctionEx CreateProfileCallTree(IRTextFunction function) {
            var rootNode = new ChildFunctionEx();
            rootNode.Children = new List<ChildFunctionEx>();
            CreateProfileCallTree(function, rootNode);
            return rootNode;
        }

        private void CreateProfileCallTree(IRTextFunction function, ChildFunctionEx parentNode) {
            var funcProfile = Session.ProfileData.GetFunctionProfile(function);

            if (funcProfile == null) {
                return;
            }
            
            //? TODO: Use pallette
            const int lightSteps = 10;
            List<Color> colors = ColorUtils.MakeColorPallete(1, 1, 0.85f, 0.95f, lightSteps);
            
            
            var selfInfo = CreateChildInfo(function, funcProfile.ExclusiveWeight, funcProfile, null, colors);
            selfInfo.Name = "Self";
            selfInfo.IsSelf = true;
            parentNode.Children.Add(selfInfo);

            foreach (var pair in funcProfile.ChildrenWeights) {
                var childFunc = summary_.GetFunctionWithId(pair.Key);
                var childFuncProfile = Session.ProfileData.GetFunctionProfile(childFunc);
                var childNode = CreateChildInfo(childFunc, pair.Value, funcProfile, childFuncProfile, colors); 
                parentNode.Children.Add(childNode);

                if (childFuncProfile.ChildrenWeights.Count > 0) {
                    childNode.Children = new List<ChildFunctionEx>();
                    CreateProfileCallTree(childFunc, childNode);
                }
            }
            
            // Sort children, since that is not yet supported by the TreeListView control.
            parentNode.Children.Sort((a, b) => b.Time.CompareTo(a.Time));
        }

        private ChildFunctionEx CreateChildInfo(IRTextFunction childFunc, TimeSpan childWeight, 
                FunctionProfileData funcProfile, FunctionProfileData childFuncProfile, List<Color> colors) {
            KeyValuePair<int, TimeSpan> pair;
            double weightPercentage = funcProfile.ScaleChildWeight(childWeight);
            int colorIndex = (int)Math.Floor(10 * (1.0 - weightPercentage));

            var childInfo = new ChildFunctionEx()
            {
                Function = childFunc,
                Time =  childWeight.Ticks,
                Name = childFunc.Name,
                ChildCount = childFuncProfile != null ? childFuncProfile.ChildrenWeights.Count : 0,
                Text = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(childWeight.TotalMilliseconds, 2)} ms)",
                BackColor = ColorBrushes.GetBrush(colors[colorIndex])
            };
            return childInfo;
        }

        public override void OnSessionSave() {
            base.OnSessionStart();
            var state = new SectionPanelState();
            state.AnnotatedSections = annotatedSections_.ToList(item => item.Section.Id);

            state.SelectedFunctionNumber = FunctionList.SelectedItem != null
                ? ((IRTextFunctionEx)FunctionList.SelectedItem).Function.Number : 0;

            var data = StateSerializer.Serialize(state);
            Session.SavePanelState(data, this, null);
        }

        #endregion
        
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

        private void OptionsPanel_SettingsChanged(object sender, EventArgs e) {
            var newSettings = (SectionSettings)optionsPanelWindow_.Settings;
            HandleNewSettings(newSettings, false);
            optionsPanelWindow_.Settings = null;
            optionsPanelWindow_.Settings = (SectionSettings)sectionSettings_.Clone();
        }

        private void OptionsPanel_PanelReset(object sender, EventArgs e) {
            HandleNewSettings(new SectionSettings(), true);

            //? TODO: Setting to null should be part of OptionsPanelBase and remove it in all places
            optionsPanelWindow_.Settings = null;
            optionsPanelWindow_.Settings = (SectionSettings)sectionSettings_.Clone();
        }

        private async void OptionsPanel_PanelClosed(object sender, EventArgs e) {
            await CloseOptionsPanel();
        }

        private async Task CloseOptionsPanel() {
            if (!optionsPanelVisible_) {
                return;
            }

            optionsPanelWindow_.IsOpen = false;
            optionsPanelWindow_.PanelClosed -= OptionsPanel_PanelClosed;
            optionsPanelWindow_.PanelReset -= OptionsPanel_PanelReset;
            optionsPanelWindow_.SettingsChanged -= OptionsPanel_SettingsChanged;

            var newSettings = (SectionSettings)optionsPanelWindow_.Settings;
            await HandleNewSettings(newSettings, true);

            optionsPanelWindow_ = null;
            optionsPanelVisible_ = false;
        }

        private async Task HandleNewSettings(SectionSettings newSettings, bool commit) {
            if (commit) {
                App.Settings.SectionSettings = newSettings;
                App.SaveApplicationSettings();
            }

            if (newSettings.Equals(sectionSettings_)) {
                return;
            }

            bool updateFunctionList = newSettings.HasFunctionListChanges(sectionSettings_);
            App.Settings.SectionSettings = newSettings;
            sectionSettings_ = newSettings;

            if (updateFunctionList) {
                await UpdateFunctionListBindings();
            }

            UpdateSectionListBindings(currentFunction_, true);
            RefreshSectionList();
            await ComputeConsecutiveSectionDiffs();
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

        private void CopyFunctionNameExecuted(object sender, ExecutedRoutedEventArgs e) {
            var func = GetSelectedFunction(e);

            if (func != null) {
                var text = Session.CompilerInfo.NameProvider.GetFunctionName(func);
                Clipboard.SetText(text);
            }
        }
        
        private void CopyDemangledFunctionNameExecuted(object sender, ExecutedRoutedEventArgs e) {
            var func = GetSelectedFunction(e);

            if (func != null) {
                var options = FunctionNameDemanglingOptions.Default;
                var text = Session.CompilerInfo.NameProvider.DemangleFunctionName(func, options);
                Clipboard.SetText(text);
            }
        }

        private async void DisplayCallGraphExecuted(object sender, ExecutedRoutedEventArgs e) {
            if (e.Parameter is IRTextSectionEx sectionEx) {
                DisplayCallGraph?.Invoke(this, new DisplayCallGraphEventArgs(Summary, sectionEx.Section, false));
            }
        }

        private IRTextFunction GetSelectedFunction(ExecutedRoutedEventArgs e) {
            if (e.Parameter is IRTextFunctionEx funcEx) {
                return funcEx.Function;
            }
            else if (e.Parameter is IRTextSectionEx sectionEx) {
                return sectionEx.Section.ParentFunction;
            }

            return null;
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
            double defaultSpace = 60;
            double profileControlsSpace = profileControlsVisible_ ? 180 : 0;
            FunctionFilterGrid.Width = Math.Max(1, width - defaultSpace - profileControlsSpace);
        }
        
        private void ChildDoubleClick(object sender, MouseButtonEventArgs e) {
            if (sender is not ListViewItem) {
                return;
            }
            
            var childInfo = ((ListViewItem)sender).Content as ChildFunctionEx;

            if (!childInfo.IsSelf) {
                SelectFunction(childInfo.Function);
            }
        }

    }
}