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
    public class ProfileCallTreeNodeEx : BindableObject {
        public ProfileCallTreeNodeEx(ProfileCallTreeNode callTreeNode,
                                     ProfileCallTreeNode parentCallTreeNode) {
            CallTreeNode = callTreeNode;
            ParentCallTreeNode = parentCallTreeNode;
        }

        public ProfileCallTreeNode CallTreeNode { get; }
        public ProfileCallTreeNode ParentCallTreeNode { get; }
        public string FunctionName { get; set; }
        public string FullFunctionName { get; set; }
        public string ModuleName { get; set; }
        public double Percentage { get; set; }
        public double PercentageExclusive { get; set; }
        public double PercentageParent { get; set; }
        public string Time { get; set; }
        public string ExclusiveTime { get; set; }
        public string ParentTime { get; set; }

        public bool ParentTimeVisible => PercentageParent > 0;
        //? dummy merged node fields
    }

    public partial class CallTreeNodePopup : DraggablePopup, INotifyPropertyChanged {
        private const int MaxFunctionNmeLength = 80;
        private const int MaxModuleNameLength = 50;

        private ProfileCallTreeNodeEx nodeEx_;

        public ISession Session { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public CallTreeNodePopup(ProfileCallTreeNode node, ProfileCallTreeNode parentNode,
                                 Point position, double width, double height,
                                 UIElement referenceElement, ISession session) {
            Session = session;
            Node = SetupNodeExtension(node, parentNode);
            //? for long names, expander?

            InitializeComponent();
            Initialize(position, width, height, referenceElement);
            PanelResizeGrip.ResizedControl = this;
            DataContext = this;

            var stackTrace = CreateStackBackTrace(node);
            TextView.SetText(stackTrace);
        }

        private ProfileCallTreeNodeEx SetupNodeExtension(ProfileCallTreeNode node,
                                                         ProfileCallTreeNode parentNode) {
            var nodeEx = new ProfileCallTreeNodeEx(node, parentNode) {
                FullFunctionName = node.FunctionName,
                FunctionName = FormatFunctionName(node, demangle: true, MaxFunctionNmeLength),
                ModuleName = FormatModuleName(node, MaxModuleNameLength), Percentage = Session.ProfileData.ScaleFunctionWeight(node.Weight),
                PercentageExclusive = Session.ProfileData.ScaleFunctionWeight(node.ExclusiveWeight),
            };

            nodeEx.Time = $"{nodeEx.Percentage.AsPercentageString()} ({node.Weight.AsMillisecondsString()})";
            nodeEx.ExclusiveTime = $"{nodeEx.PercentageExclusive.AsPercentageString()} ({node.ExclusiveWeight.AsMillisecondsString()})";

            if (parentNode != null) {
                nodeEx.PercentageParent = (double)node.Weight.Ticks / parentNode.Weight.Ticks;
                nodeEx.ParentTime = nodeEx.PercentageParent.AsPercentageString();
            }

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

        public async Task SetText(string text, FunctionIR function, IRTextSection section,
                                  IRDocument associatedDocument, ISession session) {
            await TextView.SetText(text, function, section, associatedDocument, session);
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