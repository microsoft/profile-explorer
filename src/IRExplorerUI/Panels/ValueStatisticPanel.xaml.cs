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

namespace IRExplorerUI {
    public partial class ValueStatisticPanel : UserControl {
        private int factor_;

        public ValueStatisticPanel() {
            InitializeComponent();
            factor_ = 1;
        }

        private void Expander_Expanded(object sender, RoutedEventArgs e) {
            UpdateDistribution(factor_);
        }

        private void UpdateDistribution(int factor) {
            var valueStats = (ValueStatistics)DataContext;
            var distribList = valueStats.ComputeDistribution(factor);
            DistributionList.ItemsSource = new ListCollectionView(distribList);
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (DataContext != null) {
                factor_ = (int)e.NewValue;
                UpdateDistribution(factor_);
            }
        }
    }
}
