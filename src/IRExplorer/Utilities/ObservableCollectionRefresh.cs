// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Client {
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

        protected override void InsertItem(int index, T item) {
            base.InsertItem(index, item);
            Refresh();
        }

        protected override void RemoveItem(int index) {
            base.RemoveItem(index);
            Refresh();
        }
    }
}
