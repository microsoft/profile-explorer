// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IRExplorerCore;
using IRExplorerUI.Controls;
using IRExplorerUI.Utilities;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace IRExplorerUI.Profile;

public interface IFunctionProfileInfoProvider {
  List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node);
  (List<ProfileCallTreeNode>, List<ModuleProfileInfo> Modules) GetTopFunctionsAndModules(ProfileCallTreeNode node);
}

public class ProfileCallTreeNodeEx : BindableObject {
  public ProfileCallTreeNodeEx(ProfileCallTreeNode callTreeNode) {
    CallTreeNode = callTreeNode;
  }

  public ProfileCallTreeNode CallTreeNode { get; }
  public string FunctionName { get; set; }
  public string FullFunctionName { get; set; }
  public string ModuleName { get; set; }
  public double Percentage { get; set; }
  public double ExclusivePercentage { get; set; }
  public bool IsMarked { get; set; }
  public TimeSpan Weight => CallTreeNode.Weight;
  public TimeSpan ExclusiveWeight => CallTreeNode.ExclusiveWeight;
}

public class ThreadListItem {
  public int ThreadId { get; set; }
  public string Title { get; set; }
  public string ToolTip { get; set; }
  public string WeightToolTip { get; set; }
  public TimeSpan Weight { get; set; }
  public TimeSpan ExclusiveWeight { get; set; }
  public double Percentage { get; set; }
  public double ExclusivePercentage { get; set; }
  public Brush Background { get; set; }
}

public partial class CallTreeNodePanel : ToolPanelControl, INotifyPropertyChanged {
  private const int MaxFunctionNameLength = 100;
  private const int MaxModuleNameLength = 50;
  private ProfileCallTreeNodeEx nodeEx_;
  private int nodeInstanceIndex_;
  private List<ProfileCallTreeNode> instanceNodes_;
  private IFunctionProfileInfoProvider funcInfoProvider_;
  private bool histogramVisible_;
  private ProfileCallTreeNodeEx instancesNode_;
  private ProfileCallTreeNodeEx averageNode_;
  private ProfileCallTreeNodeEx mediansNode_;
  private int funcInstancesCount_;
  private int currentInstanceIndex_;
  private bool showDetails_;
  private bool showInstanceNavigation_;
  private bool useSelfTimeHistogram_;
  private CallTreeNodeSettings settings_;
  private bool enableSingleNodeActions_;

  public CallTreeNodePanel() {
    InitializeComponent();
    SetupEvents();
    DataContext = this;
    CallTreeNode = null;
  }

  public event EventHandler<ProfileCallTreeNode> NodeInstanceChanged;
  public event EventHandler<ProfileCallTreeNode> BacktraceNodeClick;
  public event EventHandler<ProfileCallTreeNode> BacktraceNodeDoubleClick;
  public event EventHandler<ProfileCallTreeNode> InstanceNodeClick;
  public event EventHandler<ProfileCallTreeNode> InstanceNodeDoubleClick;
  public event EventHandler<ProfileCallTreeNode> FunctionNodeClick;
  public event EventHandler<ProfileCallTreeNode> FunctionNodeDoubleClick;
  public event EventHandler<ProfileCallTreeNode> ModuleNodeClick;
  public event EventHandler<ProfileCallTreeNode> ModuleNodeDoubleClick;
  public event EventHandler<List<ProfileCallTreeNode>> NodesSelected;
  public event EventHandler MarkingChanged;
  public event PropertyChangedEventHandler PropertyChanged;

  public override ISession Session {
    get => base.Session;
    set {
      base.Session = value;
      BacktraceList.Session = value;
      FunctionList.Session = value;
      ModuleList.Session = value;
      ModuleFunctionList.Session = value;
      CategoryList.Session = value;
      CategoryFunctionList.Session = value;
      InstancesList.Session = value;
    }
  }

  public CallTreeNodeSettings Settings {
    get => settings_;
    set {
      settings_ = value;
      BacktraceList.Settings = value;
      FunctionList.Settings = value;
      ModuleList.Settings = value;
      ModuleFunctionList.Settings = value;
      CategoryList.Settings = value;
      CategoryFunctionList.Settings = value;
      InstancesList.Settings = value;

      BacktraceList.RestoreColumnsState(settings_.StackListColumns);
      FunctionList.RestoreColumnsState(settings_.FunctionListColumns);
      ModuleList.RestoreColumnsState(settings_.ModuleListColumns);
      ModuleFunctionList.RestoreColumnsState(settings_.ModuleFunctionListColumns);
      CategoryList.RestoreColumnsState(settings_.CategoryListColumns);
      CategoryFunctionList.RestoreColumnsState(settings_.CategoryFunctionListColumns);
      InstancesList.RestoreColumnsState(settings_.InstanceListColumns);

      OnPropertyChanged(nameof(HistogramInstanceBrush));
      OnPropertyChanged(nameof(HistogramAverageBrush));
      OnPropertyChanged(nameof(HistogramMedianBrush));

      if (IsHistogramExpanded) {
        InvokeSetupInstancesHistogram();
      }
    }
  }

  public void SaveListColumnSettings() {
    BacktraceList.SaveColumnsState(settings_.StackListColumns);
    FunctionList.SaveColumnsState(settings_.FunctionListColumns);
    ModuleList.SaveColumnsState(settings_.ModuleListColumns);
    ModuleFunctionList.SaveColumnsState(settings_.ModuleFunctionListColumns);
    CategoryList.SaveColumnsState(settings_.CategoryListColumns);
    CategoryFunctionList.SaveColumnsState(settings_.CategoryFunctionListColumns);
    InstancesList.SaveColumnsState(settings_.InstanceListColumns);
  }

  public bool IsInstancesExpanded {
    get => settings_ is {ExpandInstances: true};
    set {
      settings_.ExpandInstances = value;
      OnPropertyChanged();
    }
  }

  public bool IsHistogramExpanded {
    get => settings_ is {ExpandHistogram: true};
    set {
      settings_.ExpandHistogram = value;
      OnPropertyChanged();
    }
  }

  public bool IsThreadsListExpanded {
    get => settings_ is {ExpandThreads: true};
    set {
      settings_.ExpandThreads = value;
      OnPropertyChanged();
    }
  }

  public ProfileCallTreeNodeEx CallTreeNode {
    get => nodeEx_;
    set {
      SetField(ref nodeEx_, value);

      if (value != null) {
        Utils.EnableControl(this);
      }
      else {
        Utils.DisableControl(this);
      }
    }
  }

  public Brush HistogramInstanceBrush => settings_?.HistogramCurrentColor.AsBrush();
  public Brush HistogramAverageBrush => settings_?.HistogramAverageColor.AsBrush();
  public Brush HistogramMedianBrush => settings_?.HistogramMedianColor.AsBrush();

  public ProfileCallTreeNodeEx InstancesNode {
    get => instancesNode_;
    set => SetField(ref instancesNode_, value);
  }

  public ProfileCallTreeNodeEx AverageNode {
    get => averageNode_;
    set => SetField(ref averageNode_, value);
  }

  public ProfileCallTreeNodeEx MedianNode {
    get => mediansNode_;
    set => SetField(ref mediansNode_, value);
  }

  public int FunctionInstancesCount {
    get => funcInstancesCount_;
    set => SetField(ref funcInstancesCount_, value);
  }

  public int CurrentInstanceIndex {
    get => currentInstanceIndex_;
    set => SetField(ref currentInstanceIndex_, value);
  }

  public bool ShowDetails {
    get => showDetails_;
    set {
      SetField(ref showDetails_, value);
      OnPropertyChanged(nameof(ShowInstanceNavigation));
    }
  }

  public bool ShowInstanceNavigation {
    get => showInstanceNavigation_ && showDetails_;
    set => SetField(ref showInstanceNavigation_, value);
  }

  public bool UseSelfTimeHistogram {
    get => useSelfTimeHistogram_;
    set {
      if (SetField(ref useSelfTimeHistogram_, value)) {
        SetupInstancesHistogram(instanceNodes_, CallTreeNode.CallTreeNode, useSelfTimeHistogram_);
      }
    }
  }

  public bool EnableSingleNodeActions {
    get => enableSingleNodeActions_;
    set => SetField(ref enableSingleNodeActions_, value);
  }

  public void Show(ProfileCallTreeNodeEx nodeEx) {
    CallTreeNode = nodeEx;
  }

  public async Task ShowWithDetailsAsync(ProfileCallTreeNode node) {
    await ShowWithDetailsAsync(SetupNodeExtension(node, Session));
  }

  public async Task ShowWithDetailsAsync(ProfileCallTreeNodeEx node) {
    CallTreeNode = node;
    await ShowDetailsAsync();
  }

  public async Task ShowDetailsAsync() {
    ModuleFunctionList.Reset();
    CategoryFunctionList.Reset();

    await SetupInstanceInfo(CallTreeNode.CallTreeNode);
    ShowDetails = true;

    var markings = App.Settings.MarkingSettings.BuiltinMarkingCategories.FunctionColors;
    var task1 = Task.Run(() => funcInfoProvider_.GetBacktrace(CallTreeNode.CallTreeNode));
    var task2 = Task.Run(() => funcInfoProvider_.GetTopFunctionsAndModules(CallTreeNode.CallTreeNode));
    var task4 = Task.Run(() => ProfilingUtils.CollectMarkedFunctions(markings, false,
                                                                     Session, CallTreeNode.CallTreeNode));
    BacktraceList.ShowFunctions(await task1);
    var (funcList, moduleList) = await task2;
    FunctionList.ShowFunctions(funcList, settings_.FunctionListViewFilter);
    ModuleList.ShowModules(moduleList);
    CategoryList.ShowCategories(await task4);

    ModuleList.SelectFirstItem();
    CategoryList.SelectFirstItem();
  }

  public void Initialize(ISession session, IFunctionProfileInfoProvider funcInfoProvider) {
    Session = session;
    Settings = App.Settings.CallTreeNodeSettings;
    funcInfoProvider_ = funcInfoProvider;
  }

  public void UpdateMarkedFunctions() {
    BacktraceList.UpdateMarkedFunctions();
    FunctionList.UpdateMarkedFunctions();
    BacktraceList.UpdateMarkedFunctions();
    InstancesList.UpdateMarkedFunctions();
    ModuleList.UpdateMarkedFunctions();
    ModuleFunctionList.UpdateMarkedFunctions();
    CategoryList.UpdateMarkedFunctions();
    CategoryFunctionList.UpdateMarkedFunctions();
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

  private void SetupEvents() {
    BacktraceList.NodeClick += (sender, node) => BacktraceNodeClick?.Invoke(sender, node);
    BacktraceList.NodeDoubleClick += (sender, node) => BacktraceNodeDoubleClick?.Invoke(sender, node);
    BacktraceList.MarkingChanged += (sender, args) => MarkingChanged?.Invoke(sender, args);
    FunctionList.NodeClick += (sender, node) => FunctionNodeClick?.Invoke(sender, node);
    FunctionList.NodeDoubleClick += (sender, node) => FunctionNodeDoubleClick?.Invoke(sender, node);
    FunctionList.MarkingChanged += (sender, args) => MarkingChanged?.Invoke(sender, args);
    InstancesList.NodeClick += (sender, node) => InstanceNodeClick?.Invoke(sender, node);
    InstancesList.NodeDoubleClick += (sender, node) => InstanceNodeDoubleClick?.Invoke(sender, node);
    InstancesList.MarkingChanged += (sender, args) => MarkingChanged?.Invoke(sender, args);
    ModuleList.NodeClick += (sender, node) => ModuleNodeClick?.Invoke(sender, node);
    ModuleList.NodeDoubleClick += (sender, node) => ModuleNodeDoubleClick?.Invoke(sender, node);
    ModuleList.ModuleClick += (sender, moduleInfo) => UpdateModuleFunctions(moduleInfo);
    ModuleList.MarkingChanged += (sender, args) => MarkingChanged?.Invoke(sender, args);
    ModuleFunctionList.NodeClick += (sender, node) => FunctionNodeClick?.Invoke(sender, node);
    ModuleFunctionList.NodeDoubleClick += (sender, node) => FunctionNodeDoubleClick?.Invoke(sender, node);
    ModuleFunctionList.MarkingChanged += (sender, args) => MarkingChanged?.Invoke(sender, args);
    CategoryList.CategoryClick += (sender, category) => UpdateCategoryFunctions(category);
    CategoryFunctionList.NodeClick += (sender, node) => FunctionNodeClick?.Invoke(sender, node);
    CategoryFunctionList.NodeDoubleClick += (sender, node) => FunctionNodeDoubleClick?.Invoke(sender, node);
    CategoryFunctionList.MarkingChanged += (sender, args) => MarkingChanged?.Invoke(sender, args);
  }

  private void UpdateCategoryFunctions(FunctionMarkingCategory category) {
    CategoryFunctionList.ShowFunctions(category.SortedFunctions, settings_.FunctionListViewFilter);
  }

  private void UpdateModuleFunctions(ModuleProfileInfo moduleInfo) {
    ModuleFunctionList.ShowFunctions(moduleInfo.Functions, settings_.FunctionListViewFilter);
  }

  private async Task SetupInstanceInfo(ProfileCallTreeNode node) {
    var callTree = Session.ProfileData.CallTree;
    var groupNode = node as ProfileCallTreeGroupNode;

    // Collect all instances associated with the node's function.
    // With multiple nodes selected, the node is a ProfileCallTreeGroupNode,
    // combine the instances of all functions in the group.
    if (groupNode != null) {
      instanceNodes_ = await Task.Run(() => {
        // For a node group, combine the instances for each node.
        var instanceNodes = new List<ProfileCallTreeNode>();
        var handledFuncts = new HashSet<IRTextFunction>();

        foreach (var n in groupNode.Nodes) {
          if (handledFuncts.Add(n.Function)) {
            instanceNodes.AddRange(callTree.GetSortedCallTreeNodes(n.Function));
          }
        }

        return instanceNodes;
      });
    }
    else {
      instanceNodes_ = callTree.GetSortedCallTreeNodes(node.Function);
    }

    if (instanceNodes_.Count == 0) {
      ShowInstanceNavigation = false;
      return;
    }

    // Create the node that sums up all selected nodes.
    // With multiple nodes selected, the node is a ProfileCallTreeGroupNode,
    // combine the instances of all functions in the group.
    ProfileCallTreeNode combinedNode = null;

    if (groupNode != null) {
      combinedNode = await Task.Run(() => {
        // For a node group, combine the instances for each node.
        var instanceNodes = new List<ProfileCallTreeNode>();
        var handledFuncts = new HashSet<IRTextFunction>();

        foreach (var n in groupNode.Nodes) {
          if (handledFuncts.Add(n.Function)) {
            instanceNodes.Add(callTree.GetCombinedCallTreeNode(n.Function));
          }
        }

        return ProfileCallTree.CombinedCallTreeNodes(instanceNodes);
      });
    }
    else {
      combinedNode = await Task.Run(() => callTree.GetCombinedCallTreeNode(node.Function));
    }

    InstancesNode = SetupNodeExtension(combinedNode, Session);
    FunctionInstancesCount = instanceNodes_.Count;

    // Show all instances.
    InstancesList.ShowFunctions(instanceNodes_);
    ShowInstanceNavigation = instanceNodes_.Count > 1 && groupNode == null;
    EnableSingleNodeActions = groupNode == null;

    nodeInstanceIndex_ = instanceNodes_.FindIndex(instanceNode => instanceNode == node);
    CurrentInstanceIndex = nodeInstanceIndex_ + 1;

    // Show average node.
    var averageNode = instanceNodes_[0].Clone();
    averageNode.Weight = combinedNode.Weight / instanceNodes_.Count;
    averageNode.ExclusiveWeight = combinedNode.ExclusiveWeight / instanceNodes_.Count;
    var averageNodeEx = SetupNodeExtension(averageNode, Session);
    averageNodeEx.Percentage = Session.ProfileData.ScaleFunctionWeight(averageNodeEx.Weight);
    averageNodeEx.ExclusivePercentage = Session.ProfileData.ScaleFunctionWeight(averageNodeEx.ExclusiveWeight);
    AverageNode = averageNodeEx;

    // Show median time.
    var medianNode = instanceNodes_[instanceNodes_.Count / 2];
    var medianNodeEx = SetupNodeExtension(medianNode, Session);
    medianNodeEx.Percentage = Session.ProfileData.ScaleFunctionWeight(medianNodeEx.Weight);
    medianNodeEx.ExclusivePercentage = Session.ProfileData.ScaleFunctionWeight(medianNodeEx.ExclusiveWeight);
    MedianNode = medianNodeEx;

    SetupThreadList(node);
    InstancesExpander.IsExpanded = settings_.ExpandInstances;
    HistogramExpander.IsExpanded = settings_.ExpandHistogram;
    ThreadsExpander.IsExpanded = settings_.ExpandThreads;

    if (settings_.ExpandHistogram) {
      InvokeSetupInstancesHistogram();
    }
  }

  private void SetupThreadList(ProfileCallTreeNode node) {
    var threadList = node.SortedByWeightPerThreadWeights;
    var itemsList = new List<ThreadListItem>();

    foreach (var item in threadList) {
      var threadInfo = Session.ProfileData.FindThread(item.ThreadId);
      var backColor = App.Settings.TimelineSettings.
        GetThreadBackgroundColors(threadInfo, item.ThreadId).Margin;

      // Compute thread percentage relative to selected node instance.
      double threadPercentage = node.Weight.Ticks > 0 ?
        (double)item.Values.Weight.Ticks / node.Weight.Ticks : 0;
      double selfThreadPercentage = node.ExclusiveWeight.Ticks > 0 ?
        (double)item.Values.ExclusiveWeight.Ticks / node.ExclusiveWeight.Ticks : 0;

      itemsList.Add(new ThreadListItem() {
        ThreadId = item.ThreadId,
        Title = $"{item.ThreadId}",
        ToolTip = threadInfo != null ? threadInfo.Name : null,
        WeightToolTip = $"{threadPercentage.AsPercentageString()} of instance time\n" +
                        $"{selfThreadPercentage.AsPercentageString()} of instance self time",
        Background = backColor,
        Weight = item.Values.Weight,
        ExclusiveWeight = item.Values.ExclusiveWeight,
        Percentage = Session.ProfileData.ScaleFunctionWeight(item.Values.Weight),
        ExclusivePercentage = Session.ProfileData.ScaleFunctionWeight(item.Values.ExclusiveWeight)
      });
    }

    ThreadList.ItemsSource = itemsList;
  }

  private void SetupInstancesHistogram(List<ProfileCallTreeNode> nodes,
                                       ProfileCallTreeNode currentNode, bool useSelfTime) {
    if (useSelfTime) {
      SetupInstancesHistogramImpl(nodes, currentNode, node => node.ExclusiveWeight);
    }
    else {
      SetupInstancesHistogramImpl(nodes, currentNode, node => node.Weight);
    }
  }

  private void SetupInstancesHistogramImpl(List<ProfileCallTreeNode> nodes, ProfileCallTreeNode currentNode,
                                           Func<ProfileCallTreeNode, TimeSpan> selectWeight) {
    nodes = new List<ProfileCallTreeNode>(nodes);
    nodes.Sort((a, b) => selectWeight(a).CompareTo(selectWeight(b)));

    const double maxBinCount = 20;
    var maxWeight = selectWeight(nodes[^1]);
    var minWeight = selectWeight(nodes[0]);
    var delta = maxWeight - minWeight;
    long weightPerBin = (long)Math.Ceiling(delta.Ticks / maxBinCount);
    double maxHeight = InstanceHistogramHost.ActualHeight;

    // Partition nodes into bins.
    var bins = new List<(TimeSpan Weight, TimeSpan TotalWeight, int StartIndex, int Count)>();
    var binWeight = selectWeight(nodes[0]);
    var binTotalWeight = binWeight;
    int binStartIndex = 0;
    int binCount = 1;

    for (int i = 1; i < nodes.Count; i++) {
      var node = nodes[i];

      if ((selectWeight(node) - binWeight).Ticks > weightPerBin) {
        bins.Add((binWeight, binTotalWeight, binStartIndex, binCount));
        binWeight = selectWeight(node);
        binTotalWeight = binWeight;
        binStartIndex = i;
        binCount = 1;
      }
      else {
        binTotalWeight += selectWeight(node);
        binCount++;
      }
    }

    bins.Add((binWeight, binTotalWeight, binStartIndex, binCount));

    // Add each bin to the graph.
    var model = new PlotModel();
    var series = new HistogramSeries();
    series.FillColor = OxyColors.WhiteSmoke;
    series.StrokeThickness = 0.5;

    OxyColor ColorFromArgb(Color color) {
      return OxyColor.FromArgb(color.A, color.R, color.G, color.B);
    }

    foreach (var bin in bins) {
      if (bin.Count == 0) {
        continue;
      }

      long start = bin.Weight.Ticks;
      long end = (bin.Weight + TimeSpan.FromTicks(weightPerBin)).Ticks;
      var barColor = ColorFromArgb(settings_.HistogramBarColor);
      var item = new BinHistogramItem(start, end, bin.Count, bin.Count, barColor) {
        Count = bin.Count,
        TotalWeight = bin.TotalWeight,
        AverageWeight = TimeSpan.FromTicks(bin.TotalWeight.Ticks / bin.Count),
        Nodes = nodes.GetRange(bin.StartIndex, bin.Count)
      };
      series.Items.Add(item);
    }

    series.LabelFormatString = "{3}";
    series.LabelMargin = 2;
    series.MouseDown += (sender, e) => {
      if (e.HitTestResult.Item is BinHistogramItem binItem) {
        NodesSelected?.Invoke(this, binItem.Nodes);
      }
    };
    model.Series.Add(series);

    // Setup horizontal axis.
    var weightAxis = new LinearAxis();
    weightAxis.Position = AxisPosition.Bottom;
    weightAxis.TickStyle = TickStyle.None;

    if (delta.TotalSeconds <= 1) {
      weightAxis.LabelFormatter = value => TimeSpan.FromTicks((long)value).AsMillisecondsString();
    }
    else {
      weightAxis.LabelFormatter = value => TimeSpan.FromTicks((long)value).AsSecondsString();
    }

    weightAxis.MinimumPadding = 0;
    weightAxis.MinimumDataMargin = 6;
    weightAxis.IsPanEnabled = false;
    weightAxis.IsZoomEnabled = false;
    model.Axes.Add(weightAxis);

    // Setup vertical axis.
    var countAxis = new LinearAxis();
    countAxis.Position = AxisPosition.Right;
    countAxis.TickStyle = TickStyle.None;
    countAxis.Title = "Instances";
    countAxis.StringFormat = " ";
    countAxis.IsPanEnabled = false;
    countAxis.IsZoomEnabled = false;
    countAxis.MinimumPadding = 0;
    countAxis.MaximumDataMargin = 20;
    model.Axes.Add(countAxis);

    // Add line for current instance.
    void AddLineAnnotation(double value, OxyColor color, LineStyle lineStyle) {
      var line = new LineAnnotation();
      line.Type = LineAnnotationType.Vertical;
      line.X = value;
      line.StrokeThickness = 2;
      line.Color = color;
      line.LineStyle = lineStyle;
      model.Annotations.Add(line);
    }

    void AddPointAnnotation(double value, OxyColor color) {
      var point = new PointAnnotation();
      point.Shape = MarkerType.Diamond;
      point.X = value;
      point.Fill = color;
      model.Annotations.Add(point);
    }

    AddPointAnnotation(currentNode.Weight.Ticks, ColorFromArgb(settings_.HistogramCurrentColor));

    // Add lines for median and average time.
    double average = nodes.Average(node => node.Weight.Ticks);
    AddLineAnnotation(average, ColorFromArgb(settings_.HistogramAverageColor), LineStyle.Dot);

    long median = nodes[nodes.Count / 2].Weight.Ticks;
    AddLineAnnotation(median, ColorFromArgb(settings_.HistogramMedianColor), LineStyle.Dot);

    var plotView = new PlotView();
    plotView.Model = model;
    model.IsLegendVisible = false;
    plotView.VerticalContentAlignment = VerticalAlignment.Center;
    plotView.Width = InstanceHistogramHost.ActualWidth;
    plotView.Height = InstanceHistogramHost.ActualHeight;
    plotView.MinHeight = plotView.Height;
    model.PlotAreaBorderThickness = new OxyThickness(0, 0, 0, 0.5);
    plotView.Background = InstanceHistogramHost.Background;

    // Override tooltip.
    plotView.Controller = new PlotController();
    plotView.Controller.UnbindMouseDown(OxyMouseButton.Left);
    plotView.Controller.BindMouseEnter(OxyPlot.PlotCommands.HoverSnapTrack);

    object tooltipTemplate = Application.Current.FindResource("HistogramTooltipTemplate");
    plotView.DefaultTrackerTemplate = (ControlTemplate)tooltipTemplate;

    InstanceHistogramHost.Children.Clear();
    InstanceHistogramHost.Children.Add(plotView);
    histogramVisible_ = true;
  }

  public static ProfileCallTreeNodeEx SetupNodeExtension(ProfileCallTreeNode node, ISession session) {
    if (node == null) {
      return null;
    }

    var nameProvider = session.CompilerInfo.NameProvider;
    var moduleMap = new Dictionary<string, int>();
    var functionMap = new Dictionary<string, int>();
    var fullFunctionMap = new Dictionary<string, int>();
    string moduleNames = null;
    string functionNames = null;
    string fullFunctionNames = null;

    if (node is ProfileCallTreeGroupNode groupNode) {
      foreach (var n in groupNode.Nodes) {
        moduleMap.AccumulateValue(n.FormatModuleName(nameProvider.FormatFunctionName, MaxModuleNameLength), 1);
        functionMap.AccumulateValue(n.FormatFunctionName(nameProvider.FormatFunctionName, MaxFunctionNameLength), 1);
        fullFunctionMap.AccumulateValue(n.FormatFunctionName(nameProvider.FormatFunctionName), 1);
      }

      moduleNames = GenerateNameListText(moduleMap, "\n");
      functionNames = GenerateNameListText(functionMap, "\n");
      fullFunctionNames = GenerateNameListText(fullFunctionMap, "\n");
    }
    else {
      moduleNames = node.FormatModuleName(nameProvider.FormatFunctionName, MaxModuleNameLength);
      functionNames = node.FormatFunctionName(nameProvider.FormatFunctionName, MaxFunctionNameLength);
      fullFunctionNames = node.FormatFunctionName(nameProvider.FormatFunctionName);
    }

    var nodeEx = new ProfileCallTreeNodeEx(node) {
      FullFunctionName = fullFunctionNames,
      FunctionName = functionNames,
      ModuleName = moduleNames,
      Percentage = session.ProfileData.ScaleFunctionWeight(node.Weight),
      ExclusivePercentage = session.ProfileData.ScaleFunctionWeight(node.ExclusiveWeight)
    };

    return nodeEx;
  }

  private static string GenerateNameListText(Dictionary<string, int> nameMap, string separator) {
    var nameList = nameMap.ToList();
    nameList.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.Ordinal));
    var sb = new StringBuilder();

    foreach (var pair in nameList) {
      if (pair.Item2 > 1) {
        sb.Append($"{pair.Item2} x {pair.Item1}{separator}");
      }
      else {
        sb.Append($"{pair.Item1}{separator}");
      }
    }

    return sb.ToString().Trim();
  }

  private async void PreviousInstanceButton_Click(object sender, RoutedEventArgs e) {
    if (nodeInstanceIndex_ > 0) {
      int index = Utils.IsKeyboardModifierActive() ? 0 : nodeInstanceIndex_ - 1;
      var node = instanceNodes_[index];
      NodeInstanceChanged?.Invoke(this, node);
      await ShowWithDetailsAsync(node);
    }
  }

  private async void NextInstanceButton_Click(object sender, RoutedEventArgs e) {
    if (nodeInstanceIndex_ < FunctionInstancesCount - 1) {
      int index = Utils.IsKeyboardModifierActive() ? FunctionInstancesCount - 1 : nodeInstanceIndex_ + 1;
      var node = instanceNodes_[index];
      NodeInstanceChanged?.Invoke(this, node);
      await ShowWithDetailsAsync(node);
    }
  }

  private void HistogramHost_Expanded(object sender, RoutedEventArgs e) {
    if (!histogramVisible_ && instanceNodes_ != null) {
      InvokeSetupInstancesHistogram();
    }
  }

  private void InvokeSetupInstancesHistogram() {
    // Create the histogram on the next render pass,
    // after the host has been expanded, otherwise some computations assert.
    Dispatcher.BeginInvoke(() => {
      if (instanceNodes_ != null) {
        SetupInstancesHistogram(instanceNodes_, CallTreeNode.CallTreeNode, useSelfTimeHistogram_);
      }
    }, DispatcherPriority.Render);
  }

  public class BinHistogramItem : HistogramItem {
    public BinHistogramItem(double rangeStart, double rangeEnd, double area, int count) : base(
      rangeStart, rangeEnd, area, count) {
    }

    public BinHistogramItem(double rangeStart, double rangeEnd, double area, int count, OxyColor color) : base(
      rangeStart, rangeEnd, area, count, color) {
    }

    public new int Count { get; set; }
    public TimeSpan AverageWeight { get; set; }
    public TimeSpan TotalWeight { get; set; }
    public List<ProfileCallTreeNode> Nodes { get; set; }
  }

  public void Reset() {
    Utils.DisableControl(this);
  }

  private async void ThreadListItem_MouseDown(object sender, MouseButtonEventArgs e) {
    if (e.LeftButton == MouseButtonState.Pressed &&
        e.ClickCount >= 2) {
      var threadItem = ((FrameworkElement)sender).DataContext as ThreadListItem;
      await ApplyThreadFilterAction(threadItem, ThreadActivityAction.FilterToThread);
    }
  }

  private async Task ApplyThreadFilterAction(ThreadListItem threadItem, ThreadActivityAction action) {
    if (Session.FindPanel(ToolPanelKind.Timeline) is TimelinePanel timelinePanel && threadItem != null) {
      await timelinePanel.ApplyThreadFilterAction(threadItem.ThreadId, action);
    }
  }

  public RelayCommand<object> ExcludeThreadCommand =>
    new(async obj => {
      if (((FrameworkElement)obj).DataContext is ThreadListItem threadItem) {
        await ApplyThreadFilterAction(threadItem, ThreadActivityAction.ExcludeThread);
      }
    });
  public RelayCommand<object> ExcludeSameNameThreadCommand =>
    new(async obj => {
      if (((FrameworkElement)obj).DataContext is ThreadListItem threadItem) {
        await ApplyThreadFilterAction(threadItem, ThreadActivityAction.ExcludeSameNameThread);
      }
    });
  public RelayCommand<object> FilterToThreadCommand =>
    new(async obj => {
      if (((FrameworkElement)obj).DataContext is ThreadListItem threadItem) {
        await ApplyThreadFilterAction(threadItem, ThreadActivityAction.FilterToThread);
      }
    });
  public RelayCommand<object> FilterToSameNameThreadCommand =>
    new(async obj => {
      if (((FrameworkElement)obj).DataContext is ThreadListItem threadItem) {
        await ApplyThreadFilterAction(threadItem, ThreadActivityAction.FilterToSameNameThread);
      }
    });

  private void ThreadContextMenuButton_Click(object sender, RoutedEventArgs e) {
    Utils.ShowContextMenu(sender as FrameworkElement, this);
  }

  public RelayCommand<object> PreviewFunctionCommand => new(async obj => {
    if (((FrameworkElement)obj).DataContext is ThreadListItem threadItem &&
        instancesNode_.CallTreeNode != null) {
      var filter = new ProfileSampleFilter(threadItem.ThreadId);
      await IRDocumentPopupInstance.ShowPreviewPopup(instancesNode_.CallTreeNode.Function, "",
                                                     ThreadsExpander, Session, filter);
    }
  });
  public RelayCommand<object> OpenFunctionCommand => new(async obj => {
    var mode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent;
    await OpenFunction(obj, mode);
  });
  public RelayCommand<object> OpenFunctionInNewTabCommand => new(async obj => {
    await OpenFunction(obj, OpenSectionKind.NewTabDockRight);
  });

  private async Task OpenFunction(object obj, OpenSectionKind openMode) {
    if (((FrameworkElement)obj).DataContext is ThreadListItem threadItem &&
        instancesNode_.CallTreeNode != null) {
      var filter = new ProfileSampleFilter(threadItem.ThreadId);
      await Session.OpenProfileFunction(instancesNode_.CallTreeNode, openMode, filter);
    }
  }

  private void CopyModuleButton_OnClick(object sender, RoutedEventArgs e) {
    Clipboard.SetText(instancesNode_.ModuleName);
  }

  private void CopyFUnctionButton_OnClick(object sender, RoutedEventArgs e) {
    Clipboard.SetText(instancesNode_.FullFunctionName);
  }

  private async void PreviewButton_OnClick(object sender, RoutedEventArgs e) {
    await IRDocumentPopupInstance.ShowPreviewPopup(instancesNode_.CallTreeNode.Function, "",
                                                   ThreadsExpander, Session);
  }

  private async void OpenButton_OnClick(object sender, RoutedEventArgs e) {
    await Session.OpenProfileFunction(instancesNode_.CallTreeNode, OpenSectionKind.NewTabDockRight);
  }

  private void MarkModuleButton_OnClick(object sender, RoutedEventArgs e) {
    Utils.ShowContextMenu(sender as FrameworkElement, this);
  }

  public RelayCommand<object> MarkModuleCommand => new(async obj => {
    if (obj is SelectedColorEventArgs e) {
      if (CallTreeNode.CallTreeNode is ProfileCallTreeGroupNode groupNode) {
        foreach (var node in groupNode.Nodes) {
          MarkModule(node.ModuleName, e.SelectedColor);
        }
      }
      else {
        MarkModule(CallTreeNode.CallTreeNode.ModuleName, e.SelectedColor);
      }
    }
  });
  public RelayCommand<object> MarkFunctionCommand => new(async obj => {
    if (obj is SelectedColorEventArgs e) {
      if (CallTreeNode.CallTreeNode is ProfileCallTreeGroupNode groupNode) {
        foreach (var node in groupNode.Nodes) {
          string funcName = node.FormatFunctionName(Session);
          MarkFunction(funcName, e.SelectedColor);
        }
      }
      else {
        string funcName = CallTreeNode.CallTreeNode.FormatFunctionName(Session);
        MarkFunction(funcName, e.SelectedColor);
      }
    }
  });

  private void MarkModule(string module, Color color) {
    var markingSettings = App.Settings.MarkingSettings;
    markingSettings.UseModuleColors = true;
    markingSettings.AddModuleColor(module, color);
    MarkingChanged?.Invoke(this, EventArgs.Empty);
  }

  private void MarkFunction(string function, Color color) {
    var markingSettings = App.Settings.MarkingSettings;
    markingSettings.UseFunctionColors = true;
    markingSettings.AddFunctionColor(function, color);
    MarkingChanged?.Invoke(this, EventArgs.Empty);
  }
}
