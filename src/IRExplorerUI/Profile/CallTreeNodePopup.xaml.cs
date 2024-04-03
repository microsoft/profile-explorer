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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using IRExplorerUI.Controls;

namespace IRExplorerUI.Profile;

public partial class CallTreeNodePopup : DraggablePopup, INotifyPropertyChanged {
  internal const double DefaultTextSize = 12;
  private const int MaxPreviewNameLength = 80;
  private const double InitialWidth = 450;
  private const double InitialHeight = 400;
  private Typeface defaultTextFont_;
  private bool showResizeGrip_;
  private bool canExpand_;
  private bool showBacktraceView_;
  private string backtraceText_;
  private ProfileCallTreeNodeEx nodeEx_;

  public CallTreeNodePopup(ProfileCallTreeNode node, IFunctionProfileInfoProvider funcInfoProvider,
                           Point position, UIElement referenceElement,
                           ISession session, bool canExpand = true) {
    InitializeComponent();
    Initialize(position, referenceElement);
    PanelResizeGrip.ResizedControl = this;

    //? TODO: Use GetTextTypeface everywhere instead of hardcoding fonts
    defaultTextFont_ = Utils.GetTextTypeface(TitleTextBlock);

    Session = session;
    CanExpand = canExpand;
    PanelHost.ShowInstanceNavigation = false;
    PanelHost.Initialize(session, funcInfoProvider);
    FunctionListView.Session = Session;
    FunctionListView.Settings = App.Settings.CallTreeNodeSettings;
    UpdateNode(node);
    SetupEvents();
    DataContext = this;
  }

  private void SetupEvents() {
    FunctionListView.NodeClick += async (sender, treeNode) => {
      await Session.ProfileFunctionSelected(treeNode, ToolPanelKind.Other);
    };

    FunctionListView.NodeDoubleClick += async (sender, treeNode) => {
      var mode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent;
      await Session.OpenProfileFunction(treeNode, mode);
    };
  }

  protected override void SetPanelAccentColor(Color color) {
    ToolbarPanel.Background = ColorBrushes.GetBrush(color);
    PanelBorder.BorderBrush = ColorBrushes.GetBrush(color);
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
    FunctionListView.ShowSimpleList(list);
  }

  public void ShowFunctions(List<ProfileCallTreeNode> list,
                            FunctionNameFormatter nameFormatter) {
    ShowSimpleView = true;
    UpdatePopupWidth(MeasureMaxTextWidth(list, nameFormatter));
    FunctionListView.ShowSimpleList(list);
  }

  public void UpdateNode(ProfileCallTreeNode node) {
    CallTreeNode = CallTreeNodePanel.SetupNodeExtension(node, Session);

    if (!ShowSimpleView) {
      PanelHost.Show(CallTreeNode);
    }
  }

  public override bool ShouldStartDragging(MouseButtonEventArgs e) {
    base.ShouldStartDragging(e);

    if (e.LeftButton == MouseButtonState.Pressed && ToolbarPanel.IsMouseOver) {
      if (!IsDetached) {
        DetachPopup();
      }

      return true;
    }

    return false;
  }

  public override async void DetachPopup() {
    base.DetachPopup();
    await ExpandDetailsPanel();
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
      var textSize = Utils.MeasureString(funcName, defaultTextFont_, DefaultTextSize);
      maxTextWidth = Math.Max(maxTextWidth, textSize.Width);
    }

    return maxTextWidth;
  }

  private void UpdatePopupWidth(double maxTextWidth) {
    double margin = SystemParameters.VerticalScrollBarWidth;
    Width = Math.Max(MinPopupWidth, Math.Min(maxTextWidth, MaxPopupWidth));

    // Leave some space for the vertical scroll bar
    // to avoid having a horizontal one by default.
    FunctionListView.FunctionColumnWidth = Math.Max(MinPopupWidth - 2 * margin, Width - 2 * margin);
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    Session.UnregisterDetachedPanel(this);
    ClosePopup();
  }

  private void ExpandButton_OnClick(object sender, RoutedEventArgs e) {
    DetachPopup();
  }

  private async Task ExpandDetailsPanel() {
    if (!ShowSimpleView) {
      await PanelHost.ShowDetailsAsync();
    }

    MinWidth = InitialWidth;
    Width = Math.Max(Width, InitialWidth);
    Height = Math.Max(Height, InitialHeight);
    ShowResizeGrip = true;
    CanExpand = false;
  }
}