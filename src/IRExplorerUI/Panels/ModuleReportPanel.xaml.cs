// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

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

        public ModuleReportPanel() {
            InitializeComponent();
        }

        public void ShowReport(ModuleReport report) {
            report_= report;
            DataContext = report;

            SingleCallerFunctionList.ItemsSource = CreateFunctionList(report_.SingleCallerFunctions);
            SingleCallerExpander.DataContext = report.ComputeGroupStatistics(report.SingleCallerFunctions);

            LeafExpander.DataContext = report.ComputeGroupStatistics(report.LeafFunctions);
        }

        private ListCollectionView CreateFunctionList(List<IRTextFunction> list) {
            var listEx = new List<FunctionEx>(list.Count);

            foreach(var func in list) {
                listEx.Add(new FunctionEx() {
                    Function = func,
                    Name = func.Name,
                    TextColor = Brushes.Black,
                    Statistics = report_.StatisticsMap[func]
                });
            }
            
            return new ListCollectionView(listEx);
        }

        private void ToolBar_Loaded(object sender, RoutedEventArgs e) {
            Utils.PatchToolbarStyle(sender as ToolBar);
        }

        #region IToolPanel

        public override ToolPanelKind PanelKind => ToolPanelKind.Other;

        #endregion
    }
}
