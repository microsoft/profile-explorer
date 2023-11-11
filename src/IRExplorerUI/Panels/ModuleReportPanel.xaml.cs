// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IRExplorerUI.Document;
using IRExplorerCore;
using ProtoBuf;
using Aga.Controls.Tree;
using System.Windows.Media;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Windows.Data;
using IRExplorerUI.Utilities;

namespace IRExplorerUI {
    public partial class ModuleReportPanel : ToolPanelControl {
        public class FunctionEx {
            public IRTextFunction Function { get; set; }
            public string Name { get; set; }
            public string AlternateName { get; set; }
            public Brush TextColor { get; set; }
            public Brush BackColor { get; set; }
            public FunctionCodeStatistics Statistics { get; set; }
        }

        private ModuleReport report_;
        private IRTextSummary summary_;
        private ISession session_;

        public ModuleReportPanel(ISession session) {
            InitializeComponent();
            session_ = session;
        }

        //? TODO:
        //? - use buttons instead of slider
        //? - show function list on the right side
        //? - selecting distrib range shows functs

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
                listEx.Add(new FunctionEx() {
                    Function = func,
                    Name = func.Name,
                    TextColor = Brushes.Black,
                    Statistics = report_.StatisticsMap[func]
                });
            }

            var listView = new ListCollectionView(listEx);
            listView.Filter = FilterFunctionList;
            return listView;
        }

        private bool FilterFunctionList(object value) {
            var functionEx = (FunctionEx)value;
            var function = functionEx.Function;

            // Don't filter with less than 2 letters.
            //? TODO: FunctionFilter change should rather set a property with the trimmed text
            string text = FunctionFilter.Text.Trim();

            if (text.Length < 2) {
                return true;
            }

            // Search the function name.
            if ((App.Settings.SectionSettings.FunctionSearchCaseSensitive
                ? function.Name.Contains(text, StringComparison.Ordinal)
                : function.Name.Contains(text, StringComparison.OrdinalIgnoreCase))) {
                return true;
            }

            // Search the demangled name.
            if (!string.IsNullOrEmpty(functionEx.AlternateName)) {
                return (App.Settings.SectionSettings.FunctionSearchCaseSensitive
                    ? functionEx.AlternateName.Contains(text, StringComparison.Ordinal)
                    : functionEx.AlternateName.Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Other;

        #endregion

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
    }
}
