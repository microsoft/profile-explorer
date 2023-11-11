// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
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
using IRExplorerCore;

namespace IRExplorerUI {
    public partial class ValueStatisticPanel : UserControl {
        private ValueStatistics valueStats_;
        private int factor_;

        public event EventHandler<List<IRTextFunction>> RangeSelected;

        public ValueStatisticPanel() {
            InitializeComponent();
            factor_ = 1;
        }

        private void Expander_Expanded(object sender, RoutedEventArgs e) {
            valueStats_ = (ValueStatistics)DataContext;
            FactorSlider.Maximum = valueStats_.MaxDistributionFactor;
            UpdateDistribution(factor_);
        }

        private void UpdateDistribution(int factor) {
            var distribList = valueStats_.ComputeDistribution(factor);
            DistributionList.ItemsSource = new ListCollectionView(distribList);
            GroupSizeLabel.Text = valueStats_.GetGroupSize(factor).ToString();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (valueStats_ != null) {
                factor_ = (int)e.NewValue;
                UpdateDistribution(factor_);
            }
        }

        private void DistributionList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            var range = DistributionList.SelectedItem as ValueStatistics.DistributionRange;

            if (range == null) {
                return;
            }

            var funcList = new List<IRTextFunction>(range.Values.Count);

            foreach (var value in range.Values) {
                funcList.Add(value.Item1);
            }

            range.Values.Sort((a, b) => {
                var result = a.Item2.CompareTo(b.Item2);
                if (result != 0) {
                    return result;
                }

                return a.Item1.Name.CompareTo(b.Item1.Name);
            });

            RangeSelected?.Invoke(this, funcList);
        }
    }
}
