// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace IRExplorerUI.Profile;

public interface IFunctionProfileInfoProvider {
  List<ProfileCallTreeNode> GetBacktrace(ProfileCallTreeNode node);
  List<ProfileCallTreeNode> GetTopFunctions(ProfileCallTreeNode node);
  List<ModuleProfileInfo> GetTopModules(ProfileCallTreeNode node);
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
  public event PropertyChangedEventHandler PropertyChanged;

  public override ISession Session {
    get => base.Session;
    set {
      base.Session = value;
      BacktraceList.Session = value;
      FunctionList.Session = value;
      ModuleList.Session = value;
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
      InstancesList.Settings = value;
    }
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
    await SetupInstanceInfo(CallTreeNode.CallTreeNode);
    ShowDetails = true;

    BacktraceList.Show(await Task.Run(() => funcInfoProvider_.GetBacktrace(CallTreeNode.CallTreeNode)));
    FunctionList.Show(await Task.Run(() => funcInfoProvider_.GetTopFunctions(CallTreeNode.CallTreeNode)),
                      settings_.FunctionListViewFilter);
    ModuleList.Show(await Task.Run(() => funcInfoProvider_.GetTopModules(CallTreeNode.CallTreeNode)));
  }

  public void Initialize(ISession session, IFunctionProfileInfoProvider funcInfoProvider) {
    Session = session;
    Settings = App.Settings.CallTreeNodeSettings;
    funcInfoProvider_ = funcInfoProvider;
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
    FunctionList.NodeClick += (sender, node) => FunctionNodeClick?.Invoke(sender, node);
    FunctionList.NodeDoubleClick += (sender, node) => FunctionNodeDoubleClick?.Invoke(sender, node);
    InstancesList.NodeClick += (sender, node) => InstanceNodeClick?.Invoke(sender, node);
    InstancesList.NodeDoubleClick += (sender, node) => InstanceNodeDoubleClick?.Invoke(sender, node);
    ModuleList.NodeClick += (sender, node) => ModuleNodeClick?.Invoke(sender, node);
    ModuleList.NodeDoubleClick += (sender, node) => ModuleNodeDoubleClick?.Invoke(sender, node);
  }

  private async Task SetupInstanceInfo(ProfileCallTreeNode node) {
    var callTree = Session.ProfileData.CallTree;
    instanceNodes_ = callTree.GetSortedCallTreeNodes(node.Function);

    //? TODO: Should rather be an assert.
    if (instanceNodes_ == null || instanceNodes_.Count == 0) {
      ShowInstanceNavigation = false;
      return;
    }

    var combinedNode = await Task.Run(() => callTree.GetCombinedCallTreeNode(node.Function));
    InstancesNode = SetupNodeExtension(combinedNode, Session);
    FunctionInstancesCount = instanceNodes_.Count;

    // Show all instances.
    InstancesList.Show(instanceNodes_);
    ShowInstanceNavigation = instanceNodes_.Count > 1;

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

    InstancesExpander.IsExpanded = settings_.ExpandInstances;
    HistogramExpander.IsExpanded = settings_.ExpandHistogram;
    
    if (settings_.ExpandHistogram) {
      InvokeSetupInstancesHistogram();
    }
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

    foreach (var bin in bins) {
      if (bin.Count == 0) {
        continue;
      }

      long start = bin.Weight.Ticks;
      long end = (bin.Weight + TimeSpan.FromTicks(weightPerBin)).Ticks;
      double height = maxHeight / nodes.Count * bin.Count;
      var item = new BinHistogramItem(start, end, bin.Count, bin.Count, OxyColors.Khaki) {
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
      line.StrokeThickness = 1.5;
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

    AddPointAnnotation(currentNode.Weight.Ticks, OxyColors.Green);

    // Add lines for median and average time.
    double average = nodes.Average(node => node.Weight.Ticks);
    AddLineAnnotation(average, OxyColors.Firebrick, LineStyle.Dot);

    long median = nodes[nodes.Count / 2].Weight.Ticks;
    AddLineAnnotation(median, OxyColors.DarkBlue, LineStyle.Dot);

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
    var nodeEx = new ProfileCallTreeNodeEx(node) {
      FullFunctionName = node.FormatFunctionName(nameProvider.FormatFunctionName),
      FunctionName =
        node.FormatFunctionName(nameProvider.FormatFunctionName, MaxFunctionNameLength),
      ModuleName = node.FormatModuleName(nameProvider.FormatFunctionName, MaxModuleNameLength),
      Percentage = session.ProfileData.ScaleFunctionWeight(node.Weight),
      ExclusivePercentage = session.ProfileData.ScaleFunctionWeight(node.ExclusiveWeight)
    };

    return nodeEx;
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
      SetupInstancesHistogram(instanceNodes_, CallTreeNode.CallTreeNode, useSelfTimeHistogram_);
    }, DispatcherPriority.Background);
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
}
