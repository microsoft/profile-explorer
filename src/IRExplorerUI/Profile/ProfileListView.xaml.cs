// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HtmlAgilityPack;
using IRExplorerUI.Controls;
using IRExplorerUI.Document;
using IRExplorerUI.Utilities;
using Microsoft.Diagnostics.Runtime;

namespace IRExplorerUI.Profile;

public class ProfileListViewItem : SearchableProfileItem {
  CallTreeNodeSettings settings_;
  private object associatedObject_;
  private Brush functionBackColor_;
  private Brush moduleBackColor_;
  private ProfileCallTreeNode callTreeNode;
  private ModuleProfileInfo moduleInfo;

  private ProfileListViewItem(FunctionNameFormatter funcNameFormatter,
                              CallTreeNodeSettings settings) :
    base(funcNameFormatter) {
    settings_ = settings;
  }

  public ProfileCallTreeNode CallTreeNode {
    get => associatedObject_ as ProfileCallTreeNode;
    set => associatedObject_ = value;
  }

  public ModuleProfileInfo ModuleInfo {
    get => associatedObject_ as ModuleProfileInfo;
    set => associatedObject_ = value;
  }

  public FunctionMarkingCategory MarkingCategory {
    get => associatedObject_ as FunctionMarkingCategory;
    set => associatedObject_ = value;
  }

  public Brush FunctionBackColor {
    get => functionBackColor_;
    set => SetAndNotify(ref functionBackColor_, value);
  }

  public Brush ModuleBackColor {
    get => moduleBackColor_;
    set => SetAndNotify(ref moduleBackColor_, value);
  }

  protected override string GetFunctionName() {
    return CallTreeNode?.FunctionName;
  }

  public static ProfileListViewItem From(ProfileCallTreeNode node, ProfileData profileData,
                                         FunctionNameFormatter funcNameFormatter,
                                         CallTreeNodeSettings settings) {
    return new ProfileListViewItem(funcNameFormatter, settings) {
      CallTreeNode = node,
      ModuleName = node.ModuleName,
      Weight = node.Weight,
      ExclusiveWeight = node.ExclusiveWeight,
      Percentage = profileData.ScaleFunctionWeight(node.Weight),
      ExclusivePercentage = profileData.ScaleFunctionWeight(node.ExclusiveWeight)
    };
  }

  public static ProfileListViewItem From(ModuleProfileInfo moduleInfo, ProfileData profileData,
                                         FunctionNameFormatter funcNameFormatter,
                                         CallTreeNodeSettings settings) {
    return new ProfileListViewItem(funcNameFormatter, settings) {
      ModuleInfo = moduleInfo,
      FunctionName = moduleInfo.Name, // Override name, disables GetFunctionName.
      Weight = moduleInfo.Weight,
      Percentage = moduleInfo.Percentage
    };
  }

  public static ProfileListViewItem From(FunctionMarkingCategory category, ProfileData profileData,
                                         FunctionNameFormatter funcNameFormatter,
                                         CallTreeNodeSettings settings) {
    return new ProfileListViewItem(funcNameFormatter, settings) {
      MarkingCategory = category,
      FunctionName = category.Marking.Title, // Override name, disables GetFunctionName.
      Weight = category.Weight,
      Percentage = category.Percentage
    };
  }

  protected override bool ShouldPrependModule() {
    return settings_.PrependModuleToFunction;
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
  private IRDocumentPopupInstance previewPopup_;
  private CallTreeNodeSettings settings_;
  private bool searchPanelVisible_;
  private List<ProfileListViewItem> itemList_;
  private List<ProfileListViewItem> resultList_;
  private List<FunctionMarkingCategory> categories_;
  private bool isCategoriesList_;

  public ProfileListView() {
    InitializeComponent();
    FunctionColumnWidth = DefaultFunctionColumnWidth;
    DataContext = this;

    SearchPanel.SearchChanged += SearchPanel_SearchChanged;
    SearchPanel.CloseSearchPanel += SearchPanel_CloseSearchPanel;
    SearchPanel.NavigateToPreviousResult += SearchPanel_NaviateToPreviousResult;
    SearchPanel.NavigateToNextResult += SearchPanel_NavigateToNextResult;
  }

  private void SearchPanel_NavigateToNextResult(object sender, SearchInfo e) {
    SelectSearchResult(e.CurrentResult);
  }

  private void SearchPanel_NaviateToPreviousResult(object sender, SearchInfo e) {
    SelectSearchResult(e.CurrentResult);
  }

  private void SearchPanel_CloseSearchPanel(object sender, SearchInfo e) {
    SearchPanelVisible = false;
    resultList_ = null;
  }

  private void SearchPanel_SearchChanged(object sender, SearchInfo e) {
    if (itemList_ == null) {
      return;
    }

    resultList_ = new List<ProfileListViewItem>();

    foreach (var item in itemList_) {
      var result = TextSearcher.FirstIndexOf(item.FunctionName, e.SearchedText, 0,
                                             e.SearchKind);

      if (result.HasValue) {
        item.SearchResult = result;
        item.ResetCachedName();
        resultList_.Add(item);
      }
      else {
        item.SearchResult = null;
        item.ResetCachedName();
      }
    }

    e.CurrentResult = 0;
    e.ResultCount = resultList_.Count;
  }

  private void SelectSearchResult(int index) {
    if (resultList_ == null) {
      return;
    }

    ItemList.SelectedItem = resultList_[index];
    ItemList.ScrollIntoView(ItemList.SelectedItem);
  }

  ~ProfileListView() {
    previewPopup_?.UnregisterHoverEvents();
  }

  public CallTreeNodeSettings Settings {
    get => settings_;
    set {
      settings_ = value;
      OnPropertyChanged();

      if (settings_ != null && session_ != null) {
        SetupPreviewPopup();
      }
    }
  }

  public ISession Session {
    get => session_;
    set => session_ = value;
  }

  public double FunctionColumnWidth {
    get => functionColumnWidth_;
    set => SetField(ref functionColumnWidth_, value);
  }

  public bool IsCategoriesList {
    get => isCategoriesList_;
    set => SetField(ref isCategoriesList_, value);
  }

  private void SetupPreviewPopup() {
    if (previewPopup_ != null) {
      previewPopup_.UnregisterHoverEvents();
      previewPopup_ = null;
    }

    if(!Settings.ShowPreviewPopup) {
      return;
    }

    previewPopup_ = new IRDocumentPopupInstance(App.Settings.PreviewPopupSettings, Session);
    previewPopup_.SetupHoverEvents(ItemList, TimeSpan.FromMilliseconds(Settings.PreviewPopupDuration), () => {
      var hoveredItem = Utils.FindPointedListViewItem(ItemList);
      if (hoveredItem == null)
        return null;

      var item = (ProfileListViewItem)hoveredItem.DataContext;

      if (item.CallTreeNode != null) {
        return PreviewPopupArgs.ForFunction(item.CallTreeNode.Function, ItemList);
      }

      return null;
    });
  }

  public event EventHandler<ModuleProfileInfo> ModuleClick;
  public event EventHandler<FunctionMarkingCategory> CategoryClick;
  public event EventHandler<ProfileCallTreeNode> NodeClick;
  public event EventHandler<ProfileCallTreeNode> NodeDoubleClick;
  public event EventHandler MarkingChanged;
  public event PropertyChangedEventHandler PropertyChanged;

  public RelayCommand<object> PreviewFunctionCommand => new RelayCommand<object>(async obj => {
    if (ItemList.SelectedItem is ProfileListViewItem item && item.CallTreeNode != null) {
      var brush = GetMarkedNodeColor(item);
      await IRDocumentPopupInstance.ShowPreviewPopup(item.CallTreeNode.Function, "",
                                                     ItemList, session_, null, false, brush);
    }
  });

  private Brush GetMarkedNodeColor(ProfileListViewItem node) {
    return App.Settings.MarkingSettings.
      GetMarkedNodeBrush(node.FunctionName, node.ModuleName);
  }

  public RelayCommand<object> OpenFunctionCommand => new RelayCommand<object>(async obj => {
    var mode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent;
    await OpenFunction(mode);
  });
  public RelayCommand<object> OpenFunctionInNewTabCommand => new RelayCommand<object>(async obj => {
    await OpenFunction(OpenSectionKind.NewTabDockRight);
  });

  private async Task OpenFunction(OpenSectionKind openMode) {
    if (ItemList.SelectedItem is ProfileListViewItem item && item.CallTreeNode != null) {
      await Session.OpenProfileFunction(item.CallTreeNode, openMode);
    }
  }
  public RelayCommand<object> PreviewFunctionInstanceCommand => new RelayCommand<object>(async obj => {
    if (ItemList.SelectedItem is ProfileListViewItem item && item.CallTreeNode != null) {
      var filter = new ProfileSampleFilter(item.CallTreeNode);
      await IRDocumentPopupInstance.ShowPreviewPopup(item.CallTreeNode.Function, "",
                                                     ItemList, session_, filter);
    }
  });
  public RelayCommand<object> OpenInstanceCommand => new RelayCommand<object>(async obj => {
    var mode = Utils.IsShiftModifierActive() ? OpenSectionKind.NewTab : OpenSectionKind.ReplaceCurrent;
    await OpenFunction(mode);
  });
  public RelayCommand<object> OpenInstanceInNewTabCommand => new RelayCommand<object>(async obj => {
    await OpenFunctionInstance(OpenSectionKind.NewTabDockRight);
  });

  private async Task OpenFunctionInstance(OpenSectionKind openMode) {
    if (ItemList.SelectedItem is ProfileListViewItem item && item.CallTreeNode != null) {
      var filter = new ProfileSampleFilter(item.CallTreeNode);
      await Session.OpenProfileFunction(item.CallTreeNode, openMode, filter);
    }
  }

  public RelayCommand<object> SelectFunctionSummaryCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(ToolPanelKind.Section);
  });
  public RelayCommand<object> SelectFunctionCallTreeCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(ToolPanelKind.CallTree);
  });
  public RelayCommand<object> SelectFunctionFlameGraphCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(ToolPanelKind.FlameGraph);
  });
  public RelayCommand<object> SelectFunctionTimelineCommand => new RelayCommand<object>(async obj => {
    await SelectFunctionInPanel(ToolPanelKind.Timeline);
  });
  public RelayCommand<object> ToggleSearchCommand => new RelayCommand<object>(async obj => {
    SearchPanelVisible = !SearchPanelVisible;
  });
  public RelayCommand<object> CopyFunctionNameCommand => new RelayCommand<object>(async obj => {
    if (ItemList.SelectedItem is ProfileListViewItem item) {
      string text = Session.CompilerInfo.NameProvider.GetFunctionName(item.CallTreeNode.Function);
      Clipboard.SetText(text);
    }
  });
  public RelayCommand<object> CopyDemangledFunctionNameCommand => new RelayCommand<object>(async obj => {
    if (ItemList.SelectedItem is ProfileListViewItem item) {
      var options = FunctionNameDemanglingOptions.Default;
      string text = Session.CompilerInfo.NameProvider.DemangleFunctionName(item.CallTreeNode.Function, options);
      Clipboard.SetText(text);
    }
  });
  public RelayCommand<object> CopyFunctionDetailsCommand => new RelayCommand<object>(async obj => {
    if (ItemList.SelectedItems.Count > 0) {
      var funcList = new List<SearchableProfileItem>();

      foreach (var item in ItemList.SelectedItems) {
        funcList.Add((SearchableProfileItem)item);
      }

      SearchableProfileItem.CopyFunctionListAsHtml(funcList);
    }
  });

  public RelayCommand<object> MarkModuleCommand => new RelayCommand<object>(async obj => {
    var markingSettings = App.Settings.MarkingSettings;

    foreach (var item in ItemList.SelectedItems) {
      if (item is ProfileListViewItem profileItem &&
          obj is SelectedColorEventArgs e) {
        markingSettings.AddModuleColor(profileItem.ModuleName, e.SelectedColor);
      }
    }

    markingSettings.UseModuleColors = true;
    UpdateMarkedFunctions();
    MarkingChanged?.Invoke(this, EventArgs.Empty);
  });

  public RelayCommand<object> MarkFunctionCommand => new RelayCommand<object>(async obj => {
    var markingSettings = App.Settings.MarkingSettings;

    foreach (var item in ItemList.SelectedItems) {
      if (item is ProfileListViewItem profileItem &&
          obj is SelectedColorEventArgs e) {
        markingSettings.AddFunctionColor(profileItem.FunctionName, e.SelectedColor);
      }
    }

    markingSettings.UseFunctionColors = true;
    UpdateMarkedFunctions();
    MarkingChanged?.Invoke(this, EventArgs.Empty);
  });

  public RelayCommand<object> CopyCategoriesCommand => new RelayCommand<object>(async obj => {
    if (categories_ != null) {
      ProfilingExporting.CopyFunctionMarkingsAsHtml(categories_, Session);
    }
  });

  public RelayCommand<object> ExportCategoriesHtmlCommand => new RelayCommand<object>(async obj => {
    if (categories_ != null) {
      ProfilingExporting.ExportFunctionMarkingsAsHtmlFile(categories_, Session);
    }
  });

  public RelayCommand<object> ExportCategoriesMarkdownCommand => new RelayCommand<object>(async obj => {
    if (categories_ != null) {
      ProfilingExporting.ExportFunctionMarkingsAsMarkdownFile(categories_, Session);
    }
  });

  private async Task SelectFunctionInPanel(ToolPanelKind panelKind) {
    if (ItemList.SelectedItem is ProfileListViewItem item) {
      await Session.SelectProfileFunctionInPanel(item.CallTreeNode, panelKind);
    }
  }

  public bool SearchPanelVisible {
    get => searchPanelVisible_;
    set {
      if (value != searchPanelVisible_) {
        searchPanelVisible_ = value;

        if (searchPanelVisible_) {
          SearchPanel.Visibility = Visibility.Visible;
          SearchPanel.Show();
        }
        else {
          SearchPanel.Reset();
          SearchPanel.Visibility = Visibility.Collapsed;
        }

        OnPropertyChanged();
      }
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
    ShowFunctions(nodes);

    //? TODO: Hack for what looks like a WPF bug where the
    // ProfileListView columns visibility is not read from the property
    // when in a popup and the popup was first created.
    GridViewColumnVisibility.RemoveAllColumnsExcept("FunctionColumnHeader", ItemList);
  }

  public void ShowFunctions(List<ProfileCallTreeNode> nodes, ProfileListViewFilter filter = null) {
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

    itemList_ = new List<ProfileListViewItem>(nodes.Count);
    filteredNodes.ForEach(node => itemList_.Add(ProfileListViewItem.From(node, Session.ProfileData,
                                                                         Session.CompilerInfo.NameProvider.FormatFunctionName,
                                                                         Settings)));
    UpdateMarkedFunctions();
    ItemList.ItemsSource = itemList_;
    GridViewColumnVisibility.UpdateListView(ItemList);
  }

  public void ShowModules(List<ModuleProfileInfo> modules) {
    itemList_ = new List<ProfileListViewItem>(modules.Count);
    modules.ForEach(node => itemList_.Add(ProfileListViewItem.From(node, Session.ProfileData,
                                                                 Session.CompilerInfo.NameProvider.FormatFunctionName,
                                                                 Settings)));
    UpdateMarkedFunctions();
    ItemList.ItemsSource = new ListCollectionView(itemList_);
    ItemList.ContextMenu = null;
  }

  public void ShowCategories(List<FunctionMarkingCategory> categories) {
    itemList_ = new List<ProfileListViewItem>(categories.Count);

    foreach (var node in categories) {
      if(node.SortedFunctions.Count == 0) {
        continue;
      }

      itemList_.Add(ProfileListViewItem.From(node, Session.ProfileData,
        Session.CompilerInfo.NameProvider.FormatFunctionName, Settings));
    }

    UpdateMarkedFunctions();
    ItemList.ItemsSource = new ListCollectionView(itemList_);
    IsCategoriesList = true;
    categories_ = categories;
  }

  public void UpdateMarkedFunctions() {
    if (itemList_ == null) {
      return; // Control not being used.
    }

    var fgSettings = App.Settings.MarkingSettings;

    foreach (var f in itemList_) {
      f.ModuleBackColor = null;
      f.FunctionBackColor = null;
    }

    if (!fgSettings.UseAutoModuleColors &&
        !fgSettings.UseModuleColors &&
        !fgSettings.UseFunctionColors) {
      return;
    }

    foreach (var item in itemList_) {
      if (item.ModuleName == null || item.FunctionName == null) {
        continue;
      }

      if (fgSettings.UseModuleColors &&
          fgSettings.GetModuleBrush(item.ModuleName, out var brush)) {
        item.ModuleBackColor = brush;
      }
      else if (fgSettings.UseAutoModuleColors) {
        item.ModuleBackColor = fgSettings.GetAutoModuleBrush(item.ModuleName);
      }

      if (fgSettings.UseFunctionColors &&
          fgSettings.GetFunctionColor(item.FunctionName, out var color)) {
        item.FunctionBackColor = color.AsBrush();
      }
    }
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
    else if (node?.ModuleInfo != null) {
      ModuleClick?.Invoke(this, node.ModuleInfo);
    }
    else if (node?.MarkingCategory != null) {
      CategoryClick?.Invoke(this, node.MarkingCategory);
    }

    // Show the sum of the selected functions.
    if (ItemList.SelectedItems.Count > 1) {
      var selectedNodes = new List<ProfileCallTreeNode>();

      foreach (var item in ItemList.SelectedItems) {
        if (item is ProfileListViewItem profileItem && profileItem.CallTreeNode != null) {
          selectedNodes.Add(profileItem.CallTreeNode);
        }
      }

      var weightSum = ProfileCallTree.CombinedCallTreeNodesWeight(selectedNodes);
      double weightPercentage = Session.ProfileData.ScaleFunctionWeight(weightSum);
      string text = $"Selected {ItemList.SelectedItems.Count}: {weightPercentage.AsPercentageString()} ({weightSum.AsMillisecondsString()})";
      Session.SetApplicationStatus(text, "Sum of time for the selected functions");
    }
    else {
      Session.SetApplicationStatus("");
    }
  }

  public void Reset() {
    ItemList.ItemsSource = null;
    itemList_ = null;
    categories_ = null;
  }

  public void SelectFirstItem() {
    if (ItemList.Items.Count > 0) {
      ItemList.SelectedIndex = 0;
    }
  }
}