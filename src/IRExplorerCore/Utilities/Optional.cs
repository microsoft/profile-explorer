// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Diagnostics;

namespace IRExplorerCore;

public struct Optional<T> : IEquatable<Optional<T>> {
  private T value_;

  public bool HasValue { get; private set; }

  public T Value {
    get {
      Debug.Assert(HasValue);
      return value_;
    }
    set {
      value_ = value;
      HasValue = true;
    }
  }

  public Optional(T value) {
    value_ = value;
    HasValue = true;
  }

  public static explicit operator T(Optional<T> optional) {
    return optional.Value;
  }

  public static implicit operator Optional<T>(T value) {
    return new Optional<T>(value);
  }

  public override bool Equals(object obj) {
    if (obj is Optional<T>) {
      return Equals((Optional<T>)obj);
    }

    return false;
  }

  public bool Equals(Optional<T> other) {
    if (HasValue && other.HasValue) {
      return Equals(value_, other.value_);
    }

    return HasValue == other.HasValue;
  }

  public override int GetHashCode() {
    return HashCode.Combine(value_, HasValue);
  }
}
