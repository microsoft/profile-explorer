﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;

namespace ProfileExplorer.UI;

public class ObservableCollectionRefresh<T> : ObservableCollection<T> {
  public ObservableCollectionRefresh() { }
  public ObservableCollectionRefresh(List<T> inputValues) : base(inputValues) { }

  public void Refresh() {
    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
  }

  public void AddRange(List<T> values) {
    foreach (var value in values) {
      Add(value);
    }

    Refresh();
  }

  public ICollectionView GetFilterView() {
    return CollectionViewSource.GetDefaultView(this);
  }

  protected override void InsertItem(int index, T item) {
    base.InsertItem(index, item);
    Refresh();
  }

  protected override void RemoveItem(int index) {
    base.RemoveItem(index);
    Refresh();
  }
}