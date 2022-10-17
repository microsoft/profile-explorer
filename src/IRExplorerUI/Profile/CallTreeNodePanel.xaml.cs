﻿using System;
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

namespace IRExplorerUI.Profile;

public interface IFunctionProfileInfoProvider {
    public class ModuleProfileInfo {
        public ModuleProfileInfo() {}

        public ModuleProfileInfo(string name) {
            Name = name;
        }

        public string Name { get; set; }
        public double Percentage { get; set; }
        public TimeSpan Weight { get; set; }
    }

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
    public event EventHandler<ProfileCallTreeNode> NodeClick;
    public event EventHandler<ProfileCallTreeNode> NodeDoubleClick;

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
        DataContext = this;
    }

    public void Show(ProfileCallTreeNode node) {
        CallTreeNode = node;
    }

    public async Task ShowAsync(ProfileCallTreeNode node) {
        CallTreeNode = node;
        await ShowDetailsAsync();
    }

    public async Task ShowDetailsAsync() {
        await SetupInstanceInfo(CallTreeNode);
        BacktraceList.Show(await Task.Run(() => funcInfoProvider_.GetBacktrace(CallTreeNode)), filter: false);
        FunctionList.Show(await Task.Run(() => funcInfoProvider_.GetTopFunctions(CallTreeNode)));
        ModuleList.Show(await Task.Run(() => funcInfoProvider_.GetTopModules(CallTreeNode)));
    }

    private async Task SetupInstanceInfo(ProfileCallTreeNode node) {
        //? TODO: IF same func, don't recompute

        var callTree = Session.ProfileData.CallTree;
        instanceNodes_ = callTree.GetCallTreeNodes(node.Function);

        if (instanceNodes_ == null) {
            return;
        }

        var combinedNode = await Task.Run(() => callTree.GetCombinedCallTreeNode(node.Function));
        InstancesNode = SetupNodeExtension(combinedNode);
        FunctionInstancesCount = instanceNodes_.Count;

        // Show all instances.
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
            await ShowAsync(node);
        }
    }

    private async void NextInstanceButton_Click(object sender, RoutedEventArgs e) {
        if (nodeInstanceIndex_ < FunctionInstancesCount - 1) {
            int index = Utils.IsKeyboardModifierActive() ? FunctionInstancesCount -1 : nodeInstanceIndex_ + 1;
            var node = instanceNodes_[index];
            NodeInstanceChanged?.Invoke(this, node);
            await ShowAsync(node);
        }
    }
}