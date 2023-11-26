// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerUI.Controls;

namespace IRExplorerUI.Profile;

public partial class CallTreeNodePopup : DraggablePopup, INotifyPropertyChanged {
  internal const double DefaultTextSize = 12;
  private const int MaxPreviewNameLength = 80;
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

  public bool ShowSimpleView {
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
  private const double MaxPopupWidth = 800;
  private const double MinPopupWidth = 300;

  public string DescriptionText {
    get => CallTreeNode != null ? CallTreeNode.ModuleName : descriptionText_;
    set {
      SetField(ref descriptionText_, value);
      OnPropertyChanged(nameof(HasDescriptionText));
    }
  }

  public bool HasDescriptionText => !string.IsNullOrEmpty(DescriptionText);

  public void ShowBackTrace(ProfileCallTreeNode node, int maxLevel,
                            FunctionNameFormatter nameFormatter) {
    UpdateNode(node); // Set title.
    var list = new List<ProfileCallTreeNode>();

    while (node != null && maxLevel-- > 0) {
      list.Add(node);
      node = node.Caller;
    }

    ShowSimpleView = true;
    UpdatePopupWidth(MeasureMaxTextWidth(list, nameFormatter));
    StackTraceListView.ShowSimpleList(list);
  }

  public void ShowFunctions(List<ProfileCallTreeNode> list,
                            FunctionNameFormatter nameFormatter) {
    ShowSimpleView = true;
    UpdatePopupWidth(MeasureMaxTextWidth(list, nameFormatter));
    StackTraceListView.ShowSimpleList(list);
  }

  public void UpdateNode(ProfileCallTreeNode node) {
    CallTreeNode = CallTreeNodePanel.SetupNodeExtension(node, Session);

    if (!ShowSimpleView) {
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

  private double MeasureMaxTextWidth(List<ProfileCallTreeNode> list,
                                     FunctionNameFormatter nameFormatter) {
    double maxTextWidth = 0;

    foreach (var node in list) {
      string funcName = node.FormatFunctionName(nameFormatter, MaxPreviewNameLength);
      var textSize = Utils.MeasureString(funcName, DefaultTextFont, DefaultTextSize);
      maxTextWidth = Math.Max(maxTextWidth, textSize.Width);
    }

    return maxTextWidth;
  }

  private void UpdatePopupWidth(double maxTextWidth) {
    double margin = SystemParameters.VerticalScrollBarWidth;
    Width = Math.Max(MinPopupWidth, Math.Min(maxTextWidth, MaxPopupWidth));

    // Leave some space for the vertical scroll bar
    // to avoid having a horizontal one by default.
    StackTraceListView.FunctionColumnWidth = Math.Max(MinPopupWidth -  2 * margin, Width - 2 * margin);
  }
  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    Session.UnregisterDetachedPanel(this);
    ClosePopup();
  }

  private async void ExpandButton_OnClick(object sender, RoutedEventArgs e) {
    DetachPopup();

    if (!ShowSimpleView) {
      await PanelHost.ShowDetailsAsync();
    }

    MinWidth = 450;
    Width = Math.Max(Width, 450);
    Height = Math.Max(Height, 400);
    ShowResizeGrip = true;
    ExpandButton.Visibility = Visibility.Hidden;
  }
}