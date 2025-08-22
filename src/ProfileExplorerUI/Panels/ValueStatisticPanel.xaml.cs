// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProfileExplorerCore2;

namespace ProfileExplorer.UI;

public partial class ValueStatisticPanel : UserControl {
  private ValueStatistics valueStats_;
  private int factor_;

  public ValueStatisticPanel() {
    InitializeComponent();
    factor_ = 1;
  }

  public event EventHandler<List<IRTextFunction>> RangeSelected;

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
      int result = a.Item2.CompareTo(b.Item2);

      if (result != 0) {
        return result;
      }

      return a.Item1.Name.CompareTo(b.Item1.Name);
    });

    RangeSelected?.Invoke(this, funcList);
  }
}