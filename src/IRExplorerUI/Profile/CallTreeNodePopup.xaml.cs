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
using IRExplorerUI.Controls;
using IRExplorerUI.Profile;

namespace IRExplorerUI.Profile; 

public partial class CallTreeNodePopup : DraggablePopup, INotifyPropertyChanged {
    private IFunctionProfileInfoProvider funcInfoProvider_;

    public ISession Session { get; set; }
    public event PropertyChangedEventHandler PropertyChanged;

    public CallTreeNodePopup(ProfileCallTreeNode node, IFunctionProfileInfoProvider funcInfoProvider,
        Point position, double width, double height,
        UIElement referenceElement, ISession session) {
        Session = session;

        InitializeComponent();
        Initialize(position, width, height, referenceElement);
        PanelResizeGrip.ResizedControl = this;
        DataContext = this;
        PanelHost.Initialize(session, funcInfoProvider);
        PanelHost.Show(node);
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