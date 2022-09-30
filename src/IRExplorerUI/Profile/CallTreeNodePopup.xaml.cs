using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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

namespace IRExplorerUI.Controls {
    public class ModuleProfileInfo {
        public ModuleProfileInfo() {}

        public ModuleProfileInfo(string name) {
            Name = name;
        }

        public string Name { get; set; }
        public double Percentage { get; set; }
        public TimeSpan Weight { get; set; }
    }

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

    public partial class CallTreeNodePopup : DraggablePopup, INotifyPropertyChanged {
        private const int MaxFunctionNmeLength = 80;
        private const int MaxModuleNameLength = 50;

        private ProfileCallTreeNodeEx nodeEx_;
        private IFunctionProfileInfoProvider funcInfoProvider_;

        public ISession Session { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public CallTreeNodePopup(ProfileCallTreeNode node, IFunctionProfileInfoProvider funcInfoProvider,
                                 Point position, double width, double height,
                                 UIElement referenceElement, ISession session) {
            funcInfoProvider_ = funcInfoProvider;
            Session = session;
            Node = SetupNodeExtension(node);

            InitializeComponent();
            Initialize(position, width, height, referenceElement);
            PanelResizeGrip.ResizedControl = this;
            DataContext = this;
            BacktraceList.Session = session;
            FunctionList.Session = session;
            ModuleList.Session = session;

            BacktraceList.Show(funcInfoProvider_.GetBacktrace(node));
            FunctionList.Show(funcInfoProvider.GetTopFunctions(node));
            ModuleList.Show(funcInfoProvider.GetTopModules(node));
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

        private string CreateStackBackTrace(ProfileCallTreeNode node) {
            var builder = new StringBuilder();
            AppendStackToolTipFrames(node, builder);
            return builder.ToString();
        }

        //? callback or provide
        private void AppendStackToolTipFrames(ProfileCallTreeNode node, StringBuilder builder) {
            var percentage = Session.ProfileData.ScaleFunctionWeight(node.Weight);
            var funcName = FormatFunctionName(node, true, 80);

            if (true /*settings_.PrependModuleToFunction*/) {
                funcName = $"{node.ModuleName}!{funcName}";
            }

            builder.Append($"{percentage.AsPercentageString(2, false).PadLeft(6)} | {node.Weight.AsMillisecondsString()} | {funcName}");
            builder.AppendLine(funcName);

            if (node.HasCallers) {
                AppendStackToolTipFrames(node.Callers[0], builder);
            }
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

        public ProfileCallTreeNodeEx Node {
            get => nodeEx_;
            set {
                if (nodeEx_ != value) {
                    nodeEx_ = value;
                    OnPropertyChange(nameof(Node));
                }
            }
        }

        public ProfileCallTreeNode CallTreeNode => Node.CallTreeNode;

        public override bool ShouldStartDragging(MouseButtonEventArgs e) {
            if (e.LeftButton == MouseButtonState.Pressed && ToolbarPanel.IsMouseOver) {
                if (!IsDetached) {
                    DetachPopup();
                }

                return true;
            }

            return false;
        }

        private void OnPropertyChange(string propertyname) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            ClosePopup();
        }
    }
}