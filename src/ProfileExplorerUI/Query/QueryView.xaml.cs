// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ProfileExplorer.UI.Query;

public partial class QueryView : UserControl {
  private QueryDefinition query_;

  public QueryView() {
    InitializeComponent();
    DataContextChanged += QueryView_DataContextChanged;
  }

  public event EventHandler Closed;

  public QueryDefinition Query {
    get => query_;
    set {
      if (value != query_) {
        query_ = value;
        DataContext = query_;
        InputElementList.ItemsSource = new CollectionView(query_.Data.InputValues);
        OutputElementList.ItemsSource = new CollectionView(query_.Data.OutputValues);
      }
    }
  }

  private void QueryView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
    if (e.OldValue != null) {
      Closed -= ((ElementQueryInfoView)e.OldValue).OnClose;
    }

    if (e.NewValue != null) {
      Closed += ((ElementQueryInfoView)e.NewValue).OnClose;
    }
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) {
    Closed?.Invoke(this, null);
  }
}