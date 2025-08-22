// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ProfileExplorerCore2;

namespace ProfileExplorer.UI;

public partial class ModuleReportPanel : ToolPanelControl {
  private ModuleReport report_;
  private IRTextSummary summary_;
  private ISession session_;

  public ModuleReportPanel(ISession session) {
    InitializeComponent();
    session_ = session;
  }

  public override ToolPanelKind PanelKind => ToolPanelKind.Other;

  public void ShowReport(ModuleReport report, IRTextSummary summary) {
    report_ = report;
    summary_ = summary;
    DataContext = report;

    SingleCallerExpander.DataContext = report.ComputeGroupStatistics(report.SingleCallerFunctions);
    LeafExpander.DataContext = report.ComputeGroupStatistics(report.LeafFunctions);

    CallGraphView.Session = session_;
    CallGraphView.OnRegisterPanel();
  }

  private void UpdateFunctionList(List<IRTextFunction> list) {
    FunctionList.ItemsSource = CreateFunctionList(list);
  }

  private ListCollectionView CreateFunctionList(List<IRTextFunction> list) {
    list.Sort((a, b) => a.Name.CompareTo(b.Name));
    var listEx = new List<FunctionEx>(list.Count);

    foreach (var func in list) {
      listEx.Add(new FunctionEx {
        Function = func,
        Name = func.Name,
        TextColor = Brushes.Black,
        Statistics = report_.StatisticsMap[func]
      });
    }

    return new ListCollectionView(listEx);
  }

  private void SingleCallListButton_Click(object sender, RoutedEventArgs e) {
    UpdateFunctionList(report_.SingleCallerFunctions);
  }

  private void ValueStatisticPanel_RangeSelected(object sender, List<IRTextFunction> e) {
    UpdateFunctionList(e);
  }

  private void LeafListButton_Click(object sender, RoutedEventArgs e) {
    UpdateFunctionList(report_.LeafFunctions);
  }

  private async Task DisplayCallGraph(IRTextFunction func) {
    var loadedDoc = session_.SessionState.FindLoadedDocument(summary_);
    var section = func.Sections[0];
    var layoutGraph = await Task.Run(() =>
                                       CallGraphUtils.BuildCallGraphLayout(summary_, section, loadedDoc,
                                                                           session_.CompilerInfo, true));
    CallGraphView.DisplayGraph(layoutGraph);
  }

  private void FunctionFilter_TextChanged(object sender, TextChangedEventArgs e) {
    RefreshFunctionList();
  }

  private void RefreshFunctionList() {
    if (FunctionList.ItemsSource == null) {
      return;
    }

    ((ListCollectionView)FunctionList.ItemsSource).Refresh();
  }

  private async void FunctionList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    var funcEx = FunctionList.SelectedItem as FunctionEx;

    if (funcEx == null) {
      return;
    }

    await Session.SwitchActiveFunction(funcEx.Function);
  }

  private async void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
    var funcEx = ((ListViewItem)sender).Content as FunctionEx;

    if (funcEx == null) {
      return;
    }

    var func = funcEx.Function;

    if (func.SectionCount == 0) {
      return;
    }

    await DisplayCallGraph(func);
  }

  public class FunctionEx {
    public IRTextFunction Function { get; set; }
    public string Name { get; set; }
    public string AlternateName { get; set; }
    public Brush TextColor { get; set; }
    public Brush BackColor { get; set; }
    public FunctionCodeStatistics Statistics { get; set; }
  }
}