// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerUI.Controls;

namespace IRExplorerUI.Profile;

public partial class CallTreeNodePopup : DraggablePopup, INotifyPropertyChanged {
  internal const double DefaultTextSize = 12;
  private const int MaxPreviewNameLength = 80;
  public static readonly TimeSpan PopupHoverDuration = TimeSpan.FromMilliseconds(50);
  public static readonly TimeSpan PopupHoverLongDuration = TimeSpan.FromMilliseconds(1000);
  private static readonly Typeface DefaultTextFont = new Typeface("Segoe UI");
  private bool showResizeGrip_;
  private bool canExpand_;
  private bool showBacktraceView_;
  private string backtraceText_;

  public CallTreeNodePopup(ProfileCallTreeNode node, IFunctionProfileInfoProvider funcInfoProvider,
                           Point position, UIElement referenceElement, ISession session, bool canExpand = true) {
    InitializeComponent();
    Initialize(position, referenceElement);
    PanelResizeGrip.ResizedControl = this;

    Session = session;
    CanExpand = canExpand;
    PanelHost.ShowInstanceNavigation = false;
    PanelHost.Initialize(session, funcInfoProvider);
    UpdateNode(node);
    DataContext = this;
  }

  public event PropertyChangedEventHandler PropertyChanged;
  public ISession Session { get; set; }
  public ProfileCallTreeNode CallTreeNode { get; set; }
  public ProfileCallTreeNodeEx Node => PanelHost.Node;

  public bool ShowResizeGrip {
    get => showResizeGrip_;
    set => SetField(ref showResizeGrip_, value);
  }

  public bool CanExpand {
    get => canExpand_;
    set => SetField(ref canExpand_, value);
  }

  public bool ShowBacktraceView {
    get => showBacktraceView_;
    set => SetField(ref showBacktraceView_, value);
  }

  public string BacktraceText {
    get => backtraceText_;
    set => SetField(ref backtraceText_, value);
  }

  public static (string, double) CreateBacktraceText(ProfileCallTreeNode node, int maxLevel,
                                                     FunctionNameFormatter nameFormatter) {
    var sb = new StringBuilder();
    double maxTextWidth = 0;

    while (node != null && maxLevel-- > 0) {
      string funcName = node.FormatFunctionName(nameFormatter, MaxPreviewNameLength);
      var textSize = Utils.MeasureString(funcName, DefaultTextFont, DefaultTextSize);
      maxTextWidth = Math.Max(maxTextWidth, textSize.Width);
      sb.AppendLine(funcName);
      node = node.Caller;
    }

    if (node != null && node.HasCallers) {
      sb.AppendLine("...");
    }

    return (sb.ToString().Trim(), maxTextWidth);
  }

  public void UpdateNode(ProfileCallTreeNode node) {
    if (node == CallTreeNode) {
      return;
    }

    CallTreeNode = node;
    PanelHost.Show(CallTreeNode);
    OnPropertyChanged(nameof(Node));
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

  protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
    if (EqualityComparer<T>.Default.Equals(field, value)) return false;
    field = value;
    OnPropertyChanged(propertyName);
    return true;
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    Session.UnregisterDetachedPanel(this);
    ClosePopup();
  }

  private async void ExpandButton_OnClick(object sender, RoutedEventArgs e) {
    DetachPopup();
    await PanelHost.ShowDetailsAsync();
    Height = 400;
    ShowResizeGrip = true;
  }
}
