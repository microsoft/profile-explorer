// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections;
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
  private ProfileCallTreeNodeEx nodeEx_;

  public CallTreeNodePopup(ProfileCallTreeNode node, IFunctionProfileInfoProvider funcInfoProvider,
                           Point position, UIElement referenceElement, ISession session, bool canExpand = true) {
    InitializeComponent();
    Initialize(position, referenceElement);
    PanelResizeGrip.ResizedControl = this;

    Session = session;
    CanExpand = canExpand;
    PanelHost.ShowInstanceNavigation = false;
    PanelHost.Initialize(session, funcInfoProvider);
    StackTraceListView.Session = Session;
    UpdateNode(node);
    DataContext = this;
  }

  public event PropertyChangedEventHandler PropertyChanged;
  public ISession Session { get; set; }
  public ProfileCallTreeNodeEx CallTreeNode {
    get => nodeEx_;
    set {
      SetField(ref nodeEx_, value);
      OnPropertyChanged(nameof(TitleText));
      OnPropertyChanged(nameof(TitleTooltipText));
      OnPropertyChanged(nameof(DescriptionText));
    }
  }

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

  private string title_;
  public string TitleText {
    get => CallTreeNode != null ? CallTreeNode.FunctionName : title_;
    set => SetField(ref title_, value);
  }


  private string titleTooltipText_;
  public string TitleTooltipText {
    get => CallTreeNode != null ? CallTreeNode.FullFunctionName : titleTooltipText_;
    set => SetField(ref titleTooltipText_, value);
  }


  private string descriptionText_;
  public string DescriptionText {
    get => CallTreeNode != null ? CallTreeNode.ModuleName : descriptionText_;
    set => SetField(ref descriptionText_, value);
  }


  public void ShowBackTrace(ProfileCallTreeNode node, int maxLevel,
                            FunctionNameFormatter nameFormatter) {
    UpdateNode(node); // Set title.
    var list = new List<ProfileCallTreeNode>();
    double maxTextWidth = 0;

    while (node != null && maxLevel-- > 0) {
      string funcName = node.FormatFunctionName(nameFormatter, MaxPreviewNameLength);
      var textSize = Utils.MeasureString(funcName, DefaultTextFont, DefaultTextSize);
      maxTextWidth = Math.Max(maxTextWidth, textSize.Width);
      list.Add(node);
      node = node.Caller;
    }

    StackTraceListView.ShowSimpleList(list);

    //? TODO: Also adjust name column with in ListView
    Width = maxTextWidth + 20;
    ShowBacktraceView = true;
  }

  public void ShowFunctions(List<ProfileCallTreeNode> list,
                            FunctionNameFormatter nameFormatter) {

    StackTraceListView.ShowSimpleList(list);

    //? TODO: Also adjust name column with in ListView
    //Width = maxTextWidth + 20;
    ShowBacktraceView = true;
  }

  public void UpdateNode(ProfileCallTreeNode node) {
    CallTreeNode = CallTreeNodePanel.SetupNodeExtension(node, Session);

    if (!ShowBacktraceView) {
      PanelHost.Show(CallTreeNode);
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

    if (!ShowBacktraceView) {
      await PanelHost.ShowDetailsAsync();
    }

    MinWidth = 450;
    Width = Math.Max(Width, 450);
    Height = Math.Max(Height, 400);
    ShowResizeGrip = true;
    ExpandButton.Visibility = Visibility.Hidden;
  }
}