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
    public partial class CallTreeNodePopup : DraggablePopup, INotifyPropertyChanged {
        private string panelTitle_;
        private ProfileCallTreeNode node_;

        public ISession Session { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public CallTreeNodePopup(ProfileCallTreeNode node, Point position, double width, double height,
                                 UIElement referenceElement, ISession session) {
            CallTreeNode = node;
            PanelTitle = node.FunctionName;

            InitializeComponent();
            Initialize(position, width, height, referenceElement);
            Session = session;
            PanelResizeGrip.ResizedControl = this;
            DataContext = this;

            var stackTrace = CreateStackBackTrace(node);
            TextView.SetText(stackTrace);
        }

        private string CreateStackBackTrace(ProfileCallTreeNode node) {
            var builder = new StringBuilder();
            AppendStackToolTipFrames(node, builder);
            return builder.ToString();
        }

        private void AppendStackToolTipFrames(ProfileCallTreeNode node, StringBuilder builder) {
            var percentage = Session.ProfileData.ScaleFunctionWeight(node.Weight);
            var funcName = FormatFunctionName(node, 80);

            if (true /*settings_.PrependModuleToFunction*/) {
                funcName = $"{node.ModuleName}!{funcName}";
            }

            builder.Append($"{percentage.AsPercentageString(2, false).PadLeft(6)} | {node.Weight.AsMillisecondsString()} | {funcName}");
            builder.AppendLine(funcName);

            if (node.HasCallers) {
                AppendStackToolTipFrames(node.Callers[0], builder);
            }
        }

        private string FormatFunctionName(ProfileCallTreeNode node, int maxLength = int.MaxValue) {
            var funcName = node.FunctionName;

            if (true) {
                //? option
                var nameProvider = Session.CompilerInfo.NameProvider;

                if (nameProvider.IsDemanglingSupported) {
                    funcName = nameProvider.DemangleFunctionName(funcName, nameProvider.GlobalDemanglingOptions);
                }
            }

            if (funcName.Length > maxLength) {
                funcName = $"{funcName.Substring(0, maxLength)}...";
            }

            return funcName;
        }

        public async Task SetText(string text, FunctionIR function, IRTextSection section,
                                  IRDocument associatedDocument, ISession session) {
            await TextView.SetText(text, function, section, associatedDocument, session);
        }

        public string PanelTitle {
            get => panelTitle_;
            set {
                if (panelTitle_ != value) {
                    panelTitle_ = value;
                    OnPropertyChange(nameof(PanelTitle));
                }
            }
        }

        public ProfileCallTreeNode CallTreeNode {
            get => node_;
            set {
                if (node_ != value) {
                    node_ = value;
                    OnPropertyChange(nameof(CallTreeNode));
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