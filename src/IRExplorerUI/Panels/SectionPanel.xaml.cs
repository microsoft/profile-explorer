// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Concurrent;
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
using Grpc.Core;
using IRExplorerUI.Profile;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Analysis;
using Xceed.Wpf.Toolkit;
using MessageBox = System.Windows.MessageBox;

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

    public class IRTextDiffBaseEx {
        protected DiffKind diffKind_;

        public IRTextDiffBaseEx(DiffKind diffKind) {
            diffKind_ = diffKind;
        }

        public bool IsInsertionDiff => diffKind_ == DiffKind.Insertion;
        public bool IsDeletionDiff => diffKind_ == DiffKind.Deletion;
        public bool IsPlaceholderDiff => diffKind_ == DiffKind.Placeholder;
        public bool HasDiffs => diffKind_ == DiffKind.Modification;

        public Brush NewSectionBrush => ColorBrushes.GetBrush(App.Settings.SectionSettings.NewSectionColor);
        public Brush MissingSectionBrush => ColorBrushes.GetBrush(App.Settings.SectionSettings.MissingSectionColor);
        public Brush ChangedSectionBrush => ColorBrushes.GetBrush(App.Settings.SectionSettings.ChangedSectionColor);
    }

    public class IRTextSectionEx : IRTextDiffBaseEx, INotifyPropertyChanged {
        private bool isSelected_;
        private bool isTagged_;

        public IRTextSectionEx(IRTextSection section, int index) : base(DiffKind.None) {
            Section = section;
            Index = index;
        }

        public IRTextSectionEx(IRTextSection section, DiffKind diffKind, string name, int index) : base(diffKind) {
            Section = section;
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
            get => diffKind_;
            set {
                diffKind_ = value;
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

    public class IRTextFunctionEx : IRTextDiffBaseEx, INotifyPropertyChanged {
        public IRTextFunctionEx(IRTextFunction function, int index) : base(DiffKind.None) {
            Function = function;
            Index = index;
            Statistics = new FunctionCodeStatistics();
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
        public FunctionCodeStatistics Statistics { get; set; }
        public FunctionCodeStatistics DiffStatistics { get; set; }

        public DiffKind FunctionDiffKind {
            get => diffKind_;
            set {
                diffKind_ = value;
                OnPropertyChange(nameof(IsInsertionDiff));
                OnPropertyChange(nameof(IsDeletionDiff));
                OnPropertyChange(nameof(IsPlaceholderDiff));
                OnPropertyChange(nameof(HasDiffs));
            }
        }

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
        public Brush TextColor { get; set; }
        public Brush BackColor { get; set; }
        public List<ChildFunctionEx> Children { get; set; }
        public int DescendantCount { get; set; }
        public bool IsMarked { get; set; }
        public FunctionCodeStatistics Statistics { get; set; }

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
        Optional2,
        StatisticSize,
        StatisticInstructions,
        StatisticLoads,
        StatisticStores,
        StatisticBranches,
        StatisticCalls,
        StatisticCallees,
        StatisticCallers,
        StatisticIndirectCalls,
        StatisticDiff
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
        private bool useProfileCallTree_;

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

        private CallGraph callGraph_;
        private CallGraph otherCallGraph_;
        private ModuleReport moduleReport_;
        private CancelableTaskInstance statisticsTask_;
        private ConcurrentDictionary<IRTextFunction, FunctionCodeStatistics> functionStatMap_;

        public SectionPanel() {
            InitializeComponent();
            statisticsTask_ = new CancelableTaskInstance();
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
                    "OptionalColumnHeader2" => FunctionFieldKind.Optional2,
                    "SizeHeader" => FunctionFieldKind.StatisticSize,
                    "LoadsHeader" => FunctionFieldKind.StatisticLoads,
                    "StoresHeader" => FunctionFieldKind.StatisticStores,
                    "InstructionsHeader" => FunctionFieldKind.StatisticInstructions,
                    "BranchesHeader" => FunctionFieldKind.StatisticBranches,
                    "CallsHeader" => FunctionFieldKind.StatisticCalls,
                    "CallersHeader" => FunctionFieldKind.StatisticCallers,
                    "CalleesHeader" => FunctionFieldKind.StatisticCallees,
                    "IndirectCallsHeader" => FunctionFieldKind.StatisticIndirectCalls,
                    "DiffHeader" => FunctionFieldKind.StatisticDiff
                }, 
                (x, y, field, direction) => {
                    var functionX = x as IRTextFunctionEx;
                    var functionY = y as IRTextFunctionEx;

                    switch (field) {
                        case FunctionFieldKind.Sections: {
                            int result = functionY.SectionCount - functionX.SectionCount;
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.Name: {
                            int result = string.Compare(functionY.Name, functionX.Name, StringComparison.Ordinal);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.AlternateName: {
                            int result = string.Compare(functionY.AlternateName, functionX.AlternateName, StringComparison.Ordinal);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.Optional: {
                            int result = 0;

                            if (functionX.OptionalData != null && functionY.OptionalData != null) {
                                result = ((long)functionY.OptionalData).CompareTo((long)functionX.OptionalData);
                            }

                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.Optional2: {
                            int result = 0;

                            if (functionX.OptionalData2 != null && functionY.OptionalData2 != null) {
                                result = ((long)functionY.OptionalData2).CompareTo((long)functionX.OptionalData2);
                            }

                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.StatisticSize: {
                            int result = functionY.Statistics.Size.CompareTo(functionX.Statistics.Size);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.StatisticInstructions: {
                            int result = functionY.Statistics.Instructions.CompareTo(functionX.Statistics.Instructions);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.StatisticLoads: {
                            int result = functionY.Statistics.Loads.CompareTo(functionX.Statistics.Loads);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.StatisticStores: {
                            int result = functionY.Statistics.Stores.CompareTo(functionX.Statistics.Stores);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.StatisticBranches: {
                            int result = functionY.Statistics.Branches.CompareTo(functionX.Statistics.Branches);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.StatisticCalls: {
                            int result = functionY.Statistics.Calls.CompareTo(functionX.Statistics.Calls);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.StatisticCallees: {
                            int result = functionY.Statistics.Callees.CompareTo(functionX.Statistics.Callees);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.StatisticCallers: {
                            int result = functionY.Statistics.Callers.CompareTo(functionX.Statistics.Callers);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.StatisticIndirectCalls: {
                            int result = functionY.Statistics.IndirectCalls.CompareTo(functionX.Statistics.IndirectCalls);
                            return direction == ListSortDirection.Ascending ? -result : result;
                        }
                        case FunctionFieldKind.StatisticDiff: {
                            int result = 0;
                            if (functionY.FunctionDiffKind != functionX.FunctionDiffKind) {
                                if (functionX.IsDeletionDiff) {
                                    result = -1;
                                }
                                else if (functionX.IsInsertionDiff) {
                                    result = 1;
                                }
                            }
                            else {
                                result = string.Compare(functionY.Name, functionX.Name, StringComparison.Ordinal);
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
                            int result = childY.DescendantCount.CompareTo(childX.DescendantCount);
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

        public bool UseProfileCallTree {
            get => useProfileCallTree_;
            set {
                if (useProfileCallTree_ != value) {
                    useProfileCallTree_ = value;
                    OnPropertyChange(nameof(UseProfileCallTree));
                    OnPropertyChange(nameof(UseIRCallTree));
                }
            }
        }

        public bool UseIRCallTree {
            get => !useProfileCallTree_;
            set {
                if (useProfileCallTree_ == value) {
                    useProfileCallTree_ = !value;
                    OnPropertyChange(nameof(UseProfileCallTree));
                    OnPropertyChange(nameof(UseIRCallTree));
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
                    UpdateFunctionListBindings();
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

        private bool childTimeColumnVisible_;
        public bool ChildTimeColumnVisible {
            get => childTimeColumnVisible_;
            set {
                if (childTimeColumnVisible_ != value) {
                    childTimeColumnVisible_ = value;
                    OnPropertyChange(nameof(ChildTimeColumnVisible));
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

        private bool functionModuleControlsVisible_;
        public bool FunctionModuleControlsVisible {
            get => functionModuleControlsVisible_;
            set {
                if (functionModuleControlsVisible_ != value) {
                    functionModuleControlsVisible_ = value;
                    OnPropertyChange(nameof(FunctionModuleControlsVisible));
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

        private void SetDemangledChildFunctionNames(ChildFunctionEx func) {
            var nameProvider = Session.CompilerInfo.NameProvider;

            if (!sectionSettings_.ShowDemangledNames || !nameProvider.IsDemanglingSupported) {
                AlternateNameColumnVisible = false;
                return;
            }

            SetDemangledChildFunctionNamesImpl(func, nameProvider);
            AlternateNameColumnVisible = true;
        }

        private void SetDemangledChildFunctionNamesImpl(ChildFunctionEx funcEx, INameProvider nameProvider) {
            if (funcEx.Function != null) {
                funcEx.AlternateName =
                    nameProvider.DemangleFunctionName(funcEx.Function, nameProvider.DemanglingOptions);
            }

            if (funcEx.Children != null) {
                foreach (var child in funcEx.Children) {
                    SetDemangledChildFunctionNamesImpl(child, nameProvider);
                }
            }
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
            FunctionModuleControlsVisible = true;
            
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

            // In two-document diff mode, also add entries for functions that are found
            // only in the left or in the right document and mark them as diffs.
            if (otherSummary_ != null) {
                foreach (var function in summary_.Functions) {
                    var funcEx = new IRTextFunctionEx(function, index++);
                    functionExtMap_[function] = funcEx;
                    functionsEx.Add(funcEx);

                    if (otherSummary_.FindFunction(function) == null) {
                        // Function missing in right document (removed).
                        funcEx.FunctionDiffKind = DiffKind.Deletion;
                    }
                }

                foreach (var function in otherSummary_.Functions) {
                    if (summary_.FindFunction(function) == null) {
                        // Function missing in left document (new).
                        var funcEx = new IRTextFunctionEx(function, index++);
                        functionExtMap_[function] = funcEx;
                        functionsEx.Add(funcEx);
                        funcEx.FunctionDiffKind = DiffKind.Insertion;
                    }
                }
            }
            else {
                // Single document mode.
                foreach (var func in summary_.Functions) {
                    var funcEx = new IRTextFunctionEx(func, index++);
                    functionExtMap_[func] = funcEx;
                    functionsEx.Add(funcEx);
                }
            }

            if (FunctionPartVisible) {
                // Set up the filter used to search the list.
                var functionFilter = new ListCollectionView(functionsEx);
                functionFilter.Filter = FilterFunctionList;
                FunctionList.ItemsSource = functionFilter;
                SectionList.ItemsSource = null;

                if (summary_.Functions.Count == 1) {
                    await SelectFunction(summary_.Functions[0]);
                }

                // Attach additional data to the UI.

                SetDemangledFunctionNames(functionsEx);
                SetFunctionProfileInfo(functionsEx);
            }

            await ComputeFunctionStatistics();
        }

        private void ResetSectionPanel() {
            otherSummary_ = null;
            currentFunction_ = null;
            moduleReport_ = null;
            ProfileControlsVisible = false;
            FunctionModuleControlsVisible = false;
            sections_.Clear();
            sectionExtMap_.Clear();
            annotatedSections_.Clear();
            callGraph_ = null;
            otherCallGraph_ = null;
            functionStatMap_ = null;

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
            callGraph_ = null;
            otherCallGraph_ = null;
            functionStatMap_ = null;

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

        public void RefreshSectionList() {
            if (SectionList.ItemsSource == null) {
                return;
            }

            ((ListCollectionView)SectionList.ItemsSource).Refresh();
        }

        public void RefreshFunctionList() {
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

        public IRTextFunctionEx GetFunctionExtension(IRTextFunction function) {
            return functionExtMap_[function];
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

            //? TODO: A way to switch between the two modes?
            if(useProfileCallTree_ && profileControlsVisible_ && Session.ProfileData != null) {
                var funcProfile = Session.ProfileData.GetFunctionProfile(function);

                if(funcProfile != null) {
                    //? TODO: Make async
                    var profileCallTree = CreateProfileCallTree(function);
                    ChildFunctionList.Model = profileCallTree;
                    ChildTimeColumnVisible = true;
                    SetDemangledChildFunctionNames(profileCallTree);
                    AutoResizeColumns(ChildFunctionList);
                }
                else {
                    ChildFunctionList.Model = null;
                }
            }
            else {
                var callTree = await Task.Run(() => CreateCallTree(function));
                ChildFunctionList.Model = callTree;
                ChildTimeColumnVisible = false;
                SetDemangledChildFunctionNames(callTree);
                AutoResizeColumns(ChildFunctionList);
            }

            await ComputeConsecutiveSectionDiffs();
        }

        private ChildFunctionEx CreateProfileCallTree(IRTextFunction function) {
            var visitedFuncts = new HashSet<IRTextFunction>();
            var rootNode = new ChildFunctionEx();
            rootNode.Children = new List<ChildFunctionEx>();
            CreateProfileCallTree(function, rootNode, visitedFuncts);
            return rootNode;
        }

        private async Task<(CallGraph, CallGraphNode)> GenerateFunctionCallGraph(IRTextSummary summary, IRTextFunction function) {
            if(summary != summary_) {
                Trace.TraceError("BADBAD");
            }

            var callGraph = await GenerateCallGraph(summary);
            var callGraphNode = callGraph.FindNode(function);
            return (callGraph, callGraphNode);
        }

        private async Task<CallGraph> GenerateCallGraph(IRTextSummary summary) {
            if(callGraph_ != null) {
                return callGraph_;
            }

            var loadedDoc = Session.SessionState.FindLoadedDocument(summary);
            var callGraph = new CallGraph(summary, loadedDoc.Loader, Session.CompilerInfo.IR);
            
            await Task.Run(() => callGraph.Execute());
            loadedDoc.Loader.ResetCache(); // Clean up the cached functs, unlikely to be all needed later.

            // Cache the call graph, can be expensive to compute.
            callGraph_ = callGraph;
            return callGraph;
        }

        private async Task<ChildFunctionEx> CreateCallTree(IRTextFunction function) {
            ProfileControlsVisible = true;

            var (_, callGraphNode) = await GenerateFunctionCallGraph(function.ParentSummary, function);
            CallGraphNode otherNode = null;

            if (otherSummary_ != null) {
                var otherFunction = otherSummary_.FindFunction(function);

                if (otherFunction != null) {
                    (_, otherNode) = await GenerateFunctionCallGraph(otherSummary_, otherFunction);
                }
            }

            var visitedNodes = new HashSet<CallGraphNode>();
            var rootNode = new ChildFunctionEx();
            rootNode.Children = new List<ChildFunctionEx>();
            CreateCallTree(callGraphNode, otherNode, function, rootNode, visitedNodes);
            return rootNode;
        }

        private void CreateCallTree(CallGraphNode node, CallGraphNode otherNode, 
                                 IRTextFunction function, ChildFunctionEx parentNode,
                                 HashSet<CallGraphNode> visitedNodes) {
            visitedNodes.Add(node);

            if (node.HasCallers) {
                //? TODO: Sort on top of the list
                var callerInfo = CreateCallTreeChild(node, function);
                callerInfo.Name = "Callers";
                callerInfo.IsMarked = true;
                callerInfo.DescendantCount = node.UniqueCallerCount;
                parentNode.Children.Add(callerInfo);

                foreach (var callerNode in node.UniqueCallers) {
                    var callerFunc = summary_.FindFunction(callerNode.FunctionName);
                    if (callerFunc == null)
                        continue;

                    var callerNodeEx = CreateCallTreeChild(callerNode, callerFunc);
                    callerNodeEx.Statistics = GetFunctionStatistics(callerFunc);
                    callerInfo.Children.Add(callerNodeEx);
                }
            }

            foreach (var calleeNode in node.UniqueCallees) {
                var childFunc = summary_.FindFunction(calleeNode.FunctionName);
                if (childFunc == null) continue;

                // Create node and attach statistics if available.
                var childNode = CreateCallTreeChild(calleeNode, childFunc);
                childNode.Statistics = GetFunctionStatistics(childFunc);
                parentNode.Children.Add(childNode);

                var otherCalleeNode = otherNode?.FindCallee(calleeNode);

                if (otherSummary_ != null && otherCalleeNode == null) {
                    // Missing in right.
                    childNode.TextColor = ColorBrushes.GetBrush(sectionSettings_.NewSectionColor);
                    childNode.IsMarked = true;
                }

                if (calleeNode.HasCallees && !visitedNodes.Contains(calleeNode)) {
                    childNode.Children = new List<ChildFunctionEx>();
                    CreateCallTree(calleeNode, otherCalleeNode, childFunc, childNode, visitedNodes);

                    if (childNode.IsMarked && parentNode != null) {
                        parentNode.IsMarked = true;
                    }
                }
            }

            // Sort children, since that is not yet supported by the TreeListView control.
            parentNode.Children.Sort((a, b) => {
                // Ensure the callers node is placed first.
                if(a.IsMarked) {
                    return 1;
                }
                else if(b.IsMarked) {
                    return -1;
                }

                if (b.Time > a.Time) {
                    return 1;
                }
                else if (b.Time < a.Time) {
                    return -1;
                }
                return b.Name.CompareTo(a.Name);
            });
        }

        private FunctionCodeStatistics GetFunctionStatistics(IRTextFunction function) {
            var functionEx = GetFunctionExtension(function);
            return functionEx.Statistics;
        }

        private ChildFunctionEx CreateCallTreeChild(CallGraphNode childNode, IRTextFunction childFunc) {
            int instrCount = childFunc != null ? GetFunctionStatistics(childFunc).Instructions : 0;

            var childInfo = new ChildFunctionEx() {
                Function = childFunc,
                Name = childNode.FunctionName,
                DescendantCount = childNode.UniqueCalleeCount,
                TextColor = Brushes.Black,
                BackColor = ColorBrushes.GetBrush(Colors.Transparent),
                Children = new List<ChildFunctionEx>(),
            };
            return childInfo;
        }

        private void CreateProfileCallTree(IRTextFunction function, ChildFunctionEx parentNode, 
                                           HashSet<IRTextFunction> visitedFuncts) {
            bool newFunc = visitedFuncts.Add(function);
            var funcProfile = Session.ProfileData.GetFunctionProfile(function);

            if (funcProfile == null) {
                return;
            }
            
            //? TODO: Use pallette
            const int lightSteps = 10;
            List<Color> colors = ColorUtils.MakeColorPallete(1, 1, 0.85f, 0.95f, lightSteps);
            
            var selfInfo = CreateProfileCallTreeChild(function, funcProfile.ExclusiveWeight, funcProfile, null, colors);
            selfInfo.Name = "Self";
            selfInfo.IsMarked = true;
            parentNode.Children.Add(selfInfo);

            if(!newFunc) {
                return;
            }

            foreach (var pair in funcProfile.ChildrenWeights) {
                var childFunc = summary_.GetFunctionWithId(pair.Key);
                var childFuncProfile = Session.ProfileData.GetFunctionProfile(childFunc);
                var childNode = CreateProfileCallTreeChild(childFunc, pair.Value, funcProfile, childFuncProfile, colors); 
                parentNode.Children.Add(childNode);

                if (childFuncProfile.ChildrenWeights.Count > 0) {
                    CreateProfileCallTree(childFunc, childNode, visitedFuncts);
                }
            }

            var callerInfo = CreateProfileCallTreeChild(function, funcProfile.ExclusiveWeight, funcProfile, null, colors);
            callerInfo.Name = "Callers";
            callerInfo.IsMarked = true;
            parentNode.Children.Add(callerInfo);
            
            foreach (var pair in funcProfile.CallerWeights) {
                var childFunc = summary_.GetFunctionWithId(pair.Key);
                var childFuncProfile = Session.ProfileData.GetFunctionProfile(childFunc);
                var childNode = CreateProfileCallTreeChild(childFunc, pair.Value, funcProfile, childFuncProfile, colors); 
                callerInfo.Children.Add(childNode);
            }
            
            // Sort children, since that is not yet supported by the TreeListView control.
            parentNode.Children.Sort((a, b) => {
                // Ensure the callers node is placed first.
                if (a.IsMarked) {
                    return 1;
                }
                else if (b.IsMarked) {
                    return -1;
                }

                if (b.Time > a.Time) {
                    return 1;
                }
                else if (b.Time < a.Time) {
                    return -1;
                }

                return b.Name.CompareTo(a.Name);
            });
        }

        private ChildFunctionEx CreateProfileCallTreeChild(IRTextFunction childFunc, TimeSpan childWeight, 
                FunctionProfileData funcProfile, FunctionProfileData childFuncProfile, List<Color> colors) {
            KeyValuePair<int, TimeSpan> pair;
            double weightPercentage = funcProfile.ScaleChildWeight(childWeight);
            int colorIndex = (int)Math.Floor(10 * (1.0 - weightPercentage));

            var childInfo = new ChildFunctionEx()
            {
                Function = childFunc,
                Time =  childWeight.Ticks,
                Name = childFunc.Name,
                DescendantCount = childFuncProfile != null ? childFuncProfile.ChildrenWeights.Count : 0,
                Text = $"{Math.Round(weightPercentage * 100, 2)}% ({Math.Round(childWeight.TotalMilliseconds, 2)} ms)",
                TextColor = Brushes.Black,
                BackColor = ColorBrushes.GetBrush(colors[colorIndex]),
                Children = new List<ChildFunctionEx>()
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
            double profileControlsSpace = FunctionModuleControlsVisible ? 180 : 0;
            FunctionFilterGrid.Width = Math.Max(1, width - defaultSpace - profileControlsSpace);
        }
        
        private void ChildDoubleClick(object sender, MouseButtonEventArgs e) {
            if (sender is not ListViewItem) {
                return;
            }

            // A double-click on the +/- icon doesn't have select an actual node.
            var childInfo = ((ListViewItem)sender).Content as ChildFunctionEx;

            if (childInfo != null) {
                if (!childInfo.IsMarked && childInfo.Function != null) {
                    SelectFunction(childInfo.Function);
                }
            }
        }

        private async Task ComputeFunctionStatistics() {
            var loadedDoc = Session.SessionState.FindLoadedDocument(summary_);
            using var cancelableTask = statisticsTask_.CreateTask();
            Session.SetApplicationProgress(true, double.NaN, "Computing statistics");
            
            var tasks = new List<Task>();
            var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 16);
            var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);
            var callGraph = await GenerateCallGraph(summary_);
            functionStatMap_ = new ConcurrentDictionary<IRTextFunction, FunctionCodeStatistics>();

            foreach (var function in summary_.Functions) {
                if (function.SectionCount == 0) {
                    continue;
                }

                tasks.Add(taskFactory.StartNew(() => {
                    var section = function.Sections[0];
                    var sectionStats = ComputeFunctionStatistics(section, loadedDoc.Loader, callGraph);
                    functionStatMap_.TryAdd(function, sectionStats);
                }, cancelableTask.Token));
            }
            
            await Task.WhenAll(tasks.ToArray());

            foreach (var pair in functionStatMap_) {
                var functionEx = functionExtMap_[pair.Key];
                functionEx.Statistics = pair.Value;
            }

            statisticsTask_.CompleteTask(cancelableTask);
            
            AddStatisticsFunctionListColumns(false);
            AddStatisticsChildFunctionListColumns(false);
            RefreshFunctionList();

            Session.SetApplicationProgress(false, double.NaN);
        }

        public async Task WaitForStatistics() {
            await statisticsTask_.WaitForTaskAsync();
        }

        public void ShowModuleReport() {
            //? TODO: Wait for it to be computed
            if(functionStatMap_ == null) {
                return;
            }

            moduleReport_ = new ModuleReport(functionStatMap_);
            moduleReport_.Generate();

            var panel = new ModuleReportPanel();
            panel.TitleSuffix = $"Function report";
            panel.ShowReport(moduleReport_, summary_, Session);
            Session.DisplayFloatingPanel(panel);
        }

        private FunctionCodeStatistics ComputeFunctionStatistics(IRTextSection section, IRTextSectionLoader loader,
            CallGraph callGraph) {
            var stats = new FunctionCodeStatistics();
            var result = loader.LoadSection(section);
            var metadataTag = result.Function.GetTag<AssemblyMetadataTag>();

            if(metadataTag != null) {
                stats.Size = metadataTag.FunctionSize;
            }

            var irInfo = Session.CompilerInfo.IR;

            foreach(var instr in result.Function.AllInstructions) {
                stats.Instructions++;

                if(instr.IsBranch || instr.IsGoto || instr.IsSwitch) {
                    stats.Branches++;
                }

                if (irInfo.IsLoadInstruction(instr)) {
                    stats.Loads++;
                }

                if(irInfo.IsStoreInstruction(instr)) {
                    stats.Stores++;
                }

                if(irInfo.IsCallInstruction(instr)) {
                    if (irInfo.GetCallTarget(instr) == null) {
                        stats.IndirectCalls++;
                    }
                    
                    stats.Calls++;
                }
            }

            if (callGraph != null) {
                var node = callGraph.FindNode(section.ParentFunction);

                if (node != null) {
                    stats.Callees = node.UniqueCalleeCount;
                    stats.Callers = node.UniqueCallerCount;
                }
            }

            return stats;
        }
        
        private DataTemplate CreateGridColumnBindingTemplate(string propertyName, IValueConverter valueConverter = null) {
            DataTemplate template = new DataTemplate();
            FrameworkElementFactory factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Left);
            var binding = new Binding(propertyName);
            binding.Converter = valueConverter;
            factory.SetBinding(TextBlock.TextProperty, binding);
            template.VisualTree = factory;
            return template;
        }

        private GridViewColumnHeader RemoveListViewColumn(ListView listView, string columnName) {
            var functionGrid = (GridView)listView.View;

            foreach (var column in functionGrid.Columns) {
                if (column.Header is GridViewColumnHeader header) {
                    if (header.Name == columnName) {
                        functionGrid.Columns.Remove(column);
                        return header;
                    }
                }
            }

            return null;
        }

        private GridViewColumnHeader AddListViewColumn(ListView listView, string bindingValue,
                               string columnTitle, string columnName, string columnTooltip = null,
                               double columnWidth = double.NaN, IValueConverter valueConverter = null, int index = -1) {
            var functionGrid = (GridView)listView.View;
            var columnHeader = new GridViewColumnHeader() {
                Name = columnName, Content = columnTitle, ToolTip = columnTooltip
            };


            var item = new GridViewColumn {
                Header = columnHeader,
                Width = columnWidth,
                CellTemplate = CreateGridColumnBindingTemplate(bindingValue, valueConverter),
                HeaderContainerStyle = (Style)Application.Current.FindResource("ListViewHeaderStyle") as Style
            };

            if (index != -1) {
                functionGrid.Columns.Insert(index, item);
            }
            else {
                functionGrid.Columns.Add(item);
            }

            return columnHeader;
        }

        class FunctionDiffKindConverter : IValueConverter {
            public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
                if (value is DiffKind diffKind) {
                    return diffKind switch {
                        DiffKind.Insertion => "Diff only",
                        DiffKind.Deletion => "Base only",
                        DiffKind.Modification => "Modified",
                        DiffKind.MinorModification => "Modified",
                        _ => ""
                    };
                }

                return "";
            }

            public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
                return null;
            }
        }

        class FunctionDiffValueConverter : IValueConverter {
            public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
                if (value is int intValue) {
                    if (intValue > 0) {
                        return $"+{intValue}";
                    }
                    else if (intValue == 0) {
                        return $" {intValue}";
                    }
                }
                else if (value is long longValue) {
                    if (longValue > 0) {
                        return $"+{longValue}";
                    }
                    else if (longValue == 0) {
                        return $" {longValue}";
                    }
                }

                return value;
            }

            public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
                return null;
            }
        }

        private class OptionalColumnInfo {
            public OptionalColumnInfo(string binding, string title, string name, string tooltip, 
                IValueConverter converter = null, double width = Double.NaN, bool isVisible = true) {
                Binding = binding;
                Name = name;
                Title = title;
                Tooltip = tooltip;
                Width = width;
                IsVisible = isVisible;
                Converter = converter;
            }

            public string Binding { get; set; }
            public string Name { get; set; }
            public string Title { get; set; }
            public string Tooltip { get; set; }
            public double Width { get; set; }
            public bool IsVisible { get; set; }
            public IValueConverter Converter { get; set; }
        }

        private static IValueConverter DiffValueConverter = new FunctionDiffValueConverter();
        private static IValueConverter DiffKindConverter = new FunctionDiffKindConverter();
        
        private static OptionalColumnInfo[] StatisticsColumns = new OptionalColumnInfo[] {
            new OptionalColumnInfo("Statistics.Instructions", "Instrs{0}", "InstructionsHeader", "Instruction number{0}", DiffValueConverter),
            new OptionalColumnInfo("Statistics.Size", "Size{0}", "SizeHeader", "Function size in bytes{0}", DiffValueConverter),
            new OptionalColumnInfo("Statistics.Loads", "Loads{0}", "LoadsHeader", "Number of instructions reading memory{0}", DiffValueConverter),
            new OptionalColumnInfo("Statistics.Stores", "Stores{0}", "StoresHeader", "Number of instructions writing memory{0}", DiffValueConverter),
            new OptionalColumnInfo("Statistics.Branches", "Branches{0}", "BranchesHeader", "Number of branch/jump instructions{0}", DiffValueConverter),
            new OptionalColumnInfo("Statistics.Callees", "Callees{0}", "CalleesHeader", "Number of unique called functions{0}", DiffValueConverter),
            new OptionalColumnInfo("Statistics.Callers", "Callers{0}", "CallersHeader", "Number of unique caller functions{0}", DiffValueConverter),
            new OptionalColumnInfo("Statistics.Calls", "Calls{0}", "CallsHeader", "Number of call instructions{0}", DiffValueConverter),
            new OptionalColumnInfo("Statistics.IndirectCalls", "IndirectCalls{0}", "IndirectCallsHeader", "Number of indirect/virtual call instructions{0}", DiffValueConverter),

        };
        
        private static OptionalColumnInfo[] StatisticsDiffColumns = new OptionalColumnInfo[] {
            new OptionalColumnInfo("FunctionDiffKind", $"Diff",
                "DiffHeader", "Difference kind (only in left/right document or modified)", DiffKindConverter),
        };
        

        private void RemoveListViewColumns(ListView listView, OptionalColumnInfo[] columns,
            IGridViewColumnValueSorter columnSorter = null) {
            foreach (var column in columns) {
                var columnHeader = RemoveListViewColumn(listView, column.Name);
                columnSorter?.UnregisterColumnHeader(columnHeader);
            }
        }

        private void AddListViewColumns(ListView listView, OptionalColumnInfo[] columns,
            IGridViewColumnValueSorter columnSorter = null,
            string titleSuffix = "", string tooltipSuffix = "", bool useValueConverter = true) {
            foreach (var column in columns) {
                if(!column.IsVisible) continue;
                
                var columnHeader = AddListViewColumn(listView, column.Binding, 
                    string.Format(column.Title, titleSuffix), column.Name, 
                    string.Format(column.Tooltip, tooltipSuffix),
                    column.Width, useValueConverter ? column.Converter : null);
                columnSorter?.RegisterColumnHeader(columnHeader);
            }
        }
        
        public void AddStatisticsFunctionListColumns(bool addDiffColumn, string titleSuffix = "", string tooltipSuffix = "", double columnWidth = double.NaN) {
            RemoveListViewColumns(FunctionList, StatisticsColumns, functionValueSorter_);
            RemoveListViewColumns(FunctionList, StatisticsDiffColumns, functionValueSorter_);

            AddListViewColumns(FunctionList, StatisticsColumns, functionValueSorter_, titleSuffix, tooltipSuffix, addDiffColumn);

            if (addDiffColumn) {
                AddListViewColumns(FunctionList, StatisticsDiffColumns, functionValueSorter_);    
            }
        }
        
        public void AddStatisticsChildFunctionListColumns(bool addDiffColumn, string titleSuffix = "", string tooltipSuffix = "", double columnWidth = double.NaN) {
            if (Session.ProfileData == null) {
                ChildTimeColumnVisible = false;
            }

            AddListViewColumn(ChildFunctionList, "Statistics.Instructions", $"Instrs{titleSuffix}", "InstructionsHeader", $"Instruction number{tooltipSuffix}", columnWidth, null, 1);
            AddListViewColumn(ChildFunctionList, "Statistics.Size", $"Size{titleSuffix}", "SizeHeader", $"Function size in bytes{tooltipSuffix}", columnWidth, null, 2);
        }

        public void RemoveStatisticsChildFunctionListColumns(bool addDiffColumn, string titleSuffix = "", string tooltipSuffix = "", double columnWidth = double.NaN) {
            RemoveListViewColumn(FunctionList, "SizeHeader");
            RemoveListViewColumn(FunctionList, "InstructionsHeader");
        }

        private void AutoResizeColumns(ListView listView) {
            foreach (GridViewColumn column in ((GridView)listView.View).Columns) {
                column.Width = 0;
                column.Width = double.NaN;
            }
        }
    }
}