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
    private const int MaxFunctionNmeLength = 80;
    private const int MaxModuleNameLength = 50;

    private ProfileCallTreeNodeEx nodeEx_;

    public IFunctionProfileInfoProvider FunctionInfoProvider { get; set; }

    //? TODO: use settigs for module columns
    //? ShowModuleColumn

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

    private bool showDetails_;
    public bool ShowDetails {
        get => showDetails_;
        set => SetField(ref showDetails_, value);
    }

    public CallTreeNodePanel() {
        InitializeComponent();
        DataContext = this;
    }

    public async Task Show(ProfileCallTreeNode node) {
        CallTreeNode = node;
        BacktraceList.Show(await Task.Run(() => FunctionInfoProvider.GetBacktrace(node)), filter: false);
        FunctionList.Show(await Task.Run(() => FunctionInfoProvider.GetTopFunctions(node)));
        ModuleList.Show(await Task.Run(() => FunctionInfoProvider.GetTopModules(node)));
        await SetupInstanceInfo(node);
    }

    private async Task SetupInstanceInfo(ProfileCallTreeNode node) {
        var callTree = Session.ProfileData.CallTree;
        var instanceNodes = callTree.GetCallTreeNodes(node.Function);

        if (instanceNodes == null) {
            return;
        }

        var combinedNode = await Task.Run(() => callTree.GetCombinedCallTreeNode(node.Function));
        InstancesNode = SetupNodeExtension(combinedNode);
        FunctionInstancesCount = instanceNodes.Count;

        // Show all instances.
        instanceNodes.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        InstancesList.Show(instanceNodes, false);

        // Show median time.
        var medianNode = instanceNodes[instanceNodes.Count / 2];
        var medianNodeEx = SetupNodeExtension(medianNode);
        medianNodeEx.Percentage = Session.ProfileData.ScaleFunctionWeight(medianNodeEx.Weight);
        medianNodeEx.ExclusivePercentage = Session.ProfileData.ScaleFunctionWeight(medianNodeEx.ExclusiveWeight);
        MedianNode = medianNodeEx;
    }

    private ProfileCallTreeNodeEx SetupNodeExtension(ProfileCallTreeNode node) {
        var nodeEx = new ProfileCallTreeNodeEx(node) {
            FullFunctionName = node.FunctionName,
            FunctionName = FormatFunctionName(node, demangle: true, MaxFunctionNmeLength),
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
        FunctionInfoProvider = funcInfoProvider;
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
}