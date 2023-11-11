// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
using DocumentFormat.OpenXml.Drawing.Diagrams;
using IRExplorerUI.Controls;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI.Profile;

public class ProfileListViewItem : SearchableProfileItem {
    private bool prependModule_;
    public ProfileCallTreeNode CallTreeNode { get; set; }

    public ProfileListViewItem(FunctionNameFormatter funcNameFormatter) :
        base(funcNameFormatter) {

    }

    protected override bool ShouldPrependModule() {
        return prependModule_ && App.Settings.CallTreeSettings.PrependModuleToFunction;
    }

    public static ProfileListViewItem From(ProfileCallTreeNode node, ProfileData profileData,
                                           FunctionNameFormatter funcNameFormatter) {
        return new ProfileListViewItem(funcNameFormatter) {
            //prependModule_ = true,
            CallTreeNode = node,
            FunctionName = node.FunctionName,
            ModuleName = node.ModuleName,
            Weight = node.Weight,
            ExclusiveWeight = node.ExclusiveWeight,
            Percentage = profileData.ScaleFunctionWeight(node.Weight),
            ExclusivePercentage = profileData.ScaleFunctionWeight(node.ExclusiveWeight),
        };
    }

    public static ProfileListViewItem From(ModuleProfileInfo node, ProfileData profileData,
                                           FunctionNameFormatter funcNameFormatter) {
        return new ProfileListViewItem(funcNameFormatter) {
            FunctionName = node.Name,
            Weight = node.Weight,
            Percentage = node.Percentage,
        };
    }
}


public partial class ProfileListView : UserControl, INotifyPropertyChanged {
    public ProfileListView() {
        InitializeComponent();
        DataContext = this;
    }

    public ISession Session { get; set; }
    public event PropertyChangedEventHandler PropertyChanged;
    public event EventHandler<ProfileCallTreeNode> NodeClick;
    public event EventHandler<ProfileCallTreeNode> NodeDoubleClick;

    private string nameColumnTitle_;
    public string NameColumnTitle {
        get => nameColumnTitle_;
        set => SetField(ref nameColumnTitle_, value);
    }

    private string timeColumnTitle_;
    public string TimeColumnTitle {
        get => timeColumnTitle_;
        set => SetField(ref timeColumnTitle_, value);
    }

    private string exclusiveTimeColumnTitle_;
    public string ExclusiveTimeColumnTitle {
        get => exclusiveTimeColumnTitle_;
        set => SetField(ref exclusiveTimeColumnTitle_, value);
    }

    private bool showExclusiveTimeColumn_;
    public bool ShowExclusiveTimeColumn {
        get => showExclusiveTimeColumn_;
        set => SetField(ref showExclusiveTimeColumn_, value);
    }

    private bool showTimeColumn_;
    public bool ShowTimeColumn {
        get => showTimeColumn_;
        set => SetField(ref showTimeColumn_, value);
    }

    private bool showCombinedTimeColumn_;
    public bool ShowCombinedTimeColumn {
        get => showCombinedTimeColumn_;
        set => SetField(ref showCombinedTimeColumn_, value);
    }

    private bool showCombinedTimeNameRow_;
    public bool ShowCombinedTimeNameRow {
        get => showCombinedTimeNameRow_;
        set => SetField(ref showCombinedTimeNameRow_, value);
    }

    private bool showExclusiveTimeNameRow_;
    public bool ShowExclusiveTimeNameRow {
        get => showExclusiveTimeNameRow_;
        set => SetField(ref showExclusiveTimeNameRow_, value);
    }

    private bool showTimeNameRow_;
    public bool ShowTimeNameRow {
        get => showTimeNameRow_;
        set => SetField(ref showTimeNameRow_, value);
    }

    private bool showModuleColumn_;
    public bool ShowModuleColumn {
        get => showModuleColumn_;
        set => SetField(ref showModuleColumn_, value);
    }

    private bool showContextColumn_;
    public bool ShowContextColumn {
        get => showContextColumn_;
        set => SetField(ref showContextColumn_, value);
    }


    public void Show(List<ProfileCallTreeNode> nodes, ProfileListViewFilter filter = null) {
        var filteredNodes = new List<ProfileCallTreeNode>();

        if (filter is { IsEnabled: true }) {
            if (filter.SortByExclusiveTime) {
                nodes.Sort((a, b) => b.ExclusiveWeight.CompareTo(a.ExclusiveWeight));
            }
            else {
                nodes.Sort((a, b) => b.Weight.CompareTo(a.Weight));
            }

            if (filter.FilterByWeight) {
                if (filter.SortByExclusiveTime) {
                    foreach(var node in nodes) {
                        if (node.ExclusiveWeight.TotalMilliseconds > filter.MinWeight) {
                            filteredNodes.Add(node);
                        }
                        else break;
                    }
                }
                else {
                    foreach(var node in nodes) {
                        if (node.Weight.TotalMilliseconds > filter.MinWeight) {
                            filteredNodes.Add(node);
                        }
                        else break;
                    }
                }
            }

            // Have at least MinItems functs, even if they are below the MinWeight threshold
            if (filteredNodes.Count < filter.MinItems && nodes.Count > filteredNodes.Count) {
                for(int i = filteredNodes.Count; i < nodes.Count && filteredNodes.Count < filter.MinItems; i++) {
                    filteredNodes.Add(nodes[i]);
                }
            }
        }
        else {
            filteredNodes = nodes;
        }

        var list = new List<ProfileListViewItem>(nodes.Count);
        filteredNodes.ForEach(node => list.Add(ProfileListViewItem.From(node, Session.ProfileData,
            Session.CompilerInfo.NameProvider.FormatFunctionName)));
        ItemList.ItemsSource = new ListCollectionView(list);
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