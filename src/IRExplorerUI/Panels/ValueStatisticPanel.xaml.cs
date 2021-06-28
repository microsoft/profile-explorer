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

        private void FunctionDoubleClick(object sender, MouseButtonEventArgs e) {
            var range = ((ListViewItem)sender).Content as ValueStatistics.DistributionRange;
            var funcList = new List<IRTextFunction>(range.Values.Count);
            
            foreach(var value in range.Values) {
                funcList.Add(value.Item1);
            }

            RangeSelected?.Invoke(this, funcList);
        }
    }
}
