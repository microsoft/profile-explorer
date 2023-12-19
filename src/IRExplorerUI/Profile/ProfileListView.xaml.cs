// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using IRExplorerUI.Controls;
using IRExplorerUI.Utilities;
using Irony.Parsing.Construction;

namespace IRExplorerUI.Profile;

public class ProfileListViewItem : SearchableProfileItem {
  private bool prependModule_;

  public ProfileListViewItem(FunctionNameFormatter funcNameFormatter) :
    base(funcNameFormatter) {
  }

  public ProfileCallTreeNode CallTreeNode { get; set; }

  protected override string GetFunctionName() {
    return CallTreeNode?.FunctionName;
  }

  public static ProfileListViewItem From(ProfileCallTreeNode node, ProfileData profileData,
                                         FunctionNameFormatter funcNameFormatter) {
    return new ProfileListViewItem(funcNameFormatter) {
      //prependModule_ = true,
      CallTreeNode = node,
      ModuleName = node.ModuleName,
      Weight = node.Weight,
      ExclusiveWeight = node.ExclusiveWeight,
      Percentage = profileData.ScaleFunctionWeight(node.Weight),
      ExclusivePercentage = profileData.ScaleFunctionWeight(node.ExclusiveWeight)
    };
  }

  public static ProfileListViewItem From(ModuleProfileInfo node, ProfileData profileData,
                                         FunctionNameFormatter funcNameFormatter) {
    return new ProfileListViewItem(funcNameFormatter) {
      FunctionName = node.Name, // Override name, disables GetFunctionName.
      Weight = node.Weight,
      Percentage = node.Percentage
    };
  }

  protected override bool ShouldPrependModule() {
    return prependModule_ && App.Settings.CallTreeSettings.PrependModuleToFunction;
  }
}

public partial class ProfileListView : UserControl, INotifyPropertyChanged {
  private const double DefaultFunctionColumnWidth = 250;
  private string nameColumnTitle_;
  private string timeColumnTitle_;
  private string exclusiveTimeColumnTitle_;
  private bool showExclusiveTimeColumn_;
  private bool showTimeColumn_;
  private bool showCombinedTimeColumn_;
  private bool showCombinedTimeNameRow_;
  private bool showExclusiveTimeNameRow_;
  private bool showTimeNameRow_;
  private bool showModuleColumn_;
  private bool showContextColumn_;
  private double functionColumnWidth_;
  private ISession session_;

  public double FunctionColumnWidth {
    get => functionColumnWidth_;
    set => SetField(ref functionColumnWidth_, value);
  }

  public ProfileListView() {
    InitializeComponent();
    FunctionColumnWidth = DefaultFunctionColumnWidth;
    DataContext = this;
  }

  private void SetupPreviewPopup() {
    var preview = new IRDocumentPopupInstance(IRDocumentPopup.DefaultWidth,
                                              IRDocumentPopup.DefaultHeight * 2, Session);
    preview.SetupHoverEvents(ItemList, HoverPreview.LongHoverDuration, () => {
      var hoveredItem = Utils.FindPointedListViewItem(ItemList);

      if (hoveredItem != null) {
        var item = (ProfileListViewItem)hoveredItem.DataContext;

        if (item.CallTreeNode != null) {
          return PreviewPopupArgs.ForFunction(item.CallTreeNode.Function, ItemList,
                                              $"Function {item.CallTreeNode.FunctionName}");
        }
      }

      return null;
    });
  }

  public event EventHandler<ProfileCallTreeNode> NodeClick;
  public event EventHandler<ProfileCallTreeNode> NodeDoubleClick;
  public event PropertyChangedEventHandler PropertyChanged;

  public ISession Session {
    get => session_;
    set {
      session_ = value;

      //? TODO: UI option
      if (session_ != null) {
        SetupPreviewPopup();
      }
    }
  }

  public RelayCommand<object> OpenFunctionCommand => new RelayCommand<object>(async obj => {
    await OpenFunction(OpenSectionKind.ReplaceCurrent);
  });
  public RelayCommand<object> OpenFunctionInNewTabCommand => new RelayCommand<object>(async obj => {
    await OpenFunction(OpenSectionKind.NewTab);
  });

  private async Task OpenFunction(OpenSectionKind openMode) {
    if (ItemList.SelectedItem is ProfileListViewItem item && item.CallTreeNode != null) {
      await Session.OpenProfileFunction(item.CallTreeNode, openMode);
    }
  }

  public RelayCommand<object> SelectFunctionSummaryCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(ToolPanelKind.Section);
  });
  public RelayCommand<object> SelectFunctionCallTreeCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(ToolPanelKind.CallTree);
  });
  public RelayCommand<object> SelectFunctionTimelineCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(ToolPanelKind.Timeline);
  });

  private async Task SelectFunctionInPanel(ToolPanelKind panelKind) {
    if (ItemList.SelectedItem is ProfileListViewItem item) {
      await Session.SelectProfileFunctionInPanel(item.CallTreeNode, panelKind);
    }
  }

  public string NameColumnTitle {
    get => nameColumnTitle_;
    set => SetField(ref nameColumnTitle_, value);
  }

  public string TimeColumnTitle {
    get => timeColumnTitle_;
    set => SetField(ref timeColumnTitle_, value);
  }

  public string ExclusiveTimeColumnTitle {
    get => exclusiveTimeColumnTitle_;
    set => SetField(ref exclusiveTimeColumnTitle_, value);
  }

  public bool ShowExclusiveTimeColumn {
    get => showExclusiveTimeColumn_;
    set => SetField(ref showExclusiveTimeColumn_, value);
  }

  public bool ShowTimeColumn {
    get => showTimeColumn_;
    set => SetField(ref showTimeColumn_, value);
  }

  public bool ShowCombinedTimeColumn {
    get => showCombinedTimeColumn_;
    set => SetField(ref showCombinedTimeColumn_, value);
  }

  public bool ShowCombinedTimeNameRow {
    get => showCombinedTimeNameRow_;
    set => SetField(ref showCombinedTimeNameRow_, value);
  }

  public bool ShowExclusiveTimeNameRow {
    get => showExclusiveTimeNameRow_;
    set => SetField(ref showExclusiveTimeNameRow_, value);
  }

  public bool ShowTimeNameRow {
    get => showTimeNameRow_;
    set => SetField(ref showTimeNameRow_, value);
  }

  public bool ShowModuleColumn {
    get => showModuleColumn_;
    set => SetField(ref showModuleColumn_, value);
  }

  public bool ShowContextColumn {
    get => showContextColumn_;
    set => SetField(ref showContextColumn_, value);
  }

  public void ShowSimpleList(List<ProfileCallTreeNode> nodes) {
    Show(nodes);

    //? TODO: Hack for what looks like a WPF bug where the
    // ProfileListView columns visibility is not read from the property
    // when in a popup and the popup was first created.
    GridViewColumnVisibility.RemoveAllColumnsExcept("FunctionColumnHeader", ItemList);
  }

  public void Show(List<ProfileCallTreeNode> nodes, ProfileListViewFilter filter = null) {
    var filteredNodes = new List<ProfileCallTreeNode>();

    if (filter is {IsEnabled: true}) {
      if (filter.SortByExclusiveTime) {
        nodes.Sort((a, b) => b.ExclusiveWeight.CompareTo(a.ExclusiveWeight));
      }
      else {
        nodes.Sort((a, b) => b.Weight.CompareTo(a.Weight));
      }

      if (filter.FilterByWeight) {
        if (filter.SortByExclusiveTime) {
          foreach (var node in nodes) {
            if (node.ExclusiveWeight.TotalMilliseconds > filter.MinWeight) {
              filteredNodes.Add(node);
            }
            else break;
          }
        }
        else {
          foreach (var node in nodes) {
            if (node.Weight.TotalMilliseconds > filter.MinWeight) {
              filteredNodes.Add(node);
            }
            else break;
          }
        }
      }

      // Have at least MinItems functs, even if they are below the MinWeight threshold
      if (filteredNodes.Count < filter.MinItems && nodes.Count > filteredNodes.Count) {
        for (int i = filteredNodes.Count; i < nodes.Count && filteredNodes.Count < filter.MinItems; i++) {
          filteredNodes.Add(nodes[i]);
        }
      }
    }
    else {
      filteredNodes = nodes;
    }

    var list = new List<ProfileListViewItem>(nodes.Count);
    filteredNodes.ForEach(node => list.Add(ProfileListViewItem.From(node, Session.ProfileData,
                                                                    Session.CompilerInfo.NameProvider.
                                                                      FormatFunctionName)));
    ItemList.ItemsSource = new ListCollectionView(list);
    GridViewColumnVisibility.UpdateListView(ItemList);
  }

  public void Show(List<ModuleProfileInfo> nodes) {
    var list = new List<ProfileListViewItem>(nodes.Count);
    nodes.ForEach(node => list.Add(ProfileListViewItem.From(node, Session.ProfileData,
                                                            Session.CompilerInfo.NameProvider.FormatFunctionName)));
    ItemList.ItemsSource = new ListCollectionView(list);
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

  private void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
    var node = ((ListViewItem)sender).Content as ProfileListViewItem;

    if (node?.CallTreeNode != null) {
      NodeDoubleClick?.Invoke(this, node.CallTreeNode);
    }
  }

  private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    var node = ItemList.SelectedItem as ProfileListViewItem;

    if (node?.CallTreeNode != null) {
      NodeClick?.Invoke(this, node.CallTreeNode);
    }
  }
}