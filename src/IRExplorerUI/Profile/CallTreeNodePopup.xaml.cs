﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using IRExplorerUI.Controls;
using IRExplorerUI.Profile;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace IRExplorerUI.Profile;

public partial class CallTreeNodePopup : DraggablePopup, INotifyPropertyChanged {
    public static readonly TimeSpan PopupHoverDuration = TimeSpan.FromMilliseconds(50);

    public ISession Session { get; set; }
    public ProfileCallTreeNode CallTreeNode { get; set; }
    public ProfileCallTreeNodeEx Node => PanelHost.Node;
    public event PropertyChangedEventHandler PropertyChanged;

    public CallTreeNodePopup(ProfileCallTreeNode node, IFunctionProfileInfoProvider funcInfoProvider,
        Point position, double width, double height,
        UIElement referenceElement, ISession session) {
        
        InitializeComponent();
        Initialize(position, width, height, referenceElement);
        PanelResizeGrip.ResizedControl = this;

        Session = session;
        PanelHost.Initialize(session, funcInfoProvider);
        UpdateNode(node);
        DataContext = this;
    }

    public void UpdateNode(ProfileCallTreeNode node) {
        if (node == CallTreeNode) {
            return;
        }

        CallTreeNode = node;
        PanelHost.Show(CallTreeNode);
        OnPropertyChanged(nameof(Node));

        Dispatcher.BeginInvoke(async () => {
            await PanelHost.ShowDetailsAsync();
        });
    }

    private bool showResizeGrip_;
    public bool ShowResizeGrip {
        get => showResizeGrip_;
        set => SetField(ref showResizeGrip_, value);
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Session.UnregisterDetachedPanel(this);
        ClosePopup();
    }

    private void ExpandButton_OnClick(object sender, RoutedEventArgs e) {
        DetachPopup();
        ShowResizeGrip = true;
        PanelHost.ShowDetails = true;
        Height = 400;
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
}