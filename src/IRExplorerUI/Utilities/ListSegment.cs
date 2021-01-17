// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;

namespace IRExplorerUI.Utilities {
    public class ListSegment<T> : IList<T> {
        private IList<T> source_;
        private int startIndex_;
        private int length_;
        public ListSegment(IList<T> source, int startIndex, int length) {
            source_ = source;
            startIndex_ = startIndex;
            length_ = length;
        }

        public int IndexOf(T item) {
            throw new NotImplementedException();
        }

        public void Insert(int index, T item) {
            throw new NotImplementedException();
        }

        public void RemoveAt(int index) {
            throw new NotImplementedException();
        }

        public T this[int i] {
            get => source_[startIndex_ + i];
            set => throw new NotImplementedException();
        }

        public void Add(T item) {
            throw new NotImplementedException();
        }

        public void Clear() {
            throw new NotImplementedException();
        }

        public bool Contains(T item) {
            throw new NotImplementedException();
        }

        public void CopyTo(T[] array, int arrayIndex) {
            throw new NotImplementedException();
        }

        public bool Remove(T item) {
            throw new NotImplementedException();
        }

        public int Count {
            get { return length_; }
        }

        public bool IsReadOnly { get; }

        public IEnumerator<T> GetEnumerator() {
            throw new NotImplementedException();
        }
        
        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }
    }
}