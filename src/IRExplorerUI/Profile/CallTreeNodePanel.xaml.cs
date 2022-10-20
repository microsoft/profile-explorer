using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using IRExplorerCore;
using IRExplorerCore.IR;
using IRExplorerUI.Profile;
using OxyPlot;
using OxyPlot.Series;

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

    public TimeSpan Weight => CallTreeNode.Weight;
    public TimeSpan ExclusiveWeight => CallTreeNode.ExclusiveWeight;

    //? dummy merged node fields
}

public partial class CallTreeNodePanel : ToolPanelControl, INotifyPropertyChanged {
    private const int MaxFunctionNameLength = 100;
    private const int MaxModuleNameLength = 50;

    private ProfileCallTreeNodeEx nodeEx_;
    private int nodeInstanceIndex_;
    private List<ProfileCallTreeNode> instanceNodes_;
    private IFunctionProfileInfoProvider funcInfoProvider_;

    public event EventHandler<ProfileCallTreeNode> NodeInstanceChanged;
    public event EventHandler<ProfileCallTreeNode> BacktraceNodeClick;
    public event EventHandler<ProfileCallTreeNode> BacktraceNodeDoubleClick;
    public event EventHandler<ProfileCallTreeNode> InstanceNodeClick;
    public event EventHandler<ProfileCallTreeNode> InstanceNodeDoubleClick;
    public event EventHandler<ProfileCallTreeNode> FunctionNodeClick;
    public event EventHandler<ProfileCallTreeNode> FunctionNodeDoubleClick;
    public event EventHandler<ProfileCallTreeNode> ModuleNodeClick;
    public event EventHandler<ProfileCallTreeNode> ModuleNodeDoubleClick;

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

    public ProfileCallTreeNodeEx Node {
        get => nodeEx_;
        set => SetField(ref nodeEx_, value);
    }

    public ProfileCallTreeNode CallTreeNode {
        get => Node?.CallTreeNode;
        set {
            if (value != Node?.CallTreeNode) {
                Node = SetupNodeExtension(value);
                OnPropertyChanged();
            }
        }
    }

    private ProfileCallTreeNodeEx instancesNode_;
    public ProfileCallTreeNodeEx InstancesNode {
        get => instancesNode_;
        set => SetField(ref instancesNode_, value);
    }

    private ProfileCallTreeNodeEx mediansNode_;
    public ProfileCallTreeNodeEx MedianNode {
        get => mediansNode_;
        set => SetField(ref mediansNode_, value);
    }

    private int funcInstancesCount_;
    public int FunctionInstancesCount {
        get => funcInstancesCount_;
        set => SetField(ref funcInstancesCount_, value);
    }

    private int currentInstanceIndex_;
    public int CurrentInstanceIndex {
        get => currentInstanceIndex_;
        set => SetField(ref currentInstanceIndex_, value);
    }

    private bool showDetails_;
    public bool ShowDetails {
        get => showDetails_;
        set => SetField(ref showDetails_, value);
    }

    public CallTreeNodePanel() {
        InitializeComponent();
        SetupEvents();
        DataContext = this;
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

    public void Show(ProfileCallTreeNode node) {
        CallTreeNode = node;
    }

    public async Task ShowWithDetailsAsync(ProfileCallTreeNode node) {
        CallTreeNode = node;
        await ShowDetailsAsync();
    }

    public async Task ShowDetailsAsync() {
        await SetupInstanceInfo(CallTreeNode);
        ShowDetails = true;

        BacktraceList.Show(await Task.Run(() => funcInfoProvider_.GetBacktrace(CallTreeNode)), filter: false);
        FunctionList.Show(await Task.Run(() => funcInfoProvider_.GetTopFunctions(CallTreeNode)));
        ModuleList.Show(await Task.Run(() => funcInfoProvider_.GetTopModules(CallTreeNode)));
    }

    private async Task SetupInstanceInfo(ProfileCallTreeNode node) {
        Trace.WriteLine($"Setup node {node.FunctionName}");

        //? TODO: IF same func, don't recompute

        var callTree = Session.ProfileData.CallTree;
        instanceNodes_ = callTree.GetCallTreeNodes(node.Function);

        if (instanceNodes_ == null) {
            return;
        }

        var combinedNode = await Task.Run(() => callTree.GetCombinedCallTreeNode(node.Function));
        InstancesNode = SetupNodeExtension(combinedNode);
        FunctionInstancesCount = instanceNodes_.Count;

        // Show all instances. Make a copy of the list since it's shared
        // with all other instances of the node and it may be iterated on another thread.
        instanceNodes_ = new List<ProfileCallTreeNode>(instanceNodes_);
        instanceNodes_.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        InstancesList.Show(instanceNodes_, false);

        nodeInstanceIndex_ = instanceNodes_.FindIndex(instanceNode => instanceNode == node);
        CurrentInstanceIndex = nodeInstanceIndex_ + 1;

        // Show median time.
        var medianNode = instanceNodes_[instanceNodes_.Count / 2];
        var medianNodeEx = SetupNodeExtension(medianNode);
        medianNodeEx.Percentage = Session.ProfileData.ScaleFunctionWeight(medianNodeEx.Weight);
        medianNodeEx.ExclusivePercentage = Session.ProfileData.ScaleFunctionWeight(medianNodeEx.ExclusiveWeight);
        MedianNode = medianNodeEx;

        await SetupInstancesHistogram(instanceNodes_);
    }
    

    private async Task SetupInstancesHistogram(List<ProfileCallTreeNode> nodes) {
        var model = new PlotModel();

        var histogramSeries1 = new HistogramSeries();
        histogramSeries1.FillColor = OxyColors.Gray;
        histogramSeries1.StrokeThickness = 1;
        histogramSeries1.Title = "Measurements";

        var maxWeight = nodes[0].Weight;
        var minWeight = nodes[^1].Weight;
        var delta = maxWeight - minWeight;
        double maxBinCount = 10;
        double weightPerBin = Math.Ceiling(delta.TotalMicroseconds / maxBinCount);
        double maxHeight = InstanceHistogramHost.ActualHeight - 50;

        TimeSpan binWeight = nodes[0].Weight;
        TimeSpan binTotalWeight = nodes[0].Weight;
        int binStartIndex = 0;
        int binCount = 1;

        var bins = new List<(TimeSpan Weight, TimeSpan TotalWeight, int Count)>();

        for(int i = 1; i < nodes.Count; i++) {
            var node = nodes[i];

            if ((binWeight - node.Weight).TotalMicroseconds > weightPerBin) {
                bins.Add((binWeight, binTotalWeight, binCount));
                binWeight = node.Weight;
                binTotalWeight = node.Weight;
                binStartIndex = i;
                binCount = 1;
            }
            else {
                binTotalWeight += node.Weight;
                binCount++;
            }
        }

        bins.Add((binWeight, binTotalWeight, binCount));

        foreach (var bin in bins) {
            var start = bin.Weight.TotalMicroseconds;
            var end = (bin.Weight + TimeSpan.FromMicroseconds(weightPerBin)).TotalMicroseconds;
            var height = (maxHeight / nodes.Count) * bin.Count;
            histogramSeries1.Items.Add(new HistogramItem(start, end, height, bin.Count));
        }

        histogramSeries1.LabelFormatString = "{0}";
        histogramSeries1.LabelPlacement = LabelPlacement.Inside;
        histogramSeries1.LabelMargin = 5;
        model.Series.Add(histogramSeries1);

        var plotView = new OxyPlot.SkiaSharp.Wpf.PlotView();
        plotView.Model = model;
        model.IsLegendVisible = true;
        plotView.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
        plotView.Width = InstanceHistogramHost.ActualWidth;
        plotView.Height = InstanceHistogramHost.ActualHeight - 50;
        InstanceHistogramHost.Children.Add(plotView);
    }

    private ProfileCallTreeNodeEx SetupNodeExtension(ProfileCallTreeNode node) {
        var nodeEx = new ProfileCallTreeNodeEx(node) {
            FullFunctionName = node.FunctionName,
            FunctionName = FormatFunctionName(node, demangle: true, MaxFunctionNameLength),
            ModuleName = FormatModuleName(node, MaxModuleNameLength),
            Percentage = Session.ProfileData.ScaleFunctionWeight(node.Weight),
            ExclusivePercentage = Session.ProfileData.ScaleFunctionWeight(node.ExclusiveWeight),
        };

        return nodeEx;
    }

    private string FormatFunctionName(ProfileCallTreeNode node, bool demangle = true, int maxLength = int.MaxValue) {
        return FormatName(node.FunctionName, demangle, maxLength);
    }

    private string FormatModuleName(ProfileCallTreeNode node, int maxLength = int.MaxValue) {
        return FormatName(node.ModuleName, false, maxLength);
    }

    private string FormatName(string name, bool demangle, int maxLength) {
        if (string.IsNullOrEmpty(name)) {
            return name;
        }

        if (demangle) {
            var nameProvider = Session.CompilerInfo.NameProvider;

            if (nameProvider.IsDemanglingSupported) {
                name = nameProvider.DemangleFunctionName(name, nameProvider.GlobalDemanglingOptions);
            }
        }

        if (name.Length > maxLength && name.Length > 2) {
            name = $"{name.Substring(0, maxLength - 2)}...";
        }

        return name;
    }

    public void Initialize(ISession session, IFunctionProfileInfoProvider funcInfoProvider) {
        Session = session;
        funcInfoProvider_ = funcInfoProvider;
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
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
            int index = Utils.IsKeyboardModifierActive() ? FunctionInstancesCount -1 : nodeInstanceIndex_ + 1;
            var node = instanceNodes_[index];
            NodeInstanceChanged?.Invoke(this, node);
            await ShowWithDetailsAsync(node);
        }
    }
}