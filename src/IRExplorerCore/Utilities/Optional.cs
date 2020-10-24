using System;
using System.Diagnostics;

namespace IRExplorerCore {
    public struct Optional<T> {
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
                return this.Equals((Optional<T>)obj);
            }
            else {
                return false;
            }
        }

        public bool Equals(Optional<T> other) {
            if (HasValue && other.HasValue) {
                return object.Equals(value_, other.value_);
            }
            else {
                return HasValue == other.HasValue;
            }
        }

        public override int GetHashCode() {
            return HashCode.Combine(value_, HasValue);
        }
    }
}
