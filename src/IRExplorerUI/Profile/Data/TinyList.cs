﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace IRExplorerUI.Profile;

struct TinyList<T> : IList<T> {
  private object value_; // Either T or array of T.
  private int count_;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public TinyList() {
    value_ = null;
    count_ = 0;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public TinyList(T item) {
    value_ = item;
    count_ = 1;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public TinyList(IList<T> list) {
    if (list == null || list.Count == 0) {
      value_ = null;
      count_ = 0;
    }
    else if (list.Count == 1) {
      value_ = list[0];
      count_ = 1;
    }
    else {
      var array = new T[list.Count];
      list.CopyTo(array, 0);
      value_ = array;
      count_ = list.Count;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public IEnumerator<T> GetEnumerator() {
    if (count_ == 0) {
      yield break;
    }

    if (count_ == 1) {
      yield return (T)value_;
    }
    else {
      var array = (T[])value_;

      for (int i = 0; i < count_; i++) {
        yield return array[i];
      }
    }
  }

  IEnumerator IEnumerable.GetEnumerator() {
    return GetEnumerator();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Add(T item) {
    if (count_ == 0) {
      value_ = item;
      count_ = 1;
    }
    else if (count_ == 1) {
      var array = new T[2];
      array[0] = (T)value_;
      array[1] = item;
      value_ = array;
      count_ = 2;
    }
    else {
      var array = (T[])value_;

      if (array.Length == count_) {
        var newArray = new T[array.Length * 2];
        array.CopyTo(newArray, 0);
        value_ = array = newArray;
      }

      array[count_] = item;
      count_++;
    }
  }

  public void Clear() {
    value_ = null;
    count_ = 0;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool Contains(T item) {
    return IndexOf(item) != -1;
  }

  public void CopyTo(T[] array, int arrayIndex) {
    throw new NotImplementedException();
  }

  public bool Remove(T item) {
    throw new NotImplementedException();
  }

  public int Count => count_;
  public bool IsReadOnly => false;

  public int IndexOf(T item) {
    if (count_ == 0) {
      return -1;
    }

    if (count_ == 1) {
      if (EqualityComparer<T>.Default.Equals((T)value_, item)) {
        return 0;
      }
    }
    else {
      var array = (T[])value_;

      for (int i = 0; i < count_; i++) {
        if (EqualityComparer<T>.Default.Equals(array[i], item)) {
          return i;
        }
      }
    }

    return -1;
  }

  public void Insert(int index, T item) {
    throw new NotImplementedException();
  }

  public void RemoveAt(int index) {
    throw new NotImplementedException();
  }

  public T this[int index] {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get {
#if DEBUG
      if (index >= count_) {
        throw new IndexOutOfRangeException();
      }
#endif
      if (count_ == 1) {
        return (T)value_;
      }

      var array = (T[])value_;
      return array[index];
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set {
#if DEBUG
      if (index >= count_) {
        throw new IndexOutOfRangeException();
      }
#endif
      if (count_ == 1) {
        value_ = value;
      }
      else {
        var array = (T[])value_;
        array[index] = value;
      }
    }
  }
}
