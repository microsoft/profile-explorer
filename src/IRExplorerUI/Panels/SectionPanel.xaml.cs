// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClosedXML.Excel;
using HtmlAgilityPack;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerUI.Controls;
using IRExplorerUI.OptionsPanels;
using IRExplorerUI.Panels;
using IRExplorerUI.Profile;
using IRExplorerUI.Utilities;
using ProtoBuf;
using PerformanceCounter = IRExplorerUI.Profile.PerformanceCounter;

namespace IRExplorerUI;

public enum OpenSectionKind {
  ReplaceCurrent,
  ReplaceLeft,
  ReplaceRight,
  NewTab,
  NewTabDockLeft,
  NewTabDockRight
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
  Module,
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
  StatisticDiff,
  PerformanceCounter
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

//? TODo; Commands can be defined in code-behind with the RelayCommand pattern,
//? will remove this and a lot of other Xaml code
// https://www.c-sharpcorner.com/UploadFile/20c06b/icommand-and-relaycommand-in-wpf/
// https://stackoverflow.com/questions/19573380/pass-different-commandparameters-to-same-command-using-relaycommand-wpf
// https://stackoverflow.com/questions/1361350/keyboard-shortcuts-in-wpf
//? https://stackoverflow.com/questions/13826504/how-do-you-bind-a-command-to-a-menuitem-wpf
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
  public static readonly RoutedUICommand CopySectionText =
    new RoutedUICommand("Untitled", "CopySectionText", typeof(SectionPanel));
  public static readonly RoutedUICommand SaveSectionText =
    new RoutedUICommand("Untitled", "SaveSectionText", typeof(SectionPanel));
  public static readonly RoutedUICommand SaveFunctionText =
    new RoutedUICommand("Untitled", "SaveFunctionText", typeof(SectionPanel));
  public static readonly RoutedUICommand CopyFunctionText =
    new RoutedUICommand("Untitled", "CopyFunctionText", typeof(SectionPanel));
  public static readonly RoutedUICommand OpenDocumentInEditor =
    new RoutedUICommand("Untitled", "OpenDocumentInEditor", typeof(SectionPanel));
  public static readonly RoutedUICommand OpenDocumentInNewInstance =
    new RoutedUICommand("Untitled", "OpenDocumentInNewInstance", typeof(SectionPanel));
  public static readonly RoutedUICommand DisplayCallGraph =
    new RoutedUICommand("Untitled", "DisplayCallGraph", typeof(SectionPanel));
  public static readonly RoutedUICommand DisplayPartialCallGraph =
    new RoutedUICommand("Untitled", "DisplayPartialCallGraph", typeof(SectionPanel));
  public static readonly RoutedUICommand CopyFunctionName =
    new RoutedUICommand("Untitled", "CopyFunctionName", typeof(SectionPanel));
  public static readonly RoutedUICommand CopyDemangledFunctionName =
    new RoutedUICommand("Untitled", "CopyDemangledFunctionName", typeof(SectionPanel));
  public static readonly RoutedUICommand ExportFunctionList =
    new RoutedUICommand("Untitled", "ExportFunctionList", typeof(SectionPanel));
  public static readonly RoutedUICommand ExportModuleList =
    new RoutedUICommand("Untitled", "ExportModuleList", typeof(SectionPanel));
  public static readonly RoutedUICommand CopyFunctionDetails =
    new RoutedUICommand("Untitled", "CopyFunctionDetails", typeof(SectionPanel));
  public static readonly RoutedUICommand ExportFunctionListHtml =
    new RoutedUICommand("Untitled", "ExportFunctionListHtml", typeof(SectionPanel));
  public static readonly RoutedUICommand ExportFunctionListMarkdown =
    new RoutedUICommand("Untitled", "ExportFunctionListMarkdown", typeof(SectionPanel));
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
  public bool IsModificationDiff => diffKind_ == DiffKind.Modification || diffKind_ == DiffKind.MinorModification;
  public bool IsPlaceholderDiff => diffKind_ == DiffKind.Placeholder;
  public bool HasDiffs => diffKind_ == DiffKind.Modification;
  public Brush NewSectionBrush => ColorBrushes.GetBrush(App.Settings.SectionSettings.NewSectionColor);
  public Brush MissingSectionBrush => ColorBrushes.GetBrush(App.Settings.SectionSettings.MissingSectionColor);
  public Brush ChangedSectionBrush => ColorBrushes.GetBrush(App.Settings.SectionSettings.ChangedSectionColor);
}

public class IRTextSectionEx : IRTextDiffBaseEx, INotifyPropertyChanged {
  private bool isSelected_;
  private bool isTagged_;
  private Thickness borderThickness_;
  private Brush borderBrush_;
  private string name_;
  private bool isDiffFromPrevious_;
  private bool isMarked_;

  public IRTextSectionEx(IRTextSection section, int index) : base(DiffKind.None) {
    Section = section;
    Index = index;
  }

  public IRTextSectionEx(IRTextSection section, DiffKind diffKind, string name, int index) : base(diffKind) {
    Section = section;
    Name = name;
    Index = index;
  }

  public event PropertyChangedEventHandler PropertyChanged;
  public int Index { get; set; }
  public IRTextSection Section { get; set; }

  public Thickness BorderThickness {
    get => borderThickness_;
    set {
      borderThickness_ = value;
      OnPropertyChanged(nameof(BorderThickness));
    }
  }

  public bool HasBeforeBorder => BorderThickness.Top != 0;
  public bool HasAfterBorder => BorderThickness.Bottom != 0;

  public Brush BorderBrush {
    get => borderBrush_;
    set {
      borderBrush_ = value;
      OnPropertyChanged(nameof(BorderBrush));
    }
  }

  public string Name {
    get => name_;
    set {
      name_ = value;
      OnPropertyChanged(nameof(Name));
    }
  }

  public DiffKind SectionDiffKind {
    get => diffKind_;
    set {
      diffKind_ = value;
      OnPropertyChanged(nameof(IsInsertionDiff));
      OnPropertyChanged(nameof(IsDeletionDiff));
      OnPropertyChanged(nameof(IsPlaceholderDiff));
      OnPropertyChanged(nameof(HasDiffs));
    }
  }

  public bool IsTagged {
    get => isTagged_;
    set {
      isTagged_ = value;
      OnPropertyChanged(nameof(IsTagged));
    }
  }

  public bool IsDiffFromPrevious {
    get => isDiffFromPrevious_;
    set {
      isDiffFromPrevious_ = value;
      OnPropertyChanged(nameof(IsDiffFromPrevious));
    }
  }

  public bool IsSelected {
    get => isSelected_;
    set {
      isSelected_ = value;
      OnPropertyChanged(nameof(IsSelected));
    }
  }

  public bool IsMarked {
    get => isMarked_;
    set {
      isMarked_ = value;
      OnPropertyChanged(nameof(IsMarked));
    }
  }

  public Brush TextColor { get; set; }
  public Brush BackColor { get; set; }
  public bool LowerIdenticalToPreviousOpacity { get; set; }
  public string NumberString => Section != null ? Section.Number.ToString() : "";
  public string BlockCountString => Section != null ? Section.BlockCount.ToString() : "";
  public int Number => Section?.Number ?? 0;
  public int BlockCount => Section?.BlockCount ?? 0;

  public void OnPropertyChanged(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }
}

public class PerformanceCounterSetEx {
  public PerformanceCounterSetEx(int count) {
    Counters = new List<PerformanceCounterValueEx>(count);
  }

  public List<PerformanceCounterValueEx> Counters { get; set; }
  public string this[int perfCounterId] => FindCounterLabel(perfCounterId);

  public string FindCounterLabel(int perfCounterId) {
    int index = Counters.FindIndex(item => item.CounterId == perfCounterId);
    return index != -1 ? Counters[index].Label : null;
  }

  public double FindCounterValue(int perfCounterId) {
    int index = Counters.FindIndex(item => item.CounterId == perfCounterId);
    return index != -1 ? Counters[index].Value : 0;
  }

  public void Add(PerformanceCounterValueEx counterEx) {
    Counters.Add(counterEx);
  }

  public class PerformanceCounterValueEx {
    public int CounterId { get; set; }
    public double Value { get; set; }
    public string Label { get; set; }
  }
}

public class IRTextFunctionEx : IRTextDiffBaseEx, INotifyPropertyChanged {
  private string functionName_;
  private FunctionNameFormatter funcNameFormatter_;
  private bool isMarked_;

  public IRTextFunctionEx(IRTextFunction function, int index,
                          FunctionNameFormatter funcNameFormatter) : base(DiffKind.None) {
    Function = function;
    Index = index;
    funcNameFormatter_ = funcNameFormatter;

    Statistics = new FunctionCodeStatistics();
  }

  public event PropertyChangedEventHandler PropertyChanged;

  public virtual string Name {
    get {
      if (functionName_ != null) {
        return functionName_; // Cached.
      }

      functionName_ = Function.Name;

      if (funcNameFormatter_ != null) {
        functionName_ = funcNameFormatter_(functionName_);
      }

      return functionName_;
    }
    set => functionName_ = value;
  }

  public int Index { get; set; }
  public IRTextFunction Function { get; set; }
  public string ModuleName => Function.ParentSummary.ModuleName;
  public TimeSpan Weight { get; set; }
  public TimeSpan ExclusiveWeight { get; set; }
  public string AlternateName { get; set; }
  public double ExclusivePercentage { get; set; }
  public double Percentage { get; set; }

  public bool IsMarked {
    get => isMarked_;
    set {
      isMarked_ = value;
      OnPropertyChanged(nameof(IsMarked));
    }
  }

  public Brush TextColor { get; set; }
  public Brush BackColor { get; set; }
  public Brush BackColor2 { get; set; }
  public int SectionCount => Function.SectionCount;
  public FunctionCodeStatistics Statistics { get; set; }
  public FunctionCodeStatistics DiffStatistics { get; set; }
  public PerformanceCounterSetEx Counters { get; set; }

  public DiffKind FunctionDiffKind {
    get => diffKind_;
    set {
      diffKind_ = value;
      OnPropertyChanged(nameof(IsInsertionDiff));
      OnPropertyChanged(nameof(IsDeletionDiff));
      OnPropertyChanged(nameof(IsPlaceholderDiff));
      OnPropertyChanged(nameof(HasDiffs));
    }
  }

  public void OnPropertyChanged(string propertyname) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
  }
}

public class ModuleEx {
  public string Name { get; set; }
  public Brush BackColor { get; set; }
  public double ExclusivePercentage { get; set; }
  public TimeSpan ExclusiveWeight { get; set; }
  public bool BinaryFileMissing { get; set; }
  public bool DebugFileMissing { get; set; }
  public ProfileDataReport.ModuleStatus Status { get; set; }
}

[ProtoContract]
public class SectionPanelState {
  [ProtoMember(1)]
  public List<int> AnnotatedSections;
  [ProtoMember(2)]
  public int SelectedFunctionNumber;
  [ProtoMember(3)]
  public int SelectedSectionNumber;

  public SectionPanelState() {
    AnnotatedSections = new List<int>();
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
  private static IValueConverter DiffValueConverter = new FunctionDiffValueConverter();
  private static IValueConverter DiffKindConverter = new FunctionDiffKindConverter();
  private static OptionalColumn[] StatisticsColumns = {
    OptionalColumn.Binding("Statistics.Instructions", "InstructionsHeader", "Instrs{0}", "Instruction number{0}",
                           DiffValueConverter),
    OptionalColumn.Binding("Statistics.Size", "SizeHeader", "Size{0}", "Function size in bytes{0}", DiffValueConverter),
    OptionalColumn.Binding("Statistics.Loads", "LoadsHeader", "Loads{0}", "Number of instructions reading memory{0}",
                           DiffValueConverter),
    OptionalColumn.Binding("Statistics.Stores", "StoresHeader", "Stores{0}", "Number of instructions writing memory{0}",
                           DiffValueConverter),
    OptionalColumn.Binding("Statistics.Branches", "BranchesHeader", "Branches{0}",
                           "Number of branch/jump instructions{0}", DiffValueConverter)
  };
  private static OptionalColumn[] CallStatisticsColumns = {
    OptionalColumn.Binding("Statistics.Callees", "CalleesHeader", "Callees{0}", "Number of unique called functions{0}",
                           DiffValueConverter),
    OptionalColumn.Binding("Statistics.Callers", "CallersHeader", "Callers{0}", "Number of unique caller functions{0}",
                           DiffValueConverter),
    OptionalColumn.Binding("Statistics.Calls", "CallsHeader", "Calls{0}", "Number of call instructions{0}",
                           DiffValueConverter),
    OptionalColumn.Binding("Statistics.IndirectCalls", "IndirectCallsHeader", "IndirectCalls{0}",
                           "Number of indirect/virtual call instructions{0}", DiffValueConverter)
  };
  private static OptionalColumn[] StatisticsDiffColumns = {
    OptionalColumn.Binding("FunctionDiffKind",
                           "DiffHeader", "Diff", "Difference kind (only in left/right document or modified)",
                           DiffKindConverter)
  };
  private HashSet<IRTextSectionEx> annotatedSections_;
  private IRTextFunction currentFunction_;
  private string documentTitle_;
  private bool isDiffModeEnabled_;
  private bool syncDiffedDocuments_;
  private bool isFunctionListVisible_;
  private bool isSectionListVisible_;
  private bool useProfileCallTree_;
  private SectionSettings settings_;
  private IRTextSummary summary_;
  private IRTextSummary otherSummary_;
  private List<IRTextSectionEx> sections_;
  private List<IRTextSummary> moduleSummaries_;
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
  private Dictionary<IRTextSummary, CallGraph> callGraphCache_;
  private ModuleReport moduleReport_;
  private CancelableTaskInstance statisticsTask_;
  private CancelableTaskInstance callGraphTask_;
  private ConcurrentDictionary<IRTextFunction, FunctionCodeStatistics> functionStatMap_;
  private ModuleEx activeModuleFilter_;
  private bool filterTagged_;
  private bool filterDiffFromPrevious_;
  private string optionalDataColumnName_;
  private bool optionalDataColumnVisible_;
  private string optionalDataColumnName2_;
  private bool optionalDataColumnVisible2_;
  private bool alternateNameColumnVisible_;
  private bool childTimeColumnVisible_;
  private bool showChildren_;
  private bool showSections_;
  private bool profileControlsVisible_;
  private bool sectionCountColumnVisible_;
  private bool moduleControlsVisible_;
  private CallTreeNodePopup funcBacktracePreviewPopup_;
  private PopupHoverPreview preview_;

  public SectionPanel() {
    InitializeComponent();
    sections_ = new List<IRTextSectionEx>();
    moduleSummaries_ = new List<IRTextSummary>();
    sectionExtMap_ = new Dictionary<IRTextSection, IRTextSectionEx>();
    functionExtMap_ = new Dictionary<IRTextFunction, IRTextFunctionEx>();
    annotatedSections_ = new HashSet<IRTextSectionEx>();
    IsFunctionListVisible = true;
    IsSectionListVisible = true;
    ShowSections = true;
    SectionCountColumnVisible = true;
    SyncDiffedDocuments = true;
    MainGrid.DataContext = this;
    settings_ = App.Settings.SectionSettings;

    functionValueSorter_ =
      new GridViewColumnValueSorter<FunctionFieldKind>(FunctionList,
        name => name switch {
          "FunctionColumnHeader"       => FunctionFieldKind.Name,
          "AlternateNameColumnHeader"  => FunctionFieldKind.AlternateName,
          "SectionsColumnHeader"       => FunctionFieldKind.Sections,
          "FunctionModuleColumnHeader" => FunctionFieldKind.Module,
          "OptionalColumnHeader"       => FunctionFieldKind.Optional,
          "OptionalColumnHeader2"      => FunctionFieldKind.Optional2,
          "SizeHeader"                 => FunctionFieldKind.StatisticSize,
          "LoadsHeader"                => FunctionFieldKind.StatisticLoads,
          "StoresHeader"               => FunctionFieldKind.StatisticStores,
          "InstructionsHeader" => FunctionFieldKind.
            StatisticInstructions,
          "BranchesHeader" => FunctionFieldKind.StatisticBranches,
          "CallsHeader"    => FunctionFieldKind.StatisticCalls,
          "CallersHeader"  => FunctionFieldKind.StatisticCallers,
          "CalleesHeader"  => FunctionFieldKind.StatisticCallees,
          "IndirectCallsHeader" => FunctionFieldKind.
            StatisticIndirectCalls,
          "DiffHeader" => FunctionFieldKind.StatisticDiff,
          _            => FunctionFieldKind.PerformanceCounter
        },
        (x, y, field, direction, tag) => {
          var functionX = x as IRTextFunctionEx;
          var functionY = y as IRTextFunctionEx;

          switch (field) {
            case FunctionFieldKind.Sections: {
              int result = functionY.SectionCount -
                           functionX.SectionCount;
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.Name: {
              int result = string.Compare(
                functionY.Name, functionX.Name,
                StringComparison.OrdinalIgnoreCase);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.AlternateName: {
              int result = string.Compare(
                functionY.AlternateName, functionX.AlternateName,
                StringComparison.OrdinalIgnoreCase);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.Module: {
              int result = string.Compare(
                functionY.ModuleName, functionX.ModuleName,
                StringComparison.OrdinalIgnoreCase);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.Optional: {
              int result =
                functionY.ExclusiveWeight.CompareTo(
                  functionX.ExclusiveWeight);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.Optional2: {
              int result = functionY.Weight.CompareTo(functionX.Weight);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.StatisticSize: {
              int result =
                functionY.Statistics.Size.CompareTo(
                  functionX.Statistics.Size);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.StatisticInstructions: {
              int result =
                functionY.Statistics.Instructions.CompareTo(
                  functionX.Statistics.Instructions);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.StatisticLoads: {
              int result =
                functionY.Statistics.Loads.CompareTo(
                  functionX.Statistics.Loads);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.StatisticStores: {
              int result =
                functionY.Statistics.Stores.CompareTo(
                  functionX.Statistics.Stores);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.StatisticBranches: {
              int result =
                functionY.Statistics.Branches.CompareTo(
                  functionX.Statistics.Branches);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.StatisticCalls: {
              int result =
                functionY.Statistics.Calls.CompareTo(
                  functionX.Statistics.Calls);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.StatisticCallees: {
              int result =
                functionY.Statistics.Callees.CompareTo(
                  functionX.Statistics.Callees);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.StatisticCallers: {
              int result =
                functionY.Statistics.Callers.CompareTo(
                  functionX.Statistics.Callers);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.StatisticIndirectCalls: {
              int result =
                functionY.Statistics.IndirectCalls.CompareTo(
                  functionX.Statistics.IndirectCalls);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.StatisticDiff: {
              int result = 0;

              if (functionY.FunctionDiffKind !=
                  functionX.FunctionDiffKind) {
                if (functionY.IsDeletionDiff ||
                    functionX.IsInsertionDiff) {
                  result = -1;
                }
                else if (functionY.IsInsertionDiff ||
                         functionX.IsDeletionDiff) {
                  result = 1;
                }
                else if (functionY.IsModificationDiff) {
                  result = -1;
                }
                else if (functionX.IsModificationDiff) {
                  result = 1;
                }
                else {
                  result = string.Compare(
                    functionY.Name, functionX.Name,
                    StringComparison.Ordinal);
                }
              }
              else {
                result = string.Compare(
                  functionY.Name, functionX.Name,
                  StringComparison.Ordinal);
              }

              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case FunctionFieldKind.PerformanceCounter: {
              if (tag is PerformanceCounter counter) {
                int result = 0;

                if (functionX.Counters != null &&
                    functionY.Counters != null) {
                  double valueX =
                    functionX.Counters.FindCounterValue(counter.Id);
                  double valueY =
                    functionY.Counters.FindCounterValue(counter.Id);
                  result = valueX.CompareTo(valueY);
                }
                else if (functionY.Counters == null &&
                         functionX.Counters != null) {
                  result = 1;
                }
                else if (functionX.Counters == null &&
                         functionY.Counters != null) {
                  result = -1;
                }

                return direction == ListSortDirection.Ascending ? -result
                  : result;
              }

              return 0;
            }
            default:
              throw new ArgumentOutOfRangeException();
          }
        });

    sectionValueSorter_ =
      new GridViewColumnValueSorter<SectionFieldKind>(SectionList,
        name => name switch {
          "NumberColumnHeader" => SectionFieldKind.Number,
          "NameColumnHeader"   => SectionFieldKind.Name,
          "BlocksColumnHeader" => SectionFieldKind.Blocks
        },
        (x, y, field, direction, tag) => {
          var sectionX = x as IRTextSectionEx;
          var sectionY = y as IRTextSectionEx;

          switch (field) {
            case SectionFieldKind.Number: {
              int result = sectionY.Number - sectionX.Number;
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case SectionFieldKind.Name: {
              int result = string.Compare(
                sectionY.Name, sectionX.Name, StringComparison.Ordinal);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            case SectionFieldKind.Blocks: {
              int result = sectionY.BlockCount - sectionX.BlockCount;
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            default:
              throw new ArgumentOutOfRangeException();
          }
        });

    moduleValueSorter_ =
      new GridViewColumnValueSorter<ModuleFieldKind>(ModulesList,
        name => name switch {
          "ModuleColumnHeader" => ModuleFieldKind.Name
        },
        (x, y, field, direction, tag) => {
          var moduleX = x as ModuleEx;
          var moduleY = y as ModuleEx;

          switch (field) {
            case ModuleFieldKind.Name: {
              // Always sort modules by time.
              int result =
                moduleY.ExclusiveWeight.CompareTo(moduleX.ExclusiveWeight);
              return direction == ListSortDirection.Ascending ? -result
                : result;
            }
            default:
              throw new ArgumentOutOfRangeException();
          }
        });
  }

  public event EventHandler<IRTextFunction> FunctionSwitched;
  public event EventHandler<OpenSectionEventArgs> OpenSection;
  public event EventHandler<DiffModeEventArgs> EnterDiffMode;
  public event EventHandler<double> SectionListScrollChanged;
  public event EventHandler<bool> SyncDiffedDocumentsChanged;
  public event EventHandler<DisplayCallGraphEventArgs> DisplayCallGraph;
  public event PropertyChangedEventHandler PropertyChanged;

  //? TODO: Replace all other commands with RelayCommand.
  public RelayCommand<object> SelectFunctionCallTreeCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.CallTree);
  });
  public RelayCommand<object> SelectFunctionFlameGraphCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.FlameGraph);
  });
  public RelayCommand<object> SelectFunctionTimelineCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(obj, ToolPanelKind.Timeline);
  });

  public RelayCommand<object> PreviewFunctionCommand => new RelayCommand<object>(async obj => {
    if (obj is IRTextFunctionEx funcEx) {
      await IRDocumentPopupInstance.ShowPreviewPopup(funcEx.Function, "",
                                                     FunctionList, Session);
    }
  });

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
        OnPropertyChanged();
      }
    }
  }

  public bool IsSectionListVisible {
    get => isSectionListVisible_;
    set {
      if (isSectionListVisible_ != value) {
        isSectionListVisible_ = value;
        OnPropertyChanged();
      }
    }
  }

  public string DocumentTitle {
    get => documentTitle_;
    set {
      if (documentTitle_ != value) {
        documentTitle_ = value;
        OnPropertyChanged();
      }
    }
  }

  public bool IsDiffModeEnabled {
    get => isDiffModeEnabled_;
    set {
      if (isDiffModeEnabled_ != value) {
        isDiffModeEnabled_ = value;
        OnPropertyChanged();
      }
    }
  }

  public bool SyncDiffedDocuments {
    get => syncDiffedDocuments_;
    set {
      if (syncDiffedDocuments_ != value) {
        syncDiffedDocuments_ = value;
        OnPropertyChanged();
        SyncDiffedDocumentsChanged?.Invoke(this, value);
      }
    }
  }

  public bool FilterTagged {
    get => filterTagged_;
    set {
      if (filterTagged_ != value) {
        filterTagged_ = value;
        OnPropertyChanged();
        RefreshSectionList();
      }
    }
  }

  public bool FilterDiffFromPrevious {
    get => filterDiffFromPrevious_;
    set {
      if (filterDiffFromPrevious_ != value) {
        filterDiffFromPrevious_ = value;
        OnPropertyChanged();
        RefreshSectionList();
      }
    }
  }

  public bool UseProfileCallTree {
    get => useProfileCallTree_;
    set {
      if (useProfileCallTree_ != value) {
        useProfileCallTree_ = value;
        OnPropertyChanged();
        OnPropertyChanged(nameof(UseIRCallTree));
      }
    }
  }

  public bool UseIRCallTree {
    get => !useProfileCallTree_;
    set {
      if (useProfileCallTree_ == value) {
        useProfileCallTree_ = !value;
        OnPropertyChanged(nameof(UseProfileCallTree));
        OnPropertyChanged();
      }
    }
  }

  public bool SyncSourceFile {
    get => settings_.SyncSourceFile;
    set {
      if (value != settings_.SyncSourceFile) {
        settings_.SyncSourceFile = value;
        OnPropertyChanged();
      }
    }
  }

  public bool SyncSelection {
    get => settings_.SyncSelection;
    set {
      if (value != settings_.SyncSelection) {
        settings_.SyncSelection = value;
        OnPropertyChanged();
      }
    }
  }

  //? TODO: Remember column width, order
  public double SelfTimeColumnWidth => settings_ is {AppendTimeToSelfColumn: true} ? 150 : 60;
  public double TotalTimeColumnWidth => settings_ is { AppendTimeToTotalColumn: true } ? 150 : 60;

  public IRTextSummary Summary {
    get => summary_;
    set {
      if (value != summary_) {
        summary_ = value;
        sectionExtensionComputed_ = false;
        //SetupFunctionList();
      }
    }
  }

  public IRTextSummary OtherSummary {
    get => otherSummary_;
    set {
      if (value != otherSummary_) {
        otherSummary_ = value;
        //SetupFunctionList(false);
      }
    }
  }

  public bool IsInTwoDocumentsMode => Session != null && Session.IsInTwoDocumentsMode;

  public string OptionalDataColumnName {
    get => optionalDataColumnName_;
    set {
      if (optionalDataColumnName_ != value) {
        optionalDataColumnName_ = value;
        OnPropertyChanged();
      }
    }
  }

  public bool OptionalDataColumnVisible {
    get => optionalDataColumnVisible_;
    set {
      if (optionalDataColumnVisible_ != value) {
        optionalDataColumnVisible_ = value;
        OnPropertyChanged();
      }
    }
  }

  public string OptionalDataColumnName2 {
    get => optionalDataColumnName2_;
    set {
      if (optionalDataColumnName2_ != value) {
        optionalDataColumnName2_ = value;
        OnPropertyChanged();
      }
    }
  }

  public bool OptionalDataColumnVisible2 {
    get => optionalDataColumnVisible2_;
    set {
      if (optionalDataColumnVisible2_ != value) {
        optionalDataColumnVisible2_ = value;
        OnPropertyChanged();
      }
    }
  }

  public bool AlternateNameColumnVisible {
    get => alternateNameColumnVisible_;
    set {
      if (alternateNameColumnVisible_ != value) {
        alternateNameColumnVisible_ = value;
        OnPropertyChanged();
      }
    }
  }

  public bool ChildTimeColumnVisible {
    get => childTimeColumnVisible_;
    set {
      if (childTimeColumnVisible_ != value) {
        childTimeColumnVisible_ = value;
        OnPropertyChanged();
      }
    }
  }

  public bool ShowModules {
    get => settings_.ShowModulePanel;
    set {
      if (settings_.ShowModulePanel != value) {
        settings_.ShowModulePanel = value;
        OnPropertyChanged();
        ResizeFunctionFilter(FunctionToolbar.RenderSize.Width);
      }
    }
  }

  public bool ShowChildren {
    get => showChildren_;
    set {
      if (showChildren_ != value) {
        showChildren_ = value;

        if (showChildren_) {
          ShowSections = false;
          IsSectionListVisible = true;
        }
        else {
          IsSectionListVisible = false;
        }

        OnPropertyChanged(nameof(ShowSections));
        OnPropertyChanged();
        OnPropertyChanged(nameof(IsSectionListVisible));
        ResizeFunctionFilter(FunctionToolbar.RenderSize.Width);
      }
    }
  }

  public bool ShowSections {
    get => showSections_;
    set {
      if (showSections_ != value) {
        showSections_ = value;

        if (showSections_) {
          ShowChildren = false;
          IsSectionListVisible = true;
        }
        else {
          IsSectionListVisible = false;
        }

        OnPropertyChanged();
        OnPropertyChanged(nameof(ShowChildren));
        OnPropertyChanged(nameof(IsSectionListVisible));
        ResizeFunctionFilter(FunctionToolbar.RenderSize.Width);
      }
    }
  }

  public bool ProfileControlsVisible {
    get => profileControlsVisible_;
    set {
      if (profileControlsVisible_ != value) {
        profileControlsVisible_ = value;
        OnPropertyChanged();
      }
    }
  }

  public bool SectionCountColumnVisible {
    get => sectionCountColumnVisible_;
    set {
      if (sectionCountColumnVisible_ != value) {
        sectionCountColumnVisible_ = value;
        OnPropertyChanged();
      }
    }
  }

  public bool ModuleControlsVisible {
    get => moduleControlsVisible_;
    set {
      if (moduleControlsVisible_ != value) {
        moduleControlsVisible_ = value;
        OnPropertyChanged();
      }
    }
  }

  public IRTextFunction CurrentFunction => currentFunction_;
  public bool HasAnnotatedSections => annotatedSections_.Count > 0;
  public ICompilerInfoProvider CompilerInfo { get; set; }

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
    if (!settings_.MarkAnnotatedSections) {
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
    SetupSectionList(section.ParentFunction, force);
    var sectionEx = GetSectionExtension(section);
    SectionList.SelectedItem = sectionEx;

    if (sectionEx != null) {
      MarkCurrentSection(sectionEx);
      BringIntoViewSelectedListItem(SectionList, focus);
    }
  }

  public void AddModuleSummary(IRTextSummary summary) {
    if (!moduleSummaries_.Contains(summary)) {
      moduleSummaries_.Add(summary);
      sectionExtensionComputed_ = false;
    }
  }

  public bool HasSummary(IRTextSummary summary) {
    return summary == summary_ ||
           moduleSummaries_.Contains(summary);
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

  public async Task Update(bool force = false, bool analyzeFunctions = true) {
    if (summary_ != null) {
      await SetupFunctionList(force);
    }
  }

  public async Task ResetUI() {
    await ResetStatistics();
    ResetSectionPanel();
  }

  public async Task SetupFunctionList(bool force = false, bool analyzeFunctions = true) {
    if (summary_ == null) {
      await ResetUI();
      return;
    }

    // Create mappings between each section and their UI counterpart.
    SetupSectionExtensions(force);

    // Create mappings between each function and their UI counterparts.
    // In two-document diff mode, also add entries for functions that are found
    // only in the left or in the right document and mark them as diffs.
    var functionsEx = SetupFunctionExtensions();

    // Prepare UI.
    await SetupFunctionListUI(functionsEx);

    // Attach additional data to the UI.
    await LoadFunctionProfileInfo(functionsEx);

    if (analyzeFunctions) {
      await RunFunctionAnalysis(functionsEx);
    }
  }

  public List<IRTextSectionEx> CreateSectionsExtension(bool force = false) {
    SetupSectionExtensions(force);
    var sections = new List<IRTextSectionEx>();
    int sectionIndex = 0;

    if (currentFunction_ == null) {
      return sections;
    }

    foreach (var section in currentFunction_.Sections) {
      if (!sectionExtMap_.ContainsKey(section)) {
        Utils.WaitForDebugger();
      }

      var sectionEx = sectionExtMap_[section];
      sectionEx = new IRTextSectionEx(section, sectionEx.Index);
      sectionEx.Name = CompilerInfo.NameProvider.GetSectionName(section, false);

      sectionExtMap_[section] = sectionEx;
      sections.Add(sectionEx);

      if (CompilerInfo.SectionStyleProvider.IsMarkedSection(section, out var markedName)) {
        if (settings_.ColorizeSectionNames) {
          sectionEx.IsMarked = true;
          sectionEx.TextColor = ColorBrushes.GetBrush(markedName.TextColor);
        }
        else {
          sectionEx.IsMarked = false;
        }

        if (settings_.ShowSectionSeparators) {
          ApplySectionBorder(sectionEx, sectionIndex, markedName, sections);
        }
        else
          sectionEx.BorderThickness = new Thickness();

        if (settings_.UseNameIndentation &&
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
                                                  settings_.LowerIdenticalToPreviousOpacity;
      sectionIndex++;
    }

    return sections;
  }

  public void DiffSelectedSection() {
    if (SectionList.SelectedItem != null) {
      var sectionEx = SectionList.SelectedItem as IRTextSectionEx;
      var args = new DiffModeEventArgs {
        IsWithOtherDocument = true, Left = new OpenSectionEventArgs(sectionEx.Section, OpenSectionKind.NewTabDockLeft)
      };
      EnterDiffMode?.Invoke(this, args);
    }
  }

  public void RefreshSectionList() {
    if (SectionList.ItemsSource == null) {
      return;
    }

    ((ListCollectionView)SectionList.ItemsSource).Refresh();

    if (SectionList.Items.Count > 0) {
      SectionList.ScrollIntoView(SectionList.Items[0]);
    }
  }

  public void RefreshFunctionList() {
    if (FunctionList.ItemsSource == null) {
      return;
    }

    ((ListCollectionView)FunctionList.ItemsSource).Refresh();

    if (FunctionList.Items.Count > 0) {
      FunctionList.ScrollIntoView(FunctionList.Items[0]);
    }
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
    OpenSectionImpl(section, OpenSectionKind.ReplaceCurrent, targetDocument);
  }

  public void SwitchToSection(IRTextSection section, IRDocumentHost targetDocument = null) {
    SwitchToSection(Sections.Find(item => item.Section == section), targetDocument);
  }

  public void ScrollSectionList(double offset) {
    RegisterSectionListScrollEvent();
    sectionsScrollViewer_?.ScrollToVerticalOffset(offset);
  }

  public async Task WaitForStatistics() {
    await statisticsTask_.WaitForTaskAsync();
  }

  public void ShowModuleReport() {
    //? TODO: Wait for it to be computed
    if (functionStatMap_ == null) {
      return;
    }

    moduleReport_ = new ModuleReport(functionStatMap_);
    moduleReport_.Generate();

    var panel = new ModuleReportPanel(Session);
    panel.TitleSuffix = "Function report";
    panel.ShowReport(moduleReport_, summary_);
    Session.DisplayFloatingPanel(panel);
  }

  public void AddCountersFunctionListColumns(bool addDiffColumn, string titleSuffix = "", string tooltipSuffix = "",
                                             double columnWidth = double.NaN) {
    OptionalColumn.RemoveListViewColumns(FunctionList, header => header.Tag is PerformanceCounter);
    var counters = Session.ProfileData.SortedPerformanceCounters;

    for (int i = 0; i < counters.Count; i++) {
      var counter = counters[i];

      if (!ProfileDocumentMarker.IsPerfCounterVisible(counter)) {
        continue;
      }

      if ((!settings_.ShowPerformanceCounterColumns && !counter.IsMetric) ||
          (!settings_.ShowPerformanceMetricColumns && counter.IsMetric)) {
        continue;
      }

      string name = $"{ProfileDocumentMarker.ShortenPerfCounterName(counter.Name)}";
      string tooltip = /*counter?.Config?.Description != null ? $"{counter.Config.Description}" : */ $"{counter.Name}";
      int insertionIndex = -1;

      // Insert before the demangled func name column.
      if (AlternateNameColumnVisible) {
        insertionIndex = OptionalColumn.FindListViewColumnIndex("AlternateNameColumnHeader", FunctionList);
      }

      var gridColumn = OptionalColumn.AddListViewColumn(FunctionList,
                                                        OptionalColumn.Binding(
                                                          $"Counters[{counter.Id}]", $"PerfCounters{i}", name, tooltip),
                                                        functionValueSorter_, "", " counter", true, insertionIndex);
      gridColumn.Header.Tag = counter;
    }
  }

  public void AddStatisticsFunctionListColumns(bool addDiffColumn, string titleSuffix = "", string tooltipSuffix = "",
                                               double columnWidth = double.NaN) {
    OptionalColumn.RemoveListViewColumns(FunctionList, StatisticsColumns, functionValueSorter_);
    OptionalColumn.RemoveListViewColumns(FunctionList, CallStatisticsColumns, functionValueSorter_);
    OptionalColumn.RemoveListViewColumns(FunctionList, StatisticsDiffColumns, functionValueSorter_);

    // Insert before the demangled func name column.
    int insertionIndex = -1;

    if (AlternateNameColumnVisible) {
      insertionIndex = OptionalColumn.FindListViewColumnIndex("AlternateNameColumnHeader", FunctionList);
    }

    var list = OptionalColumn.AddListViewColumns(FunctionList, StatisticsColumns, functionValueSorter_, titleSuffix,
                                                 tooltipSuffix, addDiffColumn, insertionIndex);
    insertionIndex = insertionIndex != -1 ? insertionIndex + list.Count : -1;

    if (settings_.IncludeCallGraphStatistics) {
      var list2 = OptionalColumn.AddListViewColumns(FunctionList, CallStatisticsColumns, functionValueSorter_,
                                                    titleSuffix, tooltipSuffix, addDiffColumn, insertionIndex);
      insertionIndex = insertionIndex != -1 ? insertionIndex + list2.Count : -1;
    }

    if (addDiffColumn) {
      OptionalColumn.AddListViewColumns(FunctionList, StatisticsDiffColumns, functionValueSorter_, null, null, false,
                                        insertionIndex);
    }
  }

  public void MarkFunctions(List<IRTextFunction> list) {
    foreach (var func in list) {
      if (functionExtMap_.TryGetValue(func, out var funcEx)) {
        funcEx.IsMarked = true;
      }
    }
  }

  public void ClearMarkedFunctions() {
    foreach (var funcEx in functionExtMap_.Values) {
      funcEx.IsMarked = false;
    }
  }

  protected void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  private async Task SelectFunctionInPanel(object target, ToolPanelKind panelKind) {
    if (target is IRTextFunctionEx funcEx) {
      await Session.SelectProfileFunctionInPanel(funcEx.Function, panelKind);
    }
  }

  private void BringIntoViewSelectedListItem(ListView list, bool focus) {
    list.ScrollIntoView(list.SelectedItem);

    if (focus) {
      var item = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as ListViewItem;
      item?.Focus();
    }
  }

  private void SectionsScrollViewer__ScrollChanged(object sender, ScrollChangedEventArgs e) {
    SectionListScrollChanged?.Invoke(this, e.VerticalOffset);
  }

  private void SetDemangledFunctionNames(List<IRTextFunctionEx> functions) {
    var nameProvider = Session.CompilerInfo.NameProvider;

    if (!nameProvider.IsDemanglingEnabled) {
      AlternateNameColumnVisible = false;

      // Clear the cached names.
      foreach (var funcEx in functions) {
        string funcName = funcEx.Function.Name;
        funcEx.Name = null;
      }

      return;
    }

    // Set mangled name as alternate.
    foreach (var funcEx in functions) {
      funcEx.AlternateName = funcEx.Function.Name;
    }

    AlternateNameColumnVisible = true;
  }

  private async Task LoadFunctionProfileInfo(List<IRTextFunctionEx> functions) {
    var profile = Session.ProfileData;

    if (profile == null) {
      OptionalDataColumnVisible = false;
      OptionalDataColumnVisible2 = false;
      return;
    }

    var modulesEx = new List<ModuleEx>();

    foreach (var pair in profile.ModuleWeights) {
      double weightPercentage = profile.ScaleModuleWeight(pair.Value);

      var moduleInfo = new ModuleEx {
        Name = profile.Modules[pair.Key].ModuleName,
        ExclusivePercentage = weightPercentage,
        ExclusiveWeight = pair.Value
      };

      // Set warnings for missing binary/debug files.
      var moduleStatus = profile.Report?.GetModuleStatus(profile.Modules[pair.Key].ModuleName);

      if (moduleStatus != null) {
        moduleInfo.Status = moduleStatus;
        moduleInfo.BinaryFileMissing = !moduleStatus.HasBinaryLoaded;
        moduleInfo.DebugFileMissing = !moduleStatus.HasDebugInfoLoaded;
      }

      modulesEx.Add(moduleInfo);
    }

    // Add one entry to represent all modules.
    double allWeightPercentage = profile.ScaleFunctionWeight(profile.ProfileWeight);
    modulesEx.Add(new ModuleEx {
      Name = "All",
      ExclusivePercentage = allWeightPercentage,
      ExclusiveWeight = profile.ProfileWeight
    });

    var modulesFilter = new ListCollectionView(modulesEx);
    ModulesList.ItemsSource = modulesFilter;
    moduleValueSorter_.SortByField(ModuleFieldKind.Name);

    OptionalDataColumnVisible = true;
    OptionalDataColumnName = "Time (self)";
    OptionalDataColumnVisible2 = true;
    OptionalDataColumnName2 = "Time (total)";
    var markerOptions = App.Settings.DocumentSettings.ProfileMarkerSettings;
    bool counterColumnsAdded = false;

    //? TODO: Can be expensive, multithread and async
    foreach (var funcEx in functions) {
      var funcProfile = profile.GetFunctionProfile(funcEx.Function);

      if (funcProfile != null) {
        double exclusivePercentage = profile.ScaleFunctionWeight(funcProfile.ExclusiveWeight);
        funcEx.ExclusivePercentage = exclusivePercentage;
        funcEx.ExclusiveWeight = funcProfile.ExclusiveWeight;
        funcEx.BackColor = markerOptions.PickBrushForPercentage(exclusivePercentage);

        double percentage = profile.ScaleFunctionWeight(funcProfile.Weight);
        funcEx.Percentage = percentage;
        funcEx.Weight = funcProfile.Weight;
        funcEx.BackColor2 = markerOptions.PickBrushForPercentage(percentage);

        if (funcProfile.HasPerformanceCounters) {
          var counters = funcProfile.ComputeFunctionTotalCounters();
          funcEx.Counters = new PerformanceCounterSetEx(counters.Count);

          // Add values for metrics first.
          foreach (var counter in profile.SortedPerformanceCounters) {
            if (counter.IsMetric) {
              var counterEx = new PerformanceCounterSetEx.PerformanceCounterValueEx {CounterId = counter.Id};
              var metric = counter as PerformanceMetric;
              counterEx.Value = metric.ComputeMetric(counters, out long _, out long _);
              counterEx.Label = ProfileDocumentMarker.FormatPerformanceMetric(counterEx.Value, metric);
              funcEx.Counters.Add(counterEx);
            }
          }

          foreach (var counter in counters.Counters) {
            var counterInfo = profile.GetPerformanceCounter(counter.CounterId);

            if (!counterInfo.IsMetric) {
              var counterEx = new PerformanceCounterSetEx.PerformanceCounterValueEx {CounterId = counter.CounterId};
              counterEx.Value = counter.Value;
              counterEx.Label = ProfileDocumentMarker.FormatPerformanceCounter(counter.Value, counterInfo);
              funcEx.Counters.Add(counterEx);
            }
          }

          if (!counterColumnsAdded) {
            counterColumnsAdded = true;

            // Wait for all other columns to be added to the UI first
            // before adding the counter columns, which are relative to other
            // columns, such as the mangled name.
            Dispatcher.BeginInvoke(() => {
              AddCountersFunctionListColumns(false);
            }, DispatcherPriority.ContextIdle);
          }
        }
      }
      else {
        funcEx.ExclusiveWeight = TimeSpan.Zero;
        funcEx.Weight = TimeSpan.Zero;
      }
    }

    functionValueSorter_.SortByField(FunctionFieldKind.Optional);
    ProfileControlsVisible = true;
    ModuleControlsVisible = true;
    IsFunctionListVisible = false;
    IsFunctionListVisible = true;
    SectionCountColumnVisible = false;
    ShowSections = false;

    UseProfileCallTree = true;
    GridViewColumnVisibility.UpdateListView(FunctionList);
    Utils.ScrollToFirstListViewItem(FunctionList);
    SetupStackFunctionHoverPreview();
  }

  private void SetupStackFunctionHoverPreview() {
    if (preview_ != null) {
      preview_.Unregister();
      preview_ = null;
    }

    preview_ = new PopupHoverPreview(
      FunctionList, TimeSpan.FromMilliseconds(settings_.CallStackPopupDuration),
      (mousePoint, previewPoint) => {
        if (!settings_.ShowCallStackPopup) {
          return null;
        }

        var element = FunctionList.GetObjectAtPoint<ListViewItem>(mousePoint);

        if (element == null ||
            element.Content is not IRTextFunctionEx funcEx) {
          return null;
        }

        var nodeList = Session.ProfileData.CallTree.GetSortedCallTreeNodes(funcEx.Function);

        if (nodeList is not {Count: > 0}) {
          return null;
        }

        // Show popup for hottest call stack.
        var callNode = nodeList[0];

        if (funcBacktracePreviewPopup_ != null) {
          funcBacktracePreviewPopup_.UpdatePosition(previewPoint, FunctionList);
        }
        else {
          funcBacktracePreviewPopup_ = new CallTreeNodePopup(
            callNode, null, previewPoint,
            FunctionList, Session);
        }

        //? TODO: Max backtrace depth 10 should be a a global profiling option
        funcBacktracePreviewPopup_.ShowBackTrace(callNode, 10,
                                                 Session.CompilerInfo.NameProvider.FormatFunctionName);
        return funcBacktracePreviewPopup_;
      },
      (mousePoint, popup) => true,
      popup => {
        Session.RegisterDetachedPanel(popup);
        funcBacktracePreviewPopup_ = null;
      });
  }

  private List<IRTextFunctionEx> SetupFunctionExtensions() {
    var functionsEx = new List<IRTextFunctionEx>();
    int index = 0;

    if (otherSummary_ != null) {
      foreach (var function in summary_.Functions) {
        //? TODO: Use CreateFunctionExtensions
        var funcEx = new IRTextFunctionEx(function, index++, Session.CompilerInfo.NameProvider.FormatFunctionName);
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
          var funcEx = new IRTextFunctionEx(function, index++, Session.CompilerInfo.NameProvider.FormatFunctionName);
          functionExtMap_[function] = funcEx;
          functionsEx.Add(funcEx);
          funcEx.FunctionDiffKind = DiffKind.Insertion;
        }
      }
    }
    else {
      // Single document mode.
      CreateFunctionExtensions(summary_, functionsEx);

      foreach (var moduleSummary in moduleSummaries_) {
        CreateFunctionExtensions(moduleSummary, functionsEx);
      }
    }

    SetDemangledFunctionNames(functionsEx);
    return functionsEx;
  }

  private async Task SetupFunctionListUI(List<IRTextFunctionEx> functionsEx) {
    if (!FunctionPartVisible) {
      return;
    }

    // Set up the filter used to search the list.
    var functionFilter = new ListCollectionView(functionsEx);
    functionFilter.Filter = FilterFunctionList;
    FunctionList.ItemsSource = functionFilter;
    SectionList.ItemsSource = null;

    if (summary_.Functions.Count == 1) {
      await SelectFunction(summary_.Functions[0]);
    }
  }

  private async Task RunFunctionAnalysis(List<IRTextFunctionEx> functions) {
    if (settings_.ComputeStatistics) {
      await ComputeFunctionStatistics(functions);
    }
  }

  private void CreateFunctionExtensions(IRTextSummary summary, List<IRTextFunctionEx> functionsEx) {
    foreach (var func in summary.Functions) {
      if (!functionExtMap_.TryGetValue(func, out var funcEx)) {
        funcEx = new IRTextFunctionEx(func, functionsEx.Count, Session.CompilerInfo.NameProvider.FormatFunctionName);
        functionExtMap_[func] = funcEx;
      }
      else {
        funcEx.Index = functionsEx.Count;
      }

      functionsEx.Add(funcEx);
    }
  }

  private void ResetSectionPanel() {
    SectionCountColumnVisible = true;
    ProfileControlsVisible = false;
    ModuleControlsVisible = false;
    SectionList.ItemsSource = null;
    FunctionList.ItemsSource = null;

    otherSummary_ = null;
    currentFunction_ = null;
    moduleReport_ = null;
    sections_.Clear();
    sectionExtMap_.Clear();
    annotatedSections_.Clear();
    sectionExtensionComputed_ = false;

    SectionList.UpdateLayout();
    FunctionList.UpdateLayout();
  }

  private async Task ResetStatistics() {
    Trace.WriteLine($"Cancel stats at {DateTime.Now}, ticks {Environment.TickCount64}");

    if (statisticsTask_ != null) {
      await statisticsTask_.CancelTaskAndWaitAsync();
    }

    //Utils.WaitForDebugger(true);
    Trace.WriteLine($"Done cancel stats at {DateTime.Now}, ticks {Environment.TickCount64}");
    callGraphCache_ = null;
    functionStatMap_ = null;
  }

  private bool SetupSectionExtensions(bool force = false) {
    if (sectionExtensionComputed_ && !force) {
      return false;
    }

    sectionExtMap_.Clear();
    annotatedSections_.Clear();

    SetupSectionExtensions(summary_);

    foreach (var moduleSummary in moduleSummaries_) {
      SetupSectionExtensions(moduleSummary);
    }

    sectionExtensionComputed_ = true;
    return true;
  }

  private void SetupSectionExtensions(IRTextSummary summary) {
    foreach (var func in summary.Functions) {
      int index = 0;

      foreach (var section in func.Sections) {
        if (!sectionExtMap_.ContainsKey(section)) {
          var sectionEx = new IRTextSectionEx(section, index++);
          sectionExtMap_[section] = sectionEx;
        }
      }
    }
  }

  private void SetupSectionList(IRTextFunction function, bool force = false) {
    if (function != null && function.Equals(currentFunction_) && !force) {
      return;
    }

    currentFunction_ = function;
    FunctionList.SelectedItem = function;
    Sections = CreateSectionsExtension();
    FunctionSwitched?.Invoke(this, currentFunction_);
  }

  private void ApplySectionBorder(IRTextSectionEx sectionEx, int sectionIndex,
                                  MarkedSectionName markedName,
                                  List<IRTextSectionEx> sections) {
    // Don't show the border for the first and last sections in the list,
    // and if there is a before-border following an after-border.
    bool useBeforeBorder = sectionIndex > 0 && !sections[sectionIndex - 1].HasAfterBorder;
    bool useAfterBorder = sectionIndex < currentFunction_.Sections.Count - 1;

    sectionEx.BorderThickness = new Thickness(0, useBeforeBorder ? markedName.BeforeSeparatorWeight : 0,
                                              0, useAfterBorder ? markedName.AfterSeparatorWeight : 0);
    sectionEx.BorderBrush = ColorBrushes.GetBrush(markedName.SeparatorColor);
  }

  private void ApplySectionNameIndentation(IRTextSectionEx sectionEx, MarkedSectionName markedName) {
    int level = markedName.IndentationLevel;
    var builder = new StringBuilder(sectionEx.Name.Length + level * settings_.IndentationAmount);

    while (level > 0) {
      builder.Append(' ', settings_.IndentationAmount);
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

    if (activeModuleFilter_ != null) {
      if (functionEx.ModuleName != activeModuleFilter_.Name) {
        return false;
      }
    }

    // Don't filter with less than 2 letters.
    string text = FunctionFilter.Text.Trim();

    if (text.Length < 2) {
      return true;
    }

    // Search the function name.
    if (App.Settings.SectionSettings.FunctionSearchCaseSensitive
      ? function.Name.Contains(text, StringComparison.Ordinal)
      : function.Name.Contains(text, StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    // Search the demangled name.
    if (!string.IsNullOrEmpty(functionEx.AlternateName)) {
      return App.Settings.SectionSettings.FunctionSearchCaseSensitive
        ? functionEx.AlternateName.Contains(text, StringComparison.Ordinal)
        : functionEx.AlternateName.Contains(text, StringComparison.OrdinalIgnoreCase);
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
           (App.Settings.SectionSettings.SectionSearchCaseSensitive
             ? section.Name.Contains(text, StringComparison.Ordinal)
             : section.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
  }

  private void ExecuteClearTextbox(object sender, ExecutedRoutedEventArgs e) {
    ((TextBox)e.Parameter).Text = string.Empty;
  }

  private void OpenExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (SectionList.SelectedItems.Count == 2) {
      OpenSideBySideExecuted(sender, e);
      return;
    }

    if (GetCommandTargetSection(e) is { } section) {
      OpenSectionImpl(section, OpenSectionKind.ReplaceCurrent);
    }
  }

  private void OpenInNewTabExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (SectionList.SelectedItems.Count == 2) {
      DiffSideBySideExecuted(sender, e);
      return;
    }

    if (GetCommandTargetSection(e) is { } section) {
      OpenSectionImpl(section, OpenSectionKind.NewTab);
    }
  }

  private void OpenLeftExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GetCommandTargetSection(e) is { } section) {
      OpenSectionImpl(section, OpenSectionKind.NewTabDockLeft);
    }
  }

  private void OpenRightExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (GetCommandTargetSection(e) is { } section) {
      OpenSectionImpl(section, OpenSectionKind.NewTabDockRight);
    }
  }

  private IRTextSectionEx GetCommandTargetSection(ExecutedRoutedEventArgs e) {
    var section = e.Parameter as IRTextSectionEx;

    if (e.Parameter is IRTextFunctionEx functionEx) {
      section = GetSectionExtension(functionEx.Function.Sections[0]);
    }

    return section;
  }

  private void OpenSideBySideExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (SectionList.SelectedItems.Count == 2) {
      var leftSection = SectionList.SelectedItems[0] as IRTextSectionEx;
      var rightSection = SectionList.SelectedItems[1] as IRTextSectionEx;
      OpenSectionImpl(leftSection, OpenSectionKind.NewTabDockLeft);
      OpenSectionImpl(rightSection, OpenSectionKind.NewTabDockRight);
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
    DiffWithOtherSection(sectionEx);
  }

  private void DiffWithOtherSection(IRTextSectionEx sectionEx) {
    var args = new DiffModeEventArgs
      {IsWithOtherDocument = true, Left = new OpenSectionEventArgs(sectionEx.Section, OpenSectionKind.NewTabDockLeft)};
    EnterDiffMode?.Invoke(this, args);
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

  private async void FunctionList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    if (e.AddedItems.Count == 1) {
      await SelectFunction(((IRTextFunctionEx)FunctionList.SelectedItem).Function);
    }
  }

  private (TimeSpan Weight, TimeSpan ExclusiveWeight) ComputeSelectedFunctionsWeight() {
    var weight = TimeSpan.Zero;
    var exclusiveWeight = TimeSpan.Zero;

    foreach (var functionEx in FunctionList.SelectedItems.Cast<IRTextFunctionEx>()) {
      weight += functionEx.Weight;
      exclusiveWeight += functionEx.ExclusiveWeight;
    }

    return (weight, exclusiveWeight);
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
    var sectionEx = ((ListViewItem)sender).Content as IRTextSectionEx;
    OpenSectionImpl(sectionEx);
  }

  private void OpenSectionImpl(IRTextSection section) {
    OpenSectionImpl(GetSectionExtension(section));
  }

  private void OpenSectionImpl(IRTextSectionEx sectionEx) {
    if (Session.IsInTwoDocumentsDiffMode) {
      DiffWithOtherSection(sectionEx);
    }
    else {
      bool inNewTab = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ||
                      (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
      OpenSectionImpl(sectionEx, inNewTab ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent);
    }
  }

  private void OpenSectionImpl(IRTextSectionEx value, OpenSectionKind kind,
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

  private async Task ComputeConsecutiveSectionDiffs() {
    if (!settings_.MarkSectionsIdenticalToPrevious || sections_.Count < 2) {
      return;
    }

    // Make a list of all section pairs like [i - 1, i] and diff each one.
    // Note that when comparing two documents side-by-side, some of the sections
    // may be placeholders that don't have a real section behind, those must be ignored.
    var comparedSections = new List<(IRTextSection, IRTextSection)>();
    int prevIndex = -1;

    for (int i = 0; i < sections_.Count; i++) {
      if (sections_[i].Section == null) {
        continue;
      }

      if (prevIndex != -1) {
        comparedSections.Add((sections_[prevIndex].Section, sections_[i].Section));
      }

      prevIndex = i;
    }

    //? TODO: Pass the LoadedDocument to the panel, not Summary.
    var loader = Session.SessionState.FindLoadedDocument(Summary).Loader;
    var diffBuilder = new DocumentDiffBuilder(App.Settings.DiffSettings);
    var cancelableTask = new CancelableTask();
    var results = await diffBuilder.AreSectionsDifferent(comparedSections, loader, loader,
                                                         Session.CompilerInfo, true, cancelableTask);

    foreach (var result in results) {
      if (result.HasDiffs) {
        var diffSection = GetSectionExtension(result.RightSection);
        diffSection.IsDiffFromPrevious = true;
      }
    }
  }

  private async void FixedToolbar_SettingsClicked(object sender, EventArgs e) {
    if (optionsPanelVisible_) {
      await CloseOptionsPanel();
    }
    else {
      ShowOptionsPanel();
    }
  }

  private void ShowOptionsPanel() {
    if (optionsPanelVisible_) {
      return;
    }

    double width = Math.Max(SectionOptionsPanel.MinimumWidth,
                            Math.Min(SectionList.ActualWidth, SectionOptionsPanel.DefaultWidth));
    double height = Math.Max(SectionOptionsPanel.MinimumHeight,
                             Math.Min(SectionList.ActualHeight, SectionOptionsPanel.DefaultHeight));
    var position = new Point(SectionList.ActualWidth - width, 0);
    optionsPanelWindow_ = new OptionsPanelHostWindow(new SectionOptionsPanel(),
                                                     position, width, height, SectionList,
                                                     settings_.Clone(), Session);
    optionsPanelWindow_.PanelClosed += OptionsPanel_PanelClosed;
    optionsPanelWindow_.PanelReset += OptionsPanel_PanelReset;
    optionsPanelWindow_.SettingsChanged += OptionsPanel_SettingsChanged;
    optionsPanelWindow_.IsOpen = true;
    optionsPanelVisible_ = true;
  }

  private async void OptionsPanel_SettingsChanged(object sender, EventArgs e) {
    var newSettings = (SectionSettings)optionsPanelWindow_.Settings;
    await HandleNewSettings(newSettings, false);
    optionsPanelWindow_.Settings = settings_.Clone();
  }

  private async void OptionsPanel_PanelReset(object sender, EventArgs e) {
    await HandleNewSettings(new SectionSettings(), true);
    optionsPanelWindow_.Settings = settings_.Clone();
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

  public override Task OnReloadSettings() {
    return HandleNewSettings(App.Settings.SectionSettings, false, true);
  }

  private async Task HandleNewSettings(SectionSettings newSettings, bool commit, bool force = false) {
    if (commit) {
      App.Settings.SectionSettings = newSettings;
      App.SaveApplicationSettings();
    }

    if (force || !newSettings.Equals(settings_)) {
      bool updateFunctionList = newSettings.HasFunctionListChanges(settings_);
      App.Settings.SectionSettings = newSettings;
      settings_ = newSettings;

      if (updateFunctionList) {
        await SetupFunctionList();
      }

      SetupSectionList(currentFunction_, true);
      RefreshSectionList();
      await ComputeConsecutiveSectionDiffs();

      if(ProfileControlsVisible) {
        SetupStackFunctionHoverPreview();
        OnPropertyChanged(nameof(SyncSelection));
        OnPropertyChanged(nameof(SyncSourceFile));
        OnPropertyChanged(nameof(ShowModules));
      }
    }
  }

  private async void CopySectionTextExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (e.Parameter is IRTextSectionEx sectionEx) {
      string text = await Session.GetSectionTextAsync(sectionEx.Section);
      Clipboard.SetText(text);
    }
  }

  private async void SaveSectionTextExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (!(e.Parameter is IRTextSectionEx sectionEx)) {
      return;
    }

    string path = Utils.ShowSaveFileDialog("IR text|*.txt", "*.txt|All Files|*.*");

    if (!string.IsNullOrEmpty(path)) {
      try {
        string text = await Session.GetSectionTextAsync(sectionEx.Section);
        await File.WriteAllTextAsync(path, text);
      }
      catch (Exception ex) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show($"Failed to save IR text file {path}: {ex.Message}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  private async void SaveFunctionTextExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (!(e.Parameter is IRTextFunctionEx functionEx)) {
      return;
    }

    string path = Utils.ShowSaveFileDialog("IR text|*.txt", "*.txt|All Files|*.*");

    if (!string.IsNullOrEmpty(path)) {
      try {
        await using var writer = new StreamWriter(path);
        await CombineFunctionText(functionEx.Function, writer);
      }
      catch (Exception ex) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show($"Failed to save IR text file {path}: {ex.Message}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  private async Task CombineFunctionText(IRTextFunction function, TextWriter writer) {
    foreach (var section in function.Sections) {
      string text = await Session.GetSectionTextAsync(section);

      if (section.OutputBefore != null) {
        string beforeText = await Session.GetSectionOutputTextAsync(section.OutputBefore, section);
        await writer.WriteLineAsync(beforeText);
      }

      await writer.WriteLineAsync(text);
    }
  }

  private void CopyFunctionNameExecuted(object sender, ExecutedRoutedEventArgs e) {
    string text = "";

    foreach (IRTextFunctionEx func in FunctionList.SelectedItems) {
      if (!string.IsNullOrEmpty(text)) {
        text += Environment.NewLine;
      }

      text += Session.CompilerInfo.NameProvider.GetFunctionName(func.Function);
    }

    Clipboard.SetText(text);
  }

  private void CopyDemangledFunctionNameExecuted(object sender, ExecutedRoutedEventArgs e) {
    string text = "";

    foreach (IRTextFunctionEx func in FunctionList.SelectedItems) {
      if (!string.IsNullOrEmpty(text)) {
        text += Environment.NewLine;
      }

      text += Session.CompilerInfo.NameProvider.FormatFunctionName(func.Function);
    }

    Clipboard.SetText(text);
  }

  private void DisplayCallGraphExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (e.Parameter is IRTextSectionEx sectionEx) {
      DisplayCallGraph?.Invoke(this, new DisplayCallGraphEventArgs(Summary, sectionEx.Section, false));
    }
  }

  private IRTextFunction GetSelectedFunction(ExecutedRoutedEventArgs e) {
    if (e.Parameter is IRTextFunctionEx funcEx) {
      return funcEx.Function;
    }

    if (e.Parameter is IRTextSectionEx sectionEx) {
      return sectionEx.Section.ParentFunction;
    }

    return null;
  }

  private void DisplayPartialCallGraphExecuted(object sender, ExecutedRoutedEventArgs e) {
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
    double reservedWidth = 20;

    if (profileControlsVisible_) {
      reservedWidth += 120;
    }

    if (moduleControlsVisible_) {
      reservedWidth += 80;
    }

    FunctionFilterGrid.Width = Math.Max(1, width - reservedWidth);
  }

  private async void FunctionDoubleClick(object sender, MouseButtonEventArgs e) {
    await OpenFunction(sender);
  }

  private async Task OpenFunction(object sender) {
    // A double-click on the +/- icon doesn't have an actual node selected.
    var functionEx = ((ListViewItem)sender).Content as IRTextFunctionEx;

    if (functionEx != null) {
      await SelectFunction(functionEx.Function);

      if (functionEx.SectionCount > 0) {
        OpenSectionImpl(functionEx.Function.Sections[0]);
      }
    }
  }

  private async Task ComputeFunctionStatistics(List<IRTextFunctionEx> functions) {
    Trace.TraceInformation("ComputeFunctionStatistics: start");
    using var cancelableTask = await statisticsTask_.CancelPreviousAndCreateTaskAsync();

    if (functionStatMap_ == null || functionStatMap_.Count < functions.Count) {
      functionStatMap_ = await ComputeFunctionStatisticsImpl(functions, cancelableTask);

      if (cancelableTask.IsCanceled) {
        return;
      }
    }

    foreach (var pair in functionStatMap_) {
      var functionEx = GetFunctionExtension(pair.Key);
      functionEx.Statistics = pair.Value;
    }

    Trace.TraceInformation("ComputeFunctionStatistics: done");
    statisticsTask_.CompleteTask();

    AddStatisticsFunctionListColumns(addDiffColumn: false);
    RefreshFunctionList();
    Session.SetApplicationProgress(false, double.NaN);
  }

  private async Task<ConcurrentDictionary<IRTextFunction, FunctionCodeStatistics>>
    ComputeFunctionStatisticsImpl(List<IRTextFunctionEx> functions, CancelableTask cancelableTask) {
    var functionStatMap = new ConcurrentDictionary<IRTextFunction, FunctionCodeStatistics>();

    if (functions.Count == 0) {
      return functionStatMap;
    }

    var tasks = new List<Task>();
    var taskScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 16);
    var taskFactory = new TaskFactory(taskScheduler.ConcurrentScheduler);

    Session.SetApplicationProgress(true, double.NaN, "Computing statistics");

    foreach (var function in functions) {
      if (function.SectionCount == 0) {
        continue;
      }

      if (cancelableTask.IsCanceled) {
        break;
      }

      tasks.Add(taskFactory.StartNew(() => {
        try {
          if (cancelableTask.IsCanceled) {
            return;
          }

          var section = function.Function.Sections[0];
          var summary = function.Function.ParentSummary;
          CallGraph callGraph = null;

          if (settings_.IncludeCallGraphStatistics) {
            callGraph = GenerateCallGraph(summary).Result;
          }

          var loadedDoc = Session.SessionState.FindLoadedDocument(summary);
          var sectionStats = ComputeFunctionStatistics(section, loadedDoc.Loader, callGraph);

          if (sectionStats != null) {
            functionStatMap.TryAdd(function.Function, sectionStats);
          }
        }
        catch (Exception ex) {
          Trace.WriteLine(ex.ToString());
          Trace.WriteLine($"Exception stats at {DateTime.Now}, ticks {Environment.TickCount64}");
        }
      }, cancelableTask.Token));
    }

    await Task.WhenAll(tasks.ToArray());

    if (cancelableTask.IsCanceled) {
      // Complete only now after all tasks were canceled,
      // otherwise the session ending would move on and break the tasks still executing.
      cancelableTask.Completed();
    }

    Session.SetApplicationProgress(false, double.NaN);
    return functionStatMap;
  }

  private FunctionCodeStatistics ComputeFunctionStatistics(IRTextSection section, IRTextSectionLoader loader,
                                                           CallGraph callGraph) {
    var result = loader.LoadSection(section);

    if (result == null) {
      return null;
    }

    var stats = FunctionCodeStatistics.Compute(result.Function, Session.CompilerInfo.IR);

    if (callGraph != null) {
      var node = callGraph.FindNode(section.ParentFunction);

      if (node != null) {
        stats.Callees = node.UniqueCalleeCount;
        stats.Callers = node.UniqueCallerCount;
      }
    }

    return stats;
  }

  private void AutoResizeColumns(ListView listView, int skipCount) {
    int index = 0;

    foreach (var column in ((GridView)listView.View).Columns) {
      if (index >= skipCount) {
        column.Width = 0;
        column.Width = double.NaN;
      }

      index++;
    }
  }

  private void ModuleDoubleClick(object sender, MouseButtonEventArgs e) {
    SwitchModule(sender);
  }

  private void SwitchModule(object sender) {
    var moduleEx = ((ListViewItem)sender).Content as ModuleEx;

    if (moduleEx != null) {
      ApplyModuleFilter(moduleEx);
    }
  }

  private void ApplyModuleFilter(ModuleEx moduleEx) {
    if (moduleEx.Name == "All") {
      activeModuleFilter_ = null;
    }
    else {
      activeModuleFilter_ = moduleEx;
    }

    RefreshFunctionList();
  }

  private void ExportFunctionListExecuted(object sender, ExecutedRoutedEventArgs e) {
    string path = Utils.ShowSaveFileDialog("Excel Worksheets|*.xlsx", "*.xlsx|All Files|*.*");

    if (!string.IsNullOrEmpty(path)) {
      try {
        ExportFunctionListAsExcelFile(path);
      }
      catch (Exception ex) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show($"Failed to save function list to {path}: {ex.Message}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  private string ExportFunctionListAsMarkdown(List<IRTextFunctionEx> list) {
    var sb = new StringBuilder();
    string header    = "| Function | Module |";
    string separator = "|----------|--------|";

    if (profileControlsVisible_) {
      header    += " Time (ms) | Time (%) | Time incl (ms) | Time incl (%) |";
      separator += "-----------|----------|----------------|---------------|";
    }

    if (alternateNameColumnVisible_) {
      header    += " Mangled name |";
      separator += "--------------|";
    }

    sb.AppendLine(header);
    sb.AppendLine(separator);

    foreach (var func in list) {
      if (profileControlsVisible_) {
        sb.Append($"| {func.Name} | {func.ModuleName} " +
                    $"| {func.ExclusiveWeight.TotalMilliseconds} " +
                    $"| {func.ExclusivePercentage.AsPercentageString(2, false)} " +
                    $"| {func.Weight.TotalMilliseconds} " +
                    $"| {func.Percentage.AsPercentageString(2, false)} |");
      }
      else {
        sb.Append($"| {func.Name} | {func.ModuleName} |");
      }

      if (alternateNameColumnVisible_) {
        sb.AppendLine($" {func.AlternateName} |");
      }
      else {
        sb.AppendLine();
      }
    }

    return sb.ToString();
  }

  private HtmlNode ExportFunctionListAsHtml(List<IRTextFunctionEx> list) {
    string TableStyle = @"border-collapse:collapse;border-spacing:0;";
    string HeaderStyle = @"background-color:#D3D3D3;white-space:nowrap;text-align:left;vertical-align:top;border-color:black;border-style:solid;border-width:1px;overflow:hidden;padding:2px 2px;font-family:Arial, sans-serif;";
    string CellStyle = @"text-align:left;vertical-align:top;word-wrap:break-word;max-width:300px;overflow:hidden;padding:2px 2px;border-color:black;border-style:solid;border-width:1px;font-family:Arial, sans-serif;";

    var doc = new HtmlDocument();
    var table = doc.CreateElement("table");
    table.SetAttributeValue("style", TableStyle);

    var thead = doc.CreateElement("thead");
    var tbody = doc.CreateElement("tbody");
    var tr = doc.CreateElement("tr");

    var th = doc.CreateElement("th");
    th.InnerHtml = "Function";
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);
    th = doc.CreateElement("th");
    th.InnerHtml = "Module";
    th.SetAttributeValue("style", HeaderStyle);
    tr.AppendChild(th);
    thead.AppendChild(tr);

    if (profileControlsVisible_) {
      th = doc.CreateElement("th");
      th.InnerHtml = HttpUtility.HtmlEncode("Time (ms)");
      th.SetAttributeValue("style", HeaderStyle);
      tr.AppendChild(th);

      th = doc.CreateElement("th");
      th.InnerHtml = HttpUtility.HtmlEncode("Time (%)");
      th.SetAttributeValue("style", HeaderStyle);
      tr.AppendChild(th);

      th = doc.CreateElement("th");
      th.InnerHtml = HttpUtility.HtmlEncode("Tine incl (ms)");
      th.SetAttributeValue("style", HeaderStyle);
      tr.AppendChild(th);

      th = doc.CreateElement("th");
      th.InnerHtml = HttpUtility.HtmlEncode("Time incl (%)");
      th.SetAttributeValue("style", HeaderStyle);
      tr.AppendChild(th);
    }

    if (alternateNameColumnVisible_) {
      th = doc.CreateElement("th");
      th.InnerHtml = HttpUtility.HtmlEncode("Mangled name");
      th.SetAttributeValue("style", HeaderStyle);
      tr.AppendChild(th);
    }

    table.AppendChild(thead);

    foreach (IRTextFunctionEx func in list) {
      tr = doc.CreateElement("tr");
      var td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode(func.Name);
      td.SetAttributeValue("style", CellStyle);
      tr.AppendChild(td);
      td = doc.CreateElement("td");
      td.InnerHtml = HttpUtility.HtmlEncode(func.ModuleName);
      td.SetAttributeValue("style", CellStyle);
      tr.AppendChild(td);

      if (profileControlsVisible_) {
        var backColor = Utils.BrushToString(func.BackColor);
        var colorAttr = backColor != null ? $";background-color:{backColor}" : "";

        td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode($"{func.ExclusiveWeight.TotalMilliseconds}");
        td.SetAttributeValue("style", $"{CellStyle}{colorAttr}");
        tr.AppendChild(td);
        td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode($"{func.ExclusivePercentage.AsPercentageString(2, false)}");
        td.SetAttributeValue("style", $"{CellStyle}{colorAttr}");
        tr.AppendChild(td);
        td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode($"{func.Weight.TotalMilliseconds}");
        td.SetAttributeValue("style", $"{CellStyle}{colorAttr}");
        tr.AppendChild(td);
        td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode($"{func.Percentage.AsPercentageString(2, false)}");
        td.SetAttributeValue("style", $"{CellStyle}{colorAttr}");
        tr.AppendChild(td);
      }

      if (alternateNameColumnVisible_) {
        td = doc.CreateElement("td");
        td.InnerHtml = HttpUtility.HtmlEncode(func.AlternateName);
        td.SetAttributeValue("style", CellStyle);
        tr.AppendChild(td);
      }

      tbody.AppendChild(tr);
    }

    table.AppendChild(tbody);
    doc.DocumentNode.AppendChild(table);
    return doc.DocumentNode;
  }

  private void CopyFunctionListAsHtml() {
    var doc = new HtmlDocument();
    var funcList = new List<IRTextFunctionEx>();

    foreach (var item in FunctionList.SelectedItems) {
      funcList.Add((IRTextFunctionEx)item);
    }

    doc.DocumentNode.AppendChild(ExportFunctionListAsHtml(funcList));
    var writer = new StringWriter();
    doc.Save(writer);

    // Also save as Markdown so that it can be pasted in plain text editors.
    var plainText = ExportFunctionListAsMarkdown(funcList);
    Utils.CopyHtmlToClipboard(writer.ToString(), plainText);
  }

  private bool ExportFunctionListAsHtmlFile(string filePath) {
    try {
      var funcList = (ListCollectionView)FunctionList.ItemsSource;
      var doc = new HtmlDocument();
      doc.DocumentNode.AppendChild(ExportFunctionListAsHtml(funcList.ToList<IRTextFunctionEx>()));
      var writer = new StringWriter();
      doc.Save(writer);
      File.WriteAllText(filePath, writer.ToString());
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to export to HTML file: {filePath}, {ex.Message}");
      return false;
    }
  }

  private bool ExportFunctionListAsMarkdownFile(string filePath) {
    try {
      var funcList = (ListCollectionView)FunctionList.ItemsSource;
      var text = ExportFunctionListAsMarkdown(funcList.ToList<IRTextFunctionEx>());
      File.WriteAllText(filePath, text);
      return true;
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to export to Markdown file: {filePath}, {ex.Message}");
      return false;
    }
  }

  private void ExportFunctionListAsExcelFile(string filePath) {
    var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Functions");
    int rowId = 1; // First row is for the table column names.
    int maxLineLength = 0;
    var funcList = (ListCollectionView)FunctionList.ItemsSource;

    foreach (IRTextFunctionEx func in funcList) {
      rowId++;
      ws.Cell(rowId, 1).Value = func.Name;
      ws.Cell(rowId, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
      maxLineLength = Math.Max(func.Name.Length, maxLineLength);
      ws.Cell(rowId, 2).Value = func.ModuleName;

      if (profileControlsVisible_) {
        int columnId = 3;
        ws.Cell(rowId, columnId + 0).Value = $"{func.ExclusiveWeight.TotalMilliseconds}";
        ws.Cell(rowId, columnId + 1).Value = $"{func.ExclusivePercentage.AsPercentageString(2, false, "")}";
        ws.Cell(rowId, columnId + 2).Value = $"{func.Weight.TotalMilliseconds}";
        ws.Cell(rowId, columnId + 3).Value = $"{func.Percentage.AsPercentageString(2, false, "")}";

        if (func.BackColor != null && func.BackColor is SolidColorBrush colorBrush) {
          var color = XLColor.FromArgb(colorBrush.Color.A, colorBrush.Color.R, colorBrush.Color.G, colorBrush.Color.B);
          ws.Cell(rowId, 1).Style.Fill.BackgroundColor = color;
          ws.Cell(rowId, 2).Style.Fill.BackgroundColor = color;
          ws.Cell(rowId, columnId + 0).Style.Fill.BackgroundColor = color;
          ws.Cell(rowId, columnId + 1).Style.Fill.BackgroundColor = color;
          ws.Cell(rowId, columnId + 2).Style.Fill.BackgroundColor = color;
          ws.Cell(rowId, columnId + 3).Style.Fill.BackgroundColor = color;
        }

        if (alternateNameColumnVisible_) {
          ws.Cell(rowId, columnId + 4).Value = func.AlternateName;
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
        cell.Value = "Function";
      }
      else if (cell.Address.ColumnNumber == 2) {
        cell.Value = "Module";
      }
      else if (profileControlsVisible_ && cell.Address.ColumnNumber - 3 <= 3) {
        switch (cell.Address.ColumnNumber - 3) {
          case 0: {
            cell.Value = "Time (ms)";
            break;
          }
          case 1: {
            cell.Value = "Time (%)";
            break;
          }
          case 2: {
            cell.Value = "Time incl (ms)";
            break;
          }
          case 3: {
            cell.Value = "Time incl (%)";
            break;
          }
        }
      }
      else if (alternateNameColumnVisible_) {
        cell.Value = "Mangled name";
      }

      cell.Style.Font.Bold = true;
      cell.Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    for (int i = 1; i <= 1; i++) {
      ws.Column(i).AdjustToContents((double)1, Math.Min(50, maxLineLength));
    }

    for (int i = 2; i <= lastCell.Address.ColumnNumber; i++) {
      ws.Column(i).AdjustToContents();
    }

    wb.SaveAs(filePath);
  }

  private void OpenDocumentInNewInstanceExecuted(object sender, ExecutedRoutedEventArgs e) {
    var loadedDoc = Session.SessionState.FindLoadedDocument(Summary);

    if (!App.StartNewApplicationInstance(loadedDoc.FilePath)) {
      MessageBox.Show("Failed to start new application instance", "IR Explorer", MessageBoxButton.OK,
                      MessageBoxImage.Error);
    }
  }

  private void OpenDocumentInEditorExecuted(object sender, ExecutedRoutedEventArgs e) {
    var loadedDoc = Session.SessionState.FindLoadedDocument(Summary);

    if (!Utils.OpenExternalFile(loadedDoc.FilePath)) {
      MessageBox.Show("Failed to open document", "IR Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private async void CopyFunctionTextExecuted(object sender, ExecutedRoutedEventArgs e) {
    if (!(e.Parameter is IRTextFunctionEx functionEx)) {
      return;
    }

    using var writer = new StringWriter();
    await CombineFunctionText(functionEx.Function, writer);
    Clipboard.SetText(writer.ToString());
  }

  private void ModuleWarningImage_MouseUp(object sender, MouseButtonEventArgs e) {
    var moduleEx = ModulesList.SelectedItem as ModuleEx;
    ProfileReportPanel.ShowReportWindow(Session.ProfileData.Report, Session, moduleEx?.Status);
  }

  private void FunctionClick(object sender, MouseButtonEventArgs e) {
    if (FunctionList.SelectedItems.Count > 1) {
      var (weight, exclusiveWeight) = ComputeSelectedFunctionsWeight();
      double weightPercentage = Session.ProfileData.ScaleFunctionWeight(weight);
      double exclusiveWeightPercentage = Session.ProfileData.ScaleFunctionWeight(exclusiveWeight);
      string text = $"{weightPercentage.AsPercentageString()} ({weight.AsMillisecondsString()})";
      text += $", excl {exclusiveWeightPercentage.AsPercentageString()} ({exclusiveWeight.AsMillisecondsString()})";
      Session.SetApplicationStatus(text, "Sum of selected functions");
    }
    else {
      Session.SetApplicationStatus("");
    }
  }

  private sealed class FunctionDiffKindConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
      return null;
    }
  }

  private sealed class FunctionDiffValueConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
      if (value is int intValue) {
        if (intValue > 0) {
          return $"+{intValue}";
        }

        if (intValue == 0) {
          return $" {intValue}";
        }
      }
      else if (value is long longValue) {
        if (longValue > 0) {
          return $"+{longValue}";
        }

        if (longValue == 0) {
          return $" {longValue}";
        }
      }

      return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
      return null;
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

  public SectionSettings Settings {
    get => settings_;
    set => settings_ = value;
  }

  public IRTextSectionEx GetSectionExtension(IRTextSection section) {
    return sectionExtMap_[section];
  }

  public IRTextFunctionEx GetFunctionExtension(IRTextFunction function) {
    if (functionExtMap_.TryGetValue(function, out var functionEx)) {
      return functionEx;
    }

    if (IsDiffModeEnabled && function.ParentSummary == otherSummary_) {
      //? TODO: Add name mapping using dictionary
      foreach (var pair in functionExtMap_) {
        if (pair.Key.Name == function.Name) {
          return pair.Value;
        }
      }
    }

    Utils.WaitForDebugger();
    throw new InvalidOperationException();
  }

  public override async void OnSessionStart() {
    base.OnSessionStart();
    statisticsTask_ = new CancelableTaskInstance(false, Session.SessionState.RegisterCancelableTask,
                                                 Session.SessionState.UnregisterCancelableTask);
    callGraphTask_ = new CancelableTaskInstance(false, Session.SessionState.RegisterCancelableTask,
                                                Session.SessionState.UnregisterCancelableTask);
    object data = Session.LoadPanelState(this, null);

    if (data != null) {
      var state = StateSerializer.Deserialize<SectionPanelState>(data);

      foreach (int sectionId in state.AnnotatedSections) {
        var section = summary_.GetSectionWithId(sectionId);
        var sectionExt = GetSectionExtension(section);
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

  public async Task SelectFunction(IRTextFunction function, bool handleProfiling = true) {
    if (function.Equals(currentFunction_)) {
      return;
    }

    SetupSectionList(function);
    var funcEx = GetFunctionExtension(function);
    FunctionList.SelectedItem = funcEx;
    FunctionList.ScrollIntoView(FunctionList.SelectedItem);
    RefreshSectionList();

    if (handleProfiling && profileControlsVisible_ && Session.ProfileData != null) {
      var funcProfile = Session.ProfileData.GetFunctionProfile(function);

      if (funcProfile != null) {
        if (SyncSelection) {
          await Session.ProfileFunctionSelected(function, ToolPanelKind.Section);
        }

        if (SyncSourceFile) {
          await Session.OpenProfileSourceFile(function);
        }
      }
      else {
        await Session.ProfileFunctionDeselected();
      }
    }

    await ComputeConsecutiveSectionDiffs();
  }

  private async Task<(CallGraph, CallGraphNode)> GenerateFunctionCallGraph(
    IRTextSummary summary, IRTextFunction function) {
    var callGraph = await GenerateCallGraph(summary);
    var callGraphNode = callGraph.FindNode(function);
    return (callGraph, callGraphNode);
  }

  private async Task<CallGraph> GenerateCallGraph(IRTextSummary summary) {
    callGraphCache_ ??= new Dictionary<IRTextSummary, CallGraph>();

    if (callGraphCache_.TryGetValue(summary, out var callGraph)) {
      return callGraph;
    }

    Session.SetApplicationProgress(true, double.NaN, "Generating call graph");
    using var cancelableTask = await callGraphTask_.CancelPreviousAndCreateTaskAsync();

    var loadedDoc = Session.SessionState.FindLoadedDocument(summary);
    callGraph = new CallGraph(summary, loadedDoc.Loader, Session.CompilerInfo.IR);
    await Task.Run(() => callGraph.Execute(null, cancelableTask));

    if (!cancelableTask.IsCanceled) {
      // Cache the call graph, can be expensive to compute.
      callGraphCache_[summary] = callGraph;
    }

    callGraphTask_.CompleteTask(cancelableTask);
    Session.SetApplicationProgress(false, 0);
    return callGraph;
  }

#region Dead code for making a call tree from CallGraph, not profile info

  //private async Task<ChildFunctionEx> CreateCallTree(IRTextFunction function) {
  //    var (_, callGraphNode) = await GenerateFunctionCallGraph(function.ParentSummary, function);
  //    CallGraphNode otherNode = null;

  //    if (otherSummary_ != null) {
  //        var otherFunction = otherSummary_.GetOrCreateFunction(function);

  //        if (otherFunction != null) {
  //            (_, otherNode) = await GenerateFunctionCallGraph(otherSummary_, otherFunction);
  //        }
  //    }

  //    var visitedNodes = new HashSet<CallGraphNode>();
  //    var rootNode = new ChildFunctionEx(ChildFunctionExKind.CallTreeNode);
  //    rootNode.Children = new List<ChildFunctionEx>();

  //    if (callGraphNode != null) {
  //        CreateCallTree(callGraphNode, otherNode, function, rootNode, visitedNodes);
  //    }

  //    return rootNode;
  //}

  //private void CreateCallTree(CallGraphNode node, CallGraphNode otherNode,
  //    IRTextFunction function, ChildFunctionEx parentNode,
  //    HashSet<CallGraphNode> visitedNodes) {
  //    visitedNodes.Add(node);

  //    if (node.HasCallers) {
  //        //? TODO: Sort on top of the list
  //        var callerInfo = CreateCallTreeChild(node, function);
  //        callerInfo.FunctionName = "Callers";
  //        callerInfo.IsMarked = true;
  //        parentNode.Children.Add(callerInfo);

  //        foreach (var callerNode in node.UniqueCallers) {
  //            var callerFunc = function.ParentSummary.GetOrCreateFunction(callerNode.FunctionName);
  //            if (callerFunc == null)
  //                continue;

  //            var callerNodeEx = CreateCallTreeChild(callerNode, callerFunc);
  //            callerInfo.Children.Add(callerNodeEx);
  //        }
  //    }

  //    foreach (var calleeNode in node.UniqueCallees) {
  //        var childFunc = function.ParentSummary.GetOrCreateFunction(calleeNode.FunctionName);
  //        if (childFunc == null) continue;

  //        // Create node and attach statistics if available.
  //        var childNode = CreateCallTreeChild(calleeNode, childFunc);
  //        parentNode.Children.Add(childNode);

  //        var otherCalleeNode = otherNode?.FindCallee(calleeNode);

  //        if (otherSummary_ != null && otherCalleeNode == null) {
  //            // Missing in right.
  //            childNode.TextColor = ColorBrushes.GetBrush(settings_.NewSectionColor);
  //            childNode.IsMarked = true;
  //        }

  //        if (calleeNode.HasCallees && !visitedNodes.Contains(calleeNode)) {
  //            childNode.Children = new List<ChildFunctionEx>();
  //            CreateCallTree(calleeNode, otherCalleeNode, childFunc, childNode, visitedNodes);

  //            if (childNode.IsMarked && parentNode != null) {
  //                parentNode.IsMarked = true;
  //            }
  //        }
  //    }

  //    // Sort children, since that is not yet supported by the TreeListView control.
  //    parentNode.Children.Sort((a, b) => {
  //        // Ensure the callers node is placed first.
  //        if (b.Time > a.Time) {
  //            return 1;
  //        }
  //        else if (b.Time < a.Time) {
  //            return -1;
  //        }

  //        return string.Compare(b.FunctionName, a.FunctionName, StringComparison.Ordinal);
  //    });
  //}

  //private FunctionCodeStatistics GetFunctionStatistics(IRTextFunction function) {
  //    var functionEx = GetFunctionExtension(function);
  //    return functionEx.Statistics;
  //}

  //private ChildFunctionEx CreateCallTreeChild(CallGraphNode childNode, IRTextFunction childFunc) {
  //    var childInfo = new ChildFunctionEx(ChildFunctionExKind.CallTreeNode) {
  //        Function = childFunc,
  //        FunctionName = childNode.FunctionName,
  //        TextColor = Brushes.Black,
  //        BackColor = ColorBrushes.GetBrush(Colors.Transparent),
  //        Children = new List<ChildFunctionEx>(),
  //    };
  //    return childInfo;
  //}

#endregion

  public override void OnSessionSave() {
    base.OnSessionSave();
    var state = new SectionPanelState();
    state.AnnotatedSections = annotatedSections_.ToList(item => item.Section.Id);

    state.SelectedFunctionNumber = FunctionList.SelectedItem != null
      ? ((IRTextFunctionEx)FunctionList.SelectedItem).Function.Number
      : 0;

    byte[] data = StateSerializer.Serialize(state);
    Session.SavePanelState(data, this, null);
  }

  public override void OnSessionEnd() {
    base.OnSessionEnd();
    ResetUI(); //? TODO: Await, make OnSessionEnd async
  }

  private async void PanelToolbarTray_OnHelpClicked(object sender, EventArgs e) {
    await HelpPanel.DisplayPanelHelp(PanelKind, Session);
  }

  #endregion

  private void SectionPreviewKeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Return) {
      var sectionEx = ((ListViewItem)sender).Content as IRTextSectionEx;
      OpenSectionImpl(sectionEx);
      e.Handled = true;
    }
  }

  private async void FunctionPreviewKeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Return) {
      await OpenFunction(sender);
    }
  }

  private void ModulePreviewKeyDown(object sender, KeyEventArgs e) {
    if (e.Key == Key.Return) {
      SwitchModule(sender);
    }
  }

  private void FunctionToolbar_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) {
    ExportFunctionListAsHtmlFile(@"C:\work\out.html");
  }

  private void CopyFunctionDetailsExecuted(object sender, ExecutedRoutedEventArgs e) {
    CopyFunctionListAsHtml();
  }

  private void ExportFunctionListHtmlExecuted(object sender, ExecutedRoutedEventArgs e) {
    string path = Utils.ShowSaveFileDialog("HTML file|*.html", "*.html|All Files|*.*");
    bool success = true;

    if (!string.IsNullOrEmpty(path)) {
      try {
        success = ExportFunctionListAsHtmlFile(path);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to save function list to {path}: {ex.Message}");
      }

      if (!success) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show($"Failed to save function list to {path}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  private void ExportFunctionListMarkdownExecuted(object sender, ExecutedRoutedEventArgs e) {
    string path = Utils.ShowSaveFileDialog("Markdown file|*.md", "*.md|All Files|*.*");
    bool success = true;

    if (!string.IsNullOrEmpty(path)) {
      try {
        success = ExportFunctionListAsMarkdownFile(path);
      }
      catch (Exception ex) {
        Trace.WriteLine($"Failed to save function list to {path}: {ex.Message}");
      }

      if (!success) {
        using var centerForm = new DialogCenteringHelper(this);
        MessageBox.Show($"Failed to save function list to {path}", "IR Explorer",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
      }
    }
  }

  private void ScrollUpButton_OnClick(object sender, RoutedEventArgs e) {
    if (FunctionList.Items is {Count: > 0}) {
      FunctionList.ScrollIntoView(FunctionList.Items[0]);
    }
  }

  public void EnterBinaryDisplayMode() {
    IsFunctionListVisible = false;
    IsFunctionListVisible = true;
    SectionCountColumnVisible = false;
    ShowModules = false;
    ShowSections = false;
  }
}