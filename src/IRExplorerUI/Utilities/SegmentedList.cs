// ---------------------------------------------------------------------------
// <copyright file="SegmentedList.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// ---------------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System;

namespace IRExplorerUI.Utilities {
    public sealed class SegmentedDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IDictionary {
        #region Private Fields
        private static Entry EntryPlaceholder = new Entry();

        private SegmentedList<int> _buckets = new SegmentedList<int>(defaultSegmentSize);
        private SegmentedList<Entry> _entries = new SegmentedList<Entry>(defaultSegmentSize);

        private const int defaultSegmentSize = 2048;

        private int _count;
        private int _freeList;
        private int _freeCount;
        private ulong _fastModMultiplier;
        private int _version;

        private readonly IEqualityComparer<TKey> _comparer;

        private KeyCollection _keys = null;
        private ValueCollection _values = null;
        private const int StartOfFreeList = -3;

        private enum InsertionBehavior {
            None, OverwriteExisting, ThrowOnExisting
        }

        private struct Entry {
            public uint _hashCode;
            /// <summary>
            /// 0-based index of next entry in chain: -1 means end of chain
            /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
            /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
            /// </summary>
            public int _next;
            public TKey _key;     // Key of entry
            public TValue _value; // Value of entry
        }

        #endregion

        #region Helper Methods

        private int Initialize(int capacity) {
            var size = HashHelpers.GetPrime(capacity);
            var buckets = new SegmentedList<int>(defaultSegmentSize, size);
            var entries = new SegmentedList<Entry>(defaultSegmentSize, size);

            // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
            _freeList = -1;
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)buckets.Capacity);
            _buckets = buckets;
            _entries = entries;

            return size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref int GetBucket(uint hashCode) {
            var buckets = _buckets;
            return ref buckets.GetElementByReference((int)HashHelpers.FastMod(hashCode, (uint)buckets.Capacity, _fastModMultiplier));
        }

        private bool FindEntry(TKey key, out Entry entry) {
            entry = EntryPlaceholder;

            if (key == null) {
                throw new ArgumentNullException("Key cannot be null.");
            }

            if (_buckets.Capacity > 0) {
                Debug.Assert(_entries.Capacity > 0, "expected entries to be non-empty");
                var comparer = _comparer;

                var hashCode = (uint)comparer.GetHashCode(key);
                var i = GetBucket(hashCode) - 1; // Value in _buckets is 1-based; subtract 1 from i. We do it here so it fuses with the following conditional.
                var entries = _entries;
                uint collisionCount = 0;

                do {
                    // Should be a while loop https://github.com/dotnet/runtime/issues/9422
                    // Test in if to drop range check for following array access
                    if ((uint)i >= (uint)entries.Capacity) {
                        return false;
                    }

                    ref var currentEntry = ref entries.GetElementByReference(i);
                    if (currentEntry._hashCode == hashCode && comparer.Equals(currentEntry._key, key)) {
                        entry = currentEntry;
                        return true;
                    }

                    i = currentEntry._next;

                    collisionCount++;
                } while (collisionCount <= (uint)entries.Capacity);

                // The chain of entries forms a loop; which means a concurrent update has happened.
                // Break out of the loop and throw, rather than looping forever.
                throw new InvalidOperationException("Dictionary does not support concurrent operations.");
            }

            return false;
        }

        private bool TryInsert(TKey key, TValue value, InsertionBehavior behavior) {
            if (key == null) {
                throw new ArgumentNullException("Key cannot be null.");
            }

            if (_buckets.Capacity == 0) {
                Initialize(0);
            }
            Debug.Assert(_buckets.Capacity > 0);

            var entries = _entries;
            Debug.Assert(entries.Capacity > 0, "expected entries to be non-empty");

            var comparer = _comparer;
            var hashCode = (uint)comparer.GetHashCode(key);

            uint collisionCount = 0;
            ref var bucket = ref GetBucket(hashCode);
            var i = bucket - 1; // Value in _buckets is 1-based

            while (true) {
                // Should be a while loop https://github.com/dotnet/runtime/issues/9422
                // Test uint in if rather than loop condition to drop range check for following array access
                if ((uint)i >= (uint)entries.Capacity) {
                    break;
                }

                if (entries[i]._hashCode == hashCode && comparer.Equals(entries[i]._key, key)) {
                    if (behavior == InsertionBehavior.OverwriteExisting) {
                        entries.GetElementByReference(i)._value = value;
                        return true;
                    }

                    if (behavior == InsertionBehavior.ThrowOnExisting) {
                        throw new ArgumentException($"The key with value {key} is already present in the dictionary.");
                    }

                    return false;
                }

                i = entries[i]._next;

                collisionCount++;
                if (collisionCount > (uint)entries.Capacity) {
                    // The chain of entries forms a loop; which means a concurrent update has happened.
                    // Break out of the loop and throw, rather than looping forever.
                    throw new InvalidOperationException("Dictionary does not support concurrent operations.");
                }
            }


            int index;
            if (_freeCount > 0) {
                index = _freeList;
                Debug.Assert((StartOfFreeList - entries[_freeList]._next) >= -1, "shouldn't overflow because `next` cannot underflow");
                _freeList = StartOfFreeList - entries[_freeList]._next;
                _freeCount--;
            }
            else {
                var count = _count;
                if (count == entries.Capacity) {
                    Resize();
                    bucket = ref GetBucket(hashCode);
                }
                index = count;
                _count = count + 1;
                entries = _entries;
            }

            ref var entry = ref entries.GetElementByReference(index);
            entry._hashCode = hashCode;
            entry._next = bucket - 1; // Value in _buckets is 1-based
            entry._key = key;
            entry._value = value; // Value in _buckets is 1-based
            bucket = index + 1;
            _version++;
            return true;
        }

        private void Resize()
            => Resize(HashHelpers.ExpandPrime(_count));

        private void Resize(int newSize) {
            Debug.Assert(_entries.Capacity > 0, "_entries should be non-empty");
            Debug.Assert(newSize >= _entries.Capacity);

            var entries = new SegmentedList<Entry>(defaultSegmentSize, newSize);

            var count = _count;

            entries.AppendFrom(_entries, 0, count);

            // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
            _buckets = new SegmentedList<int>(defaultSegmentSize, newSize);
            _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)_buckets.Capacity);
            for (var i = 0; i < count; i++) {
                if (entries[i]._next >= -1) {
                    ref var bucket = ref GetBucket(entries[i]._hashCode);
                    entries.GetElementByReference(i)._next = bucket - 1; // Value in _buckets is 1-based
                    bucket = i + 1;
                }
            }

            _entries = entries;
        }

        private static bool IsCompatibleKey(object key) {
            if (key == null) {
                throw new ArgumentNullException(nameof(key));
            }
            return key is TKey;
        }

        #endregion

        #region Constructors

        public SegmentedDictionary()
            : this(0, null) {
        }

        public SegmentedDictionary(int capacity)
            : this(capacity, null) {
        }

        public SegmentedDictionary(IEqualityComparer<TKey> comparer)
            : this(0, comparer) {
        }

        public SegmentedDictionary(int capacity, IEqualityComparer<TKey> comparer) {
            if (capacity < 0) {
                throw new ArgumentException(nameof(capacity));
            }

            if (capacity > 0) {
                Initialize(capacity);
            }

            if (comparer != null && comparer != EqualityComparer<TKey>.Default) // first check for null to avoid forcing default comparer instantiation unnecessarily
            {
                _comparer = comparer;
            }
            else {
                _comparer = EqualityComparer<TKey>.Default;
            }
        }

        public SegmentedDictionary(IDictionary<TKey, TValue> dictionary)
            : this(dictionary, null) {
        }

        public SegmentedDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
            : this(dictionary != null ? dictionary.Count : 0, comparer) {
            if (dictionary == null) {
                throw new ArgumentNullException(nameof(dictionary));
            }

            // It is likely that the passed-in dictionary is SegmentedDictionary<TKey,TValue>. When this is the case,
            // avoid the enumerator allocation and overhead by looping through the entries array directly.
            // We only do this when dictionary is SegmentedDictionary<TKey,TValue> and not a subclass, to maintain
            // back-compat with subclasses that may have overridden the enumerator behavior.
            if (dictionary.GetType() == typeof(SegmentedDictionary<TKey, TValue>)) {
                var d = (SegmentedDictionary<TKey, TValue>)dictionary;
                var count = d._count;
                var entries = d._entries;
                for (var i = 0; i < count; i++) {
                    if (entries[i]._next >= -1) {
                        Add(entries[i]._key, entries[i]._value);
                    }
                }
                return;
            }

            foreach (var pair in dictionary) {
                Add(pair.Key, pair.Value);
            }
        }

        #endregion

        #region IDictionary<TKey, TValue> Implementation

        public TValue this[TKey key] {
            get {
                if (FindEntry(key, out Entry entry)) {
                    return entry._value;
                }

                return default;
            }
            set {
                var modified = TryInsert(key, value, InsertionBehavior.OverwriteExisting);
                Debug.Assert(modified);
            }
        }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;

        ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;

        public void Add(TKey key, TValue value) {
            var modified = TryInsert(key, value, InsertionBehavior.ThrowOnExisting);
            Debug.Assert(modified); // If there was an existing key and the Add failed, an exception will already have been thrown.
        }

        public bool ContainsKey(TKey key) {
            return FindEntry(key, out Entry entry);
        }

        public bool Remove(TKey key) {
            return Remove(key, out TValue _);
        }

        public bool TryGetValue(TKey key, out TValue value) {
            bool entryFound = FindEntry(key, out Entry entry);
            if (entryFound) {
                value = entry._value;
                return true;
            }

            value = default;
            return false;
        }

        #endregion

        #region ICollection<KeyValuePair<TKey, TValue>> Implementation

        public int Count => _count - _freeCount;

        public bool IsReadOnly => false;

        public void Add(KeyValuePair<TKey, TValue> item) =>
            Add(item.Key, item.Value);

        public void Clear() {
            var count = _count;
            if (count > 0) {
                Debug.Assert(_buckets.Capacity > 0, "_buckets should be non-empty");
                Debug.Assert(_entries.Capacity > 0, "_entries should be non-empty");

                _buckets.Clear();

                _count = 0;
                _freeList = -1;
                _freeCount = 0;
                _entries.Clear();
            }
        }

        public bool Contains(KeyValuePair<TKey, TValue> item) {
            bool valueFound = FindEntry(item.Key, out Entry entry);
            if (valueFound && EqualityComparer<TValue>.Default.Equals(entry._value, item.Value)) {
                return true;
            }

            return false;
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int index) {
            if (array == null) {
                throw new ArgumentNullException(nameof(array));
            }
            
            var count = _count;
            var entries = _entries;
            for (var i = 0; i < count; i++) {
                if (entries[i]._next >= -1) {
                    array[index++] = new KeyValuePair<TKey, TValue>(entries[i]._key, entries[i]._value);
                }
            }
        }

        public bool Remove(KeyValuePair<TKey, TValue> item) {
            if (FindEntry(item.Key, out Entry entry) && EqualityComparer<TValue>.Default.Equals(item.Value, entry._value)) {
                return Remove(item.Key, out TValue _);
            }

            return false;
        }

        #endregion

        #region IEnumerable<KeyValuePair<TKey, TValue>> Implementation

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() =>
            new Enumerator(this, Enumerator.KeyValuePair);

        #endregion

        #region IEnumerable Implementation

        IEnumerator IEnumerable.GetEnumerator() =>
            new Enumerator(this, Enumerator.KeyValuePair);

        #endregion

        #region IDictionary Implementation

        public object this[object key] {
            get {
                if (IsCompatibleKey(key)) {
                    if (FindEntry((TKey)key, out Entry entry)) {
                        return entry._value;
                    }
                }

                return null;
            }
            set {
                if (key == null) {
                    throw new ArgumentNullException(nameof(key));
                }
                
                try {
                    var tempKey = (TKey)key;
                    try {
                        this[tempKey] = (TValue)value;
                    }
                    catch (InvalidCastException) {
                    }
                }
                catch (InvalidCastException) {
                }
            }
        }

        ICollection IDictionary.Keys => Keys;

        ICollection IDictionary.Values => Values;

        public bool IsFixedSize => false;

        public void Add(object key, object value) {
            if (key == null) {
                throw new ArgumentNullException(nameof(key));
            }


            try {
                var tempKey = (TKey)key;

                try {
                    Add(tempKey, (TValue)value);
                }
                catch (InvalidCastException) {
                }
            }
            catch (InvalidCastException) {
            }
        }

        public bool Contains(object key) {
            if (IsCompatibleKey(key)) {
                return ContainsKey((TKey)key);
            }

            return false;
        }

        IDictionaryEnumerator IDictionary.GetEnumerator() =>
            new Enumerator(this, Enumerator.DictEntry);

        public void Remove(object key) {
            if (IsCompatibleKey(key)) {
                Remove((TKey)key);
            }
        }

        #endregion

        #region ICollection Implementation

        public object SyncRoot => this;

        public bool IsSynchronized => false;

        public void CopyTo(Array array, int index) {
            if (array == null) {
                throw new ArgumentNullException(nameof(array));
            }

            if (array.Rank != 1) {
                throw new ArgumentException(ThrowHelper.CommonStrings.Arg_RankMultiDimNotSupported);
            }

            if (array.GetLowerBound(0) != 0) {
                throw new ArgumentException(ThrowHelper.CommonStrings.Arg_NonZeroLowerBound);
            }

            if ((uint)index > (uint)array.Length) {
                ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
            }

            if (array.Length - index < Count) {
                throw new ArgumentException(ThrowHelper.CommonStrings.Arg_ArrayPlusOffTooSmall);
            }

            if (array is KeyValuePair<TKey, TValue>[] pairs) {
                CopyTo(pairs, index);
            }
            else if (array is DictionaryEntry[] dictEntryArray) {
                var entries = _entries;
                for (var i = 0; i < _count; i++) {
                    if (entries[i]._next >= -1) {
                        dictEntryArray[index++] = new DictionaryEntry(entries[i]._key, entries[i]._value);
                    }
                }
            }
            else {
                var objects = array as object[];
                if (objects == null) {
                    throw new ArgumentException(ThrowHelper.CommonStrings.Argument_InvalidArrayType);
                }

                try {
                    var count = _count;
                    var entries = _entries;
                    for (var i = 0; i < count; i++) {
                        if (entries[i]._next >= -1) {
                            objects[index++] = new KeyValuePair<TKey, TValue>(entries[i]._key, entries[i]._value);
                        }
                    }
                }
                catch (ArrayTypeMismatchException) {
                    throw new ArgumentException(ThrowHelper.CommonStrings.Argument_InvalidArrayType);
                }
            }
        }

        #endregion

        #region Public Properties

        public IEqualityComparer<TKey> Comparer {
            get {
                return _comparer ?? EqualityComparer<TKey>.Default;
            }
        }

        public KeyCollection Keys {
            get {
                if (_keys == null) {
                    _keys = new KeyCollection(this);
                }

                return _keys;
            }
        }

        public ValueCollection Values {
            get {
                if (_values == null) {
                    _values = new ValueCollection(this);
                }

                return _values;
            }
        }

        #endregion

        #region Public Methods

        public bool TryAdd(TKey key, TValue value) =>
            TryInsert(key, value, InsertionBehavior.None);

        public bool Remove(TKey key, out TValue value) {
            // If perfomarnce becomes an issue, you can copy this implementation over to the other Remove method overloads.

            if (key == null) {
                throw new ArgumentNullException(nameof(key));
            }

            if (_buckets.Capacity > 0) {
                Debug.Assert(_entries.Capacity > 0, "entries should be non-empty");
                uint collisionCount = 0;
                var hashCode = (uint)(_comparer?.GetHashCode(key) ?? key.GetHashCode());
                ref var bucket = ref GetBucket(hashCode);
                var entries = _entries;
                var last = -1;
                var i = bucket - 1; // Value in buckets is 1-based
                while (i >= 0) {
                    ref var entry = ref entries.GetElementByReference(i);

                    if (entry._hashCode == hashCode && (_comparer?.Equals(entry._key, key) ?? EqualityComparer<TKey>.Default.Equals(entry._key, key))) {
                        if (last < 0) {
                            bucket = entry._next + 1; // Value in buckets is 1-based
                        }
                        else {
                            entries.GetElementByReference(last)._next = entry._next;
                        }

                        value = entry._value;

                        Debug.Assert((StartOfFreeList - _freeList) < 0, "shouldn't underflow because max hashtable length is MaxPrimeArrayLength = 0x7FEFFFFD(2146435069) _freelist underflow threshold 2147483646");
                        entry._next = StartOfFreeList - _freeList;

                        entry._key = default;
                        entry._value = default;

                        _freeList = i;
                        _freeCount++;
                        return true;
                    }

                    last = i;
                    i = entry._next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Capacity) {
                        // The chain of entries forms a loop; which means a concurrent update has happened.
                        // Break out of the loop and throw, rather than looping forever.
                        throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_ConcurrentOperationsNotSupported);
                    }
                }
            }

            value = default;
            return false;
        }

        public int EnsureCapacity(int capacity) {
            if (capacity < 0) {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            // Normal usage of a dictionary should never ask for a capacity that exceeds int32.MaxValue.
            var currentCapacity = (int)_entries.Capacity;
            if (currentCapacity >= capacity) {
                return currentCapacity;
            }

            _version++;

            if (_buckets.Capacity == 0) {
                return Initialize(capacity);
            }

            var newSize = HashHelpers.GetPrime(capacity);
            Resize(newSize);
            return newSize;
        }

        public bool ContainsValue(TValue value) {
            var entries = _entries;
            if (value == null) {
                for (var i = 0; i < _count; i++) {
                    if (entries[i]._next >= -1 && entries[i]._value == null) {
                        return true;
                    }
                }
            }
            else {
                // Object type: Shared Generic, EqualityComparer<TValue>.Default won't devirtualize
                // https://github.com/dotnet/runtime/issues/10050
                // So cache in a local rather than get EqualityComparer per loop iteration
                var defaultComparer = EqualityComparer<TValue>.Default;
                for (var i = 0; i < _count; i++) {
                    if (entries[i]._next >= -1 && defaultComparer.Equals(entries[i]._value, value)) {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator {
            private readonly SegmentedDictionary<TKey, TValue> _dictionary;
            private readonly int _version;
            private int _index;
            private KeyValuePair<TKey, TValue> _current;
            private readonly int _getEnumeratorRetType;  // What should Enumerator.Current return?

            internal const int DictEntry = 1;
            internal const int KeyValuePair = 2;

            internal Enumerator(SegmentedDictionary<TKey, TValue> dictionary, int getEnumeratorRetType) {
                _dictionary = dictionary;
                _version = dictionary._version;
                _index = 0;
                _getEnumeratorRetType = getEnumeratorRetType;
                _current = default;
            }

            public bool MoveNext() {
                if (_version != _dictionary._version) {
                    throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumFailedVersion);
                }

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)_index < (uint)_dictionary._count) {
                    ref var entry = ref _dictionary._entries.GetElementByReference(_index++);

                    if (entry._next >= -1) {
                        _current = new KeyValuePair<TKey, TValue>(entry._key, entry._value);
                        return true;
                    }
                }

                _index = _dictionary._count + 1;
                _current = default;
                return false;
            }

            public KeyValuePair<TKey, TValue> Current => _current;

            public void Dispose() {
            }

            object IEnumerator.Current {
                get {
                    if (_index == 0 || (_index == _dictionary._count + 1)) {
                        throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumOpCantHappen);
                    }

                    if (_getEnumeratorRetType == DictEntry) {
                        return new DictionaryEntry(_current.Key, _current.Value);
                    }

                    return new KeyValuePair<TKey, TValue>(_current.Key, _current.Value);
                }
            }

            void IEnumerator.Reset() {
                if (_version != _dictionary._version) {
                    throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumFailedVersion);
                }

                _index = 0;
                _current = default;
            }

            DictionaryEntry IDictionaryEnumerator.Entry {
                get {
                    if (_index == 0 || (_index == _dictionary._count + 1)) {
                        throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumOpCantHappen);
                    }

                    return new DictionaryEntry(_current.Key, _current.Value);
                }
            }

            object IDictionaryEnumerator.Key {
                get {
                    if (_index == 0 || (_index == _dictionary._count + 1)) {
                        throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumOpCantHappen);
                    }

                    return _current.Key;
                }
            }

            object IDictionaryEnumerator.Value {
                get {
                    if (_index == 0 || (_index == _dictionary._count + 1)) {
                        throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumOpCantHappen);
                    }

                    return _current.Value;
                }
            }
        }

        public sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey> {
            private readonly SegmentedDictionary<TKey, TValue> _dictionary;

            public KeyCollection(SegmentedDictionary<TKey, TValue> dictionary) {
                if (dictionary == null) {
                    throw new ArgumentNullException(nameof(dictionary));
                }

                _dictionary = dictionary;
            }

            public Enumerator GetEnumerator()
                => new Enumerator(_dictionary);

            public void CopyTo(TKey[] array, int index) {
                if (array == null) {
                    throw new ArgumentNullException(nameof(array));
                }

                if (index < 0 || index > array.Length) {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count) {
                    throw new ArgumentException(ThrowHelper.CommonStrings.Arg_ArrayPlusOffTooSmall);
                }

                var count = _dictionary._count;
                var entries = _dictionary._entries;
                for (var i = 0; i < count; i++) {
                    if (entries[i]._next >= -1)
                        array[index++] = entries[i]._key;
                }
            }

            public int Count => _dictionary.Count;

            bool ICollection<TKey>.IsReadOnly => true;

            void ICollection<TKey>.Add(TKey item)
                => throw new NotSupportedException();

            void ICollection<TKey>.Clear()
                => throw new NotSupportedException();

            public bool Contains(TKey item)
                => _dictionary.ContainsKey(item);

            bool ICollection<TKey>.Remove(TKey item) {
                throw new NotSupportedException();
            }

            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator()
                => new Enumerator(_dictionary);

            IEnumerator IEnumerable.GetEnumerator()
                => new Enumerator(_dictionary);

            void ICollection.CopyTo(Array array, int index) {
                if (array == null) {
                    throw new ArgumentNullException(nameof(array));
                }

                if (array.Rank != 1) {
                    throw new ArgumentException(ThrowHelper.CommonStrings.Arg_RankMultiDimNotSupported);
                }

                if (array.GetLowerBound(0) != 0) {
                    throw new ArgumentException(ThrowHelper.CommonStrings.Arg_NonZeroLowerBound);
                }

                if ((uint)index > (uint)array.Length) {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count) {
                    throw new ArgumentException(ThrowHelper.CommonStrings.Arg_ArrayPlusOffTooSmall);
                }

                if (array is TKey[] keys) {
                    CopyTo(keys, index);
                }
                else {
                    var objects = array as object[];
                    if (objects == null) {
                        throw new ArgumentException(ThrowHelper.CommonStrings.Argument_InvalidArrayType);
                    }

                    var count = _dictionary._count;
                    var entries = _dictionary._entries;
                    try {
                        for (var i = 0; i < count; i++) {
                            if (entries[i]._next >= -1)
                                objects[index++] = entries[i]._key;
                        }
                    }
                    catch (ArrayTypeMismatchException) {
                        throw new ArgumentException(ThrowHelper.CommonStrings.Argument_InvalidArrayType);
                    }
                }
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => ((ICollection)_dictionary).SyncRoot;

            public struct Enumerator : IEnumerator<TKey>, IEnumerator {
                private readonly SegmentedDictionary<TKey, TValue> _dictionary;
                private int _index;
                private readonly int _version;
                private TKey _currentKey;

                internal Enumerator(SegmentedDictionary<TKey, TValue> dictionary) {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _index = 0;
                    _currentKey = default;
                }

                public void Dispose() {
                }

                public bool MoveNext() {
                    if (_version != _dictionary._version) {
                        throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumFailedVersion);
                    }

                    while ((uint)_index < (uint)_dictionary._count) {
                        ref var entry = ref _dictionary._entries.GetElementByReference(_index++);

                        if (entry._next >= -1) {
                            _currentKey = entry._key;
                            return true;
                        }
                    }

                    _index = _dictionary._count + 1;
                    _currentKey = default;
                    return false;
                }

                public TKey Current => _currentKey;

                object IEnumerator.Current {
                    get {
                        if (_index == 0 || (_index == _dictionary._count + 1)) {
                            throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumOpCantHappen);
                        }

                        return _currentKey;
                    }
                }

                void IEnumerator.Reset() {
                    if (_version != _dictionary._version) {
                        throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumFailedVersion);
                    }

                    _index = 0;
                    _currentKey = default;
                }
            }
        }

        public sealed class ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue> {
            private readonly SegmentedDictionary<TKey, TValue> _dictionary;

            public ValueCollection(SegmentedDictionary<TKey, TValue> dictionary) {
                if (dictionary == null) {
                    throw new ArgumentNullException(nameof(dictionary));
                }

                _dictionary = dictionary;
            }

            public Enumerator GetEnumerator()
                => new Enumerator(_dictionary);

            public void CopyTo(TValue[] array, int index) {
                if (array == null) {
                    throw new ArgumentNullException(nameof(array));
                }

                if ((uint)index > array.Length) {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count) {
                    throw new ArgumentException(ThrowHelper.CommonStrings.Arg_ArrayPlusOffTooSmall);
                }

                var count = _dictionary._count;
                var entries = _dictionary._entries;
                for (var i = 0; i < count; i++) {
                    if (entries[i]._next >= -1)
                        array[index++] = entries[i]._value;
                }
            }

            public int Count => _dictionary.Count;

            bool ICollection<TValue>.IsReadOnly => true;

            void ICollection<TValue>.Add(TValue item)
                => throw new NotSupportedException();

            bool ICollection<TValue>.Remove(TValue item)
                => throw new NotSupportedException();

            void ICollection<TValue>.Clear()
                => throw new NotSupportedException();

            public bool Contains(TValue item)
                => _dictionary.ContainsValue(item);

            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
                => new Enumerator(_dictionary);

            IEnumerator IEnumerable.GetEnumerator()
                => new Enumerator(_dictionary);

            void ICollection.CopyTo(Array array, int index) {
                if (array == null) {
                    throw new ArgumentNullException(nameof(array));
                }

                if (array.Rank != 1) {
                    throw new ArgumentException(ThrowHelper.CommonStrings.Arg_RankMultiDimNotSupported);
                }

                if (array.GetLowerBound(0) != 0) {
                    throw new ArgumentException(ThrowHelper.CommonStrings.Arg_NonZeroLowerBound);
                }

                if ((uint)index > (uint)array.Length) {
                    ThrowHelper.ThrowIndexArgumentOutOfRange_NeedNonNegNumException();
                }

                if (array.Length - index < _dictionary.Count) {
                    throw new ArgumentException(ThrowHelper.CommonStrings.Arg_ArrayPlusOffTooSmall);
                }

                if (array is TValue[] values) {
                    CopyTo(values, index);
                }
                else {
                    var objects = array as object[];
                    if (objects == null) {
                        throw new ArgumentException(ThrowHelper.CommonStrings.Argument_InvalidArrayType);
                    }

                    var count = _dictionary._count;
                    var entries = _dictionary._entries;
                    try {
                        for (var i = 0; i < count; i++) {
                            if (entries[i]._next >= -1)
                                objects[index++] = entries[i]._value;
                        }
                    }
                    catch (ArrayTypeMismatchException) {
                        throw new ArgumentException(ThrowHelper.CommonStrings.Argument_InvalidArrayType);
                    }
                }
            }

            bool ICollection.IsSynchronized => false;

            object ICollection.SyncRoot => ((ICollection)_dictionary).SyncRoot;

            public struct Enumerator : IEnumerator<TValue>, IEnumerator {
                private readonly SegmentedDictionary<TKey, TValue> _dictionary;
                private int _index;
                private readonly int _version;
                private TValue _currentValue;

                internal Enumerator(SegmentedDictionary<TKey, TValue> dictionary) {
                    _dictionary = dictionary;
                    _version = dictionary._version;
                    _index = 0;
                    _currentValue = default;
                }

                public void Dispose() {
                }

                public bool MoveNext() {
                    if (_version != _dictionary._version) {
                        throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumFailedVersion);
                    }

                    while ((uint)_index < (uint)_dictionary._count) {
                        ref var entry = ref _dictionary._entries.GetElementByReference(_index++);

                        if (entry._next >= -1) {
                            _currentValue = entry._value;
                            return true;
                        }
                    }
                    _index = _dictionary._count + 1;
                    _currentValue = default;
                    return false;
                }

                public TValue Current => _currentValue;

                object IEnumerator.Current {
                    get {
                        if (_index == 0 || (_index == _dictionary._count + 1)) {
                            throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumOpCantHappen);
                        }

                        return _currentValue;
                    }
                }

                void IEnumerator.Reset() {
                    if (_version != _dictionary._version) {
                        throw new InvalidOperationException(ThrowHelper.CommonStrings.InvalidOperation_EnumFailedVersion);
                    }

                    _index = 0;
                    _currentValue = default;
                }
            }
        }
    }

    internal static class HashHelpers {
        // This is the maximum prime smaller than Array.MaxArrayLength
        public const int MaxPrimeArrayLength = 0x7FEFFFFD;

        public const int HashPrime = 101;

        // Table of prime numbers to use as hash table sizes.
        // A typical resize algorithm would pick the smallest prime number in this array
        // that is larger than twice the previous capacity.
        // Suppose our Hashtable currently has capacity x and enough elements are added
        // such that a resize needs to occur. Resizing first computes 2x then finds the
        // first prime in the table greater than 2x, i.e. if primes are ordered
        // p_1, p_2, ..., p_i, ..., it finds p_n such that p_n-1 < 2x < p_n.
        // Doubling is important for preserving the asymptotic complexity of the
        // hashtable operations such as add.  Having a prime guarantees that double
        // hashing does not lead to infinite loops.  IE, your hash function will be
        // h1(key) + i*h2(key), 0 <= i < size.  h2 and the size must be relatively prime.
        // We prefer the low computation costs of higher prime numbers over the increased
        // memory allocation of a fixed prime number i.e. when right sizing a HashSet.
        private static readonly int[] s_primes =
        {
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
            1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
            17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
            187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
            1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
        };

        public static bool IsPrime(int candidate) {
            if ((candidate & 1) != 0) {
                var limit = (int)Math.Sqrt(candidate);
                for (var divisor = 3; divisor <= limit; divisor += 2) {
                    if ((candidate % divisor) == 0)
                        return false;
                }
                return true;
            }
            return candidate == 2;
        }

        public static int GetPrime(int min) {
            if (min < 0)
                throw new ArgumentException("Collection's capacity overflowed and went negative.");

            foreach (var prime in s_primes) {
                if (prime >= min)
                    return prime;
            }

            // Outside of our predefined table. Compute the hard way.
            for (var i = (min | 1); i < int.MaxValue; i += 2) {
                if (IsPrime(i) && ((i - 1) % HashPrime != 0))
                    return i;
            }
            return min;
        }

        // Returns size of hashtable to grow to.
        public static int ExpandPrime(int oldSize) {
            var newSize = 2 * oldSize;

            // Allow the hashtables to grow to maximum possible size (~2G elements) before encountering capacity overflow.
            // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
            if ((uint)newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize) {
                Debug.Assert(MaxPrimeArrayLength == GetPrime(MaxPrimeArrayLength), "Invalid MaxPrimeArrayLength");
                return MaxPrimeArrayLength;
            }

            return GetPrime(newSize);
        }

        /// <summary>Returns approximate reciprocal of the divisor: ceil(2**64 / divisor).</summary>
        /// <remarks>This should only be used on 64-bit.</remarks>
        public static ulong GetFastModMultiplier(uint divisor) =>
            ulong.MaxValue / divisor + 1;

        /// <summary>Performs a mod operation using the multiplier pre-computed with <see cref="GetFastModMultiplier"/>.</summary>
        /// <remarks>
        /// PERF: This improves performance in 64-bit scenarios at the expense of performance in 32-bit scenarios. Since
        /// we only build a single AnyCPU binary, we opt for improved performance in the 64-bit scenario.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint FastMod(uint value, uint divisor, ulong multiplier) {
            // We use modified Daniel Lemire's fastmod algorithm (https://github.com/dotnet/runtime/pull/406),
            // which allows to avoid the long multiplication if the divisor is less than 2**31.
            Debug.Assert(divisor <= int.MaxValue);

            // This is equivalent of (uint)Math.BigMul(multiplier * value, divisor, out _). This version
            // is faster than BigMul currently because we only need the high bits.
            var highbits = (uint)(((((multiplier * value) >> 32) + 1) * divisor) >> 32);

            Debug.Assert(highbits == value % divisor);
            return highbits;
        }
    }

    internal static class ThrowHelper {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void IfNullAndNullsAreIllegalThenThrow<T>(object value, string argName) {
            // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
            if (!(default(T) == null) && value == null)
                throw new ArgumentNullException(argName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowKeyNotFoundException<T>(T key) {
            throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
        }

        internal static void ThrowIndexArgumentOutOfRange_NeedNonNegNumException() {
            throw GetArgumentOutOfRangeException("index",
                                                 CommonStrings.ArgumentOutOfRange_NeedNonNegNum);
        }

        internal static void ThrowWrongTypeArgumentException<T>(T value, Type targetType) {
            throw GetWrongTypeArgumentException(value, targetType);
        }

        private static ArgumentOutOfRangeException GetArgumentOutOfRangeException(string argument, string message) {
            return new ArgumentOutOfRangeException(argument, message);
        }

        private static ArgumentException GetWrongTypeArgumentException(object value, Type targetType) {
            return new ArgumentException($"The value '{value}' is not of type '{targetType}' and cannot be used in this generic collection.",
                                         nameof(value));
        }

        internal static class CommonStrings {
            public static readonly string Arg_ArrayPlusOffTooSmall = "Destination array is not long enough to copy all the items in the collection. Check array index and length.";
            public static readonly string ArgumentOutOfRange_NeedNonNegNum = "Non-negative number required.";
            public static readonly string Arg_RankMultiDimNotSupported = "Only single dimensional arrays are supported for the requested action.";
            public static readonly string Arg_NonZeroLowerBound = "The lower bound of target array must be zero.";
            public static readonly string Argument_InvalidArrayType = "Target array type is not compatible with the type of items in the collection.";
            public static readonly string InvalidOperation_ConcurrentOperationsNotSupported = "Operations that change non-concurrent collections must have exclusive access. A concurrent update was performed on this collection and corrupted its state. The collection's state is no longer correct.";
            public static readonly string InvalidOperation_EnumFailedVersion = "Collection was modified; enumeration operation may not execute.";
            public static readonly string InvalidOperation_EnumOpCantHappen = "Enumeration has either not started or has already finished.";
        }
    }

    public class SegmentedList<T> : ICollection<T>, IReadOnlyList<T> {
        private readonly int segmentSize;
        private readonly int segmentShift;
        private readonly int offsetMask;

        private long capacity;
        private long count;
        private T[][] items;

        /// <summary>
        /// Constructs SegmentedList.
        /// </summary>
        /// <param name="segmentSize">Segment size</param>
        public SegmentedList(int segmentSize) : this(segmentSize, 0) {

        }

        /// <summary>
        /// Constructs SegmentedList.
        /// </summary>
        /// <param name="segmentSize">Segment size</param>
        /// <param name="initialCapacity">Initial capacity</param>
        public SegmentedList(int segmentSize, long initialCapacity) {
            if (segmentSize <= 1 || (segmentSize & (segmentSize - 1)) != 0) {
                throw new ArgumentOutOfRangeException("segment size must be power of 2 greater than 1");
            }

            this.segmentSize = segmentSize;
            this.offsetMask = segmentSize - 1;
            this.segmentShift = 0;

            while (0 != (segmentSize >>= 1)) {
                this.segmentShift++;
            }

            if (initialCapacity > 0) {
                initialCapacity = this.segmentSize * ((initialCapacity + this.segmentSize - 1) / this.segmentSize);
                this.items = new T[initialCapacity >> this.segmentShift][];
                for (int i = 0; i < items.Length; i++) {
                    items[i] = new T[this.segmentSize];
                }

                this.capacity = initialCapacity;
            }
        }

        /// <summary>
        /// Returns the count of elements in the list.
        /// </summary>
        int ICollection<T>.Count {
            get {
                if (Count > int.MaxValue) {
                    throw new InvalidOperationException("Number of elements in Collection are greater than max value of int.");
                }

                return (int)Count;
            }
        }

        public long Count {
            get { return this.count; }
            set {
                Debug.Assert(value >= 0);
                this.count = value;
            }
        }

        internal long Capacity => this.capacity;

        /// <summary>
        /// Copy to Array
        /// </summary>
        /// <returns>Array copy</returns>
        public T[] UnderlyingArray => ToArray();

        /// <summary>
        /// Returns the last element on the list and removes it from it.
        /// </summary>
        /// <returns>The last element that was on the list.</returns>
        public T Pop() {
            if (count == 0) {
                throw new InvalidOperationException("Attempting to remove an element from empty collection.");
            }

            int oldSegmentIndex = (int)(--count >> segmentShift);
            T result = items[oldSegmentIndex][count & offsetMask];

            int newSegmentIndex = (int)((count - 1) >> segmentShift);

            if (newSegmentIndex != oldSegmentIndex) {
                items[oldSegmentIndex] = null;
                capacity -= segmentSize;
            }

            return result;
        }

        /// <summary>
        /// Returns true if this ICollection is read-only.
        /// </summary>
        bool ICollection<T>.IsReadOnly {
            get { return false; }
        }

        int IReadOnlyCollection<T>.Count {
            get {
                if (Count > int.MaxValue) {
                    throw new InvalidOperationException("Number of elements in Collection are greater than max value of int.");
                }

                return (int)Count;
            }
        }

        /// <summary>
        /// Gets or sets the given element in the list.
        /// </summary>
        /// <param name="index">Element index.</param>
        T IReadOnlyList<T>.this[int index] => this[index];

        /// <summary>
        /// Gets or sets the given element in the list.
        /// </summary>
        /// <param name="index">Element index.</param>
        public T this[long index] {
            get {
                return this.items[index >> this.segmentShift][index & this.offsetMask];
            }

            set {
                this.items[index >> this.segmentShift][index & this.offsetMask] = value;
            }
        }

        internal ref T GetElementByReference(int index) =>
            ref this.items[index >> this.segmentShift][index & this.offsetMask];

        /// <summary>
        /// Necessary if the list is being used as an array since it creates the segments lazily.
        /// </summary>
        /// <param name="index"></param>
        /// <returns>true if the segment is allocated and false otherwise</returns>
        public bool IsValidIndex(long index) {
            return this.items[index >> this.segmentShift] != null;
        }

        /// <summary>
        /// Get slot of an element
        /// </summary>
        /// <param name="index"></param>
        /// <param name="slot"></param>
        /// <returns></returns>
        public T[] GetSlot(int index, out int slot) {
            slot = index & this.offsetMask;
            return this.items[index >> this.segmentShift];
        }

        /// <summary>
        /// Adds new element at the end of the list.
        /// </summary>
        /// <param name="item">New element.</param>
        public void Add(T item) {
            if (this.count == this.capacity) {
                this.EnsureCapacity(this.count + 1);
            }

            this.items[this.count >> this.segmentShift][this.count & this.offsetMask] = item;
            this.count++;
        }

        /// <summary>
        /// Inserts new element at the given position in the list.
        /// </summary>
        /// <param name="index">Insert position.</param>
        /// <param name="item">New element to insert.</param>
        public void Insert(long index, T item) {
            // Note that insertions at the end are legal.
            if (this.count == this.capacity) {
                this.EnsureCapacity(this.count + 1);
            }

            if (index < this.count) {
                this.AddRoomForElement(index);
            }

            if (index >= this.capacity) {
                this.count = index;
                this.EnsureCapacity(this.count + 1);
            }

            this.count++;

            this.items[index >> this.segmentShift][index & this.offsetMask] = item;
        }

        /// <summary>
        /// Removes element at the given position in the list.
        /// </summary>
        /// <param name="index">Position of the element to remove.</param>
        public void RemoveAt(long index) {
            if (index < this.count) {
                this.RemoveRoomForElement(index);
            }

            this.count--;
        }

        /// <summary>
        /// Performs a binary search in a sorted list.
        /// </summary>
        /// <param name="item">Element to search for.</param>
        /// <param name="comparer">Comparer to use.</param>
        /// <returns>Non-negative position of the element if found, negative binary complement of the position of the next element if not found.</returns>
        /// <remarks>The implementation was copied from CLR BinarySearch implementation.</remarks>
        public long BinarySearch(T item, IComparer<T> comparer) {
            return BinarySearch(item, 0, this.count - 1, comparer);
        }

        /// <summary>
        /// Performs a binary search in a sorted list.
        /// </summary>
        /// <param name="item">Element to search for.</param>
        /// <param name="low">The lowest index in which to search.</param>
        /// <param name="high">The highest index in which to search.</param>
        /// <param name="comparer">Comparer to use.</param>
        /// <returns>The index </returns>
        public long BinarySearch(T item, long low, long high, IComparer<T> comparer) {
            if (low < 0 || low > high) {
                throw new ArgumentOutOfRangeException($"Low index, with value {low}, must not be negative and cannot be greater than the high index, whose value is {high}.");
            }

            if (high < 0 || high >= count) {
                throw new ArgumentOutOfRangeException($"High index, with value {high}, must not be negative and cannot be greater than the number of elements contained in the list, which is {count}.");
            }

            while (low <= high) {
                long i = low + ((high - low) >> 1);
                int order = comparer.Compare(this.items[i >> this.segmentShift][i & this.offsetMask], item);

                if (order == 0) {
                    return i;
                }

                if (order < 0) {
                    low = i + 1;
                }
                else {
                    high = i - 1;
                }
            }

            return ~low;
        }

        /// <summary>
        /// Sorts the list using default comparer for elements.
        /// </summary>
        public void Sort() {
            this.Sort(Comparer<T>.Default);
        }

        /// <summary>
        /// Sorts the list using specified comparer for elements.
        /// </summary>
        /// <param name="comparer">Comparer to use.</param>
        public void Sort(IComparer<T> comparer) {
            if (this.count <= 1) {
                return;
            }

            this.QuickSort(0, this.count - 1, comparer);
        }

        /// <summary>
        /// Appends a range of elements from another list.
        /// </summary>
        /// <param name="from">Source list.</param>
        /// <param name="index">Start index in the source list.</param>
        /// <param name="count">Count of elements from the source list to append.</param>
        public void AppendFrom(SegmentedList<T> from, long index, long count) {
            if (count > 0) {
                long minCapacity = this.count + count;

                if (this.capacity < minCapacity) {
                    this.EnsureCapacity(minCapacity);
                }

                do {
                    int sourceSegment = (int)(index / from.segmentSize);
                    int sourceOffset = (int)(index % from.segmentSize);
                    int sourceLength = from.segmentSize - sourceOffset;
                    int targetSegment = (int)(this.count >> this.segmentShift);
                    int targetOffset = (int)(this.count & this.offsetMask);
                    int targetLength = this.segmentSize - targetOffset;
                    // We can safely cast to int since source and target lengths will never surpass int.MaxValue
                    int countToCopy = (int)Math.Min(count, Math.Min(sourceLength, targetLength));

                    Array.Copy(from.items[sourceSegment], sourceOffset, this.items[targetSegment], targetOffset, countToCopy);

                    index += countToCopy;
                    count -= countToCopy;
                    this.count += countToCopy;
                }
                while (count != 0);
            }
        }

        /// <summary>
        /// Appends a range of elements from another array.
        /// </summary>
        /// <param name="from">Source array.</param>
        /// <param name="index">Start index in the source list.</param>
        /// <param name="count">Count of elements from the source list to append.</param>
        public void AppendFrom(T[] from, int index, int count) {
            if (count > 0) {
                long minCapacity = this.count + count;

                if (this.capacity < minCapacity) {
                    this.EnsureCapacity(minCapacity);
                }

                do {
                    int targetSegment = (int)(this.count >> this.segmentShift);
                    int targetOffset = (int)(this.count & this.offsetMask);
                    int targetLength = this.segmentSize - targetOffset;
                    int countToCopy = Math.Min(count, targetLength);

                    Array.Copy(from, index, this.items[targetSegment], targetOffset, countToCopy);

                    index += countToCopy;
                    count -= countToCopy;
                    this.count += countToCopy;
                }
                while (count != 0);
            }
        }

        /// <summary>
        /// Returns the enumerator.
        /// </summary>
        public Enumerator GetEnumerator() {
            return new Enumerator(this);
        }

        /// <summary>
        /// Copy to Array
        /// </summary>
        /// <returns>Array copy</returns>
        public T[] ToArray() {
            T[] data = new T[this.count];

            this.CopyTo(data, 0);

            return data;
        }

        /// <summary>
        /// CopyTo copies a collection into an Array, starting at a particular
        /// index into the array.
        /// </summary>
        /// <param name="array">Destination array.</param>
        /// <param name="arrayIndex">Destination array starting index.</param>
        public void CopyTo(T[] array, int arrayIndex) {
            if (array == null) {
                throw new ArgumentNullException(nameof(array));
            }

            if (arrayIndex < 0 || arrayIndex >= array.Length) {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex),
                    "arrayIndex must be non-negative and less than the length of the array.");
            }

            if (array.Length - arrayIndex < this.count) {
                throw new ArgumentException(
                    "Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            }

            long remain = this.count;

            for (long i = 0; (remain > 0) && (i < this.items.Length); i++) {
                // We can safely cast to int, since that is the max value that items[i].Length can have.
                int len = (int)Math.Min(remain, this.items[i].Length);

                Array.Copy(this.items[i], 0, array, arrayIndex, len);

                remain -= len;
                arrayIndex += (int)len;
            }
        }

        /// <summary>
        /// Copies the contents of the collection that are within a range into an Array, starting at a particular
        /// index into the array.
        /// </summary>
        /// <param name="array">Destination array.</param>
        /// <param name="arrayIndex">Destination array starting index.</param>
        /// <param name="startIndex">The collection index from where the copying should start.</param>
        /// <param name="endIndex">The collection index where the copying should end.</param>
        public void CopyRangeTo(T[] array, int arrayIndex, long startIndex, long endIndex) {
            if (array == null) {
                throw new ArgumentNullException(nameof(array));
            }

            if (arrayIndex < 0 || arrayIndex >= array.Length) {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex),
                    "arrayIndex must be non-negative and less than the length of the array.");
            }

            if (startIndex < 0 || startIndex > endIndex) {
                throw new ArgumentOutOfRangeException(nameof(startIndex),
                    "Index must be non-negative and less than or equal to endIndex.");
            }

            if (endIndex < 0 || !IsValidIndex(endIndex)) {
                throw new ArgumentOutOfRangeException(nameof(endIndex),
                    "Index must be non-negative and less than the length of this collection.");
            }

            if (array.Length - arrayIndex < (endIndex - startIndex + 1)) {
                throw new ArgumentException(
                    "Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            }

            int remain = (int)Math.Min(this.count, endIndex - startIndex + 1);
            int firstSegmentIndex = (int)(startIndex / segmentSize);
            int lastSegmentIndex = Math.Min((int)(endIndex / segmentSize), this.items.Length); // The list might not have the range specified, we limit it if necessary to the actual size
            int segmentStartIndex = (int)(startIndex % segmentSize);

            for (int i = firstSegmentIndex; (remain > 0) && (i <= lastSegmentIndex); i++) {
                int len = Math.Min(remain, this.items[i].Length - segmentStartIndex);

                Array.Copy(this.items[i], segmentStartIndex, array, arrayIndex, len);

                remain -= len;
                arrayIndex += (int)len;
                segmentStartIndex = 0;
            }
        }

        /// <summary>
        /// Returns the enumerator.
        /// </summary>
        IEnumerator<T> IEnumerable<T>.GetEnumerator() {
            return new Enumerator(this);
        }

        /// <summary>
        /// Returns the enumerator.
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator() {
            return new Enumerator(this);
        }

        /// <summary>
        /// Clears the list (removes all elements).
        /// </summary>
        void ICollection<T>.Clear() {
            Clear();
        }

        public void Clear() {
            items = null;
            count = 0;
            capacity = 0;
        }

        /// <summary>
        /// Check if ICollection contains the given element.
        /// </summary>
        /// <param name="item">Element to check.</param>
        bool ICollection<T>.Contains(T item) =>
            throw new NotImplementedException();

        /// <summary>
        /// CopyTo copies a collection into an Array, starting at a particular
        /// index into the array.
        /// </summary>
        /// <param name="array">Destination array.</param>
        /// <param name="arrayIndex">Destination array starting index.</param>
        void ICollection<T>.CopyTo(T[] array, int arrayIndex) =>
            CopyTo(array, arrayIndex);

        /// <summary>
        /// Removes the given element from this ICollection.
        /// </summary>
        /// <param name="item">Element to remove.</param>
        bool ICollection<T>.Remove(T item) =>
            throw new NotImplementedException();

        /// <summary>
        /// Shifts the tail of the list to make room for a new inserted element.
        /// </summary>
        /// <param name="index">Index of a new inserted element.</param>
        private void AddRoomForElement(long index) {
            int firstSegment = (int)(index >> this.segmentShift);
            int lastSegment = (int)(this.count >> this.segmentShift);
            int firstOffset = (int)(index & this.offsetMask);
            int lastOffset = (int)(this.count & this.offsetMask);

            if (firstSegment == lastSegment) {
                Array.Copy(this.items[firstSegment], firstOffset, this.items[firstSegment], firstOffset + 1, lastOffset - firstOffset);
            }
            else {
                T save = this.items[firstSegment][this.segmentSize - 1];
                Array.Copy(this.items[firstSegment],
                    firstOffset, this.items[firstSegment],
                    firstOffset + 1,
                    this.segmentSize - firstOffset - 1);

                for (int segment = firstSegment + 1; segment < lastSegment; segment++) {
                    T saveT = this.items[segment][this.segmentSize - 1];
                    Array.Copy(this.items[segment], 0, this.items[segment], 1, this.segmentSize - 1);
                    this.items[segment][0] = save;
                    save = saveT;
                }

                Array.Copy(this.items[lastSegment], 0, this.items[lastSegment], 1, lastOffset);
                this.items[lastSegment][0] = save;
            }
        }

        /// <summary>
        /// Shifts the tail of the list to remove the element.
        /// </summary>
        /// <param name="index">Index of the removed element.</param>
        private void RemoveRoomForElement(long index) {
            int firstSegment = (int)(index >> this.segmentShift);
            int lastSegment = (int)((this.count - 1) >> this.segmentShift);
            int firstOffset = (int)(index & this.offsetMask);
            int lastOffset = (int)((this.count - 1) & this.offsetMask);

            if (firstSegment == lastSegment) {
                Array.Copy(this.items[firstSegment], firstOffset + 1, this.items[firstSegment], firstOffset, lastOffset - firstOffset);
            }
            else {
                Array.Copy(this.items[firstSegment], firstOffset + 1, this.items[firstSegment], firstOffset, this.segmentSize - firstOffset - 1);

                for (int segment = firstSegment + 1; segment < lastSegment; segment++) {
                    this.items[segment - 1][this.segmentSize - 1] = this.items[segment][0];
                    Array.Copy(this.items[segment], 1, this.items[segment], 0, this.segmentSize - 1);
                }

                this.items[lastSegment - 1][this.segmentSize - 1] = this.items[lastSegment][0];
                Array.Copy(this.items[lastSegment], 1, this.items[lastSegment], 0, lastOffset);
            }
        }

        /// <summary>
        /// Ensures that we have enough capacity for the given number of elements.
        /// </summary>
        /// <param name="minCapacity">Number of elements.</param>
        private void EnsureCapacity(long minCapacity) {
            if (this.capacity < this.segmentSize) {
                if (this.items == null) {
                    this.items = new T[(minCapacity + this.segmentSize - 1) >> this.segmentShift][];
                }

                long newFirstSegmentCapacity = this.segmentSize;

                if (minCapacity < this.segmentSize) {
                    newFirstSegmentCapacity = this.capacity == 0 ? 2 : this.capacity * 2;

                    while (newFirstSegmentCapacity < minCapacity) {
                        newFirstSegmentCapacity *= 2;
                    }

                    newFirstSegmentCapacity = Math.Min(newFirstSegmentCapacity, this.segmentSize);
                }

                T[] newFirstSegment = new T[newFirstSegmentCapacity];

                if (this.count > 0) {
                    // We can safely cast to int this.count because count < capacity and capacity
                    // will be less than the segment size that is always less than int32.MaxValue
                    Array.Copy(this.items[0], 0, newFirstSegment, 0, (int)this.count);
                }

                this.items[0] = newFirstSegment;
                this.capacity = newFirstSegment.Length;
            }

            if (this.capacity < minCapacity) {
                int currentSegments = (int)(this.capacity >> this.segmentShift);
                int neededSegments = (int)((minCapacity + this.segmentSize - 1) >> this.segmentShift);

                if (neededSegments > this.items.Length) {
                    int newSegmentArrayCapacity = this.items.Length * 2;

                    while (newSegmentArrayCapacity < neededSegments) {
                        newSegmentArrayCapacity *= 2;
                    }

                    T[][] newItems = new T[newSegmentArrayCapacity][];
                    Array.Copy(this.items, 0, newItems, 0, currentSegments);
                    this.items = newItems;
                }

                for (int i = currentSegments; i < neededSegments; i++) {
                    this.items[i] = new T[this.segmentSize];
                    this.capacity += this.segmentSize;
                }
            }
        }

        /// <summary>
        /// Helper method for QuickSort.
        /// </summary>
        /// <param name="comparer">Comparer to use.</param>
        /// <param name="a">Position of the first element.</param>
        /// <param name="b">Position of the second element.</param>
        private void SwapIfGreaterWithItems(IComparer<T> comparer, long a, long b) {
            if (a != b) {
                if (comparer.Compare(this.items[a >> this.segmentShift][a & this.offsetMask], this.items[b >> this.segmentShift][b & this.offsetMask]) > 0) {
                    T key = this.items[a >> this.segmentShift][a & this.offsetMask];
                    this.items[a >> this.segmentShift][a & this.offsetMask] = this.items[b >> this.segmentShift][b & this.offsetMask];
                    this.items[b >> this.segmentShift][b & this.offsetMask] = key;
                }
            }
        }

        /// <summary>
        /// QuickSort implementation.
        /// </summary>
        /// <param name="left">left boundary.</param>
        /// <param name="right">right boundary.</param>
        /// <param name="comparer">Comparer to use.</param>
        /// <remarks>The implementation was copied from CLR QuickSort implementation.</remarks>
        private void QuickSort(long left, long right, IComparer<T> comparer) {
            do {
                long i = left;
                long j = right;

                // pre-sort the low, middle (pivot), and high values in place.
                // this improves performance in the face of already sorted data, or
                // data that is made up of multiple sorted runs appended together.
                long middle = i + ((j - i) >> 1);

                this.SwapIfGreaterWithItems(comparer, i, middle); // swap the low with the mid point
                this.SwapIfGreaterWithItems(comparer, i, j); // swap the low with the high
                this.SwapIfGreaterWithItems(comparer, middle, j); // swap the middle with the high

                T x = this.items[middle >> this.segmentShift][middle & this.offsetMask];

                do {
                    while (comparer.Compare(this.items[i >> this.segmentShift][i & this.offsetMask], x) < 0) {
                        i++;
                    }

                    while (comparer.Compare(x, this.items[j >> this.segmentShift][j & this.offsetMask]) < 0) {
                        j--;
                    }

                    Debug.Assert(i >= left && j <= right, "(i>=left && j<=right) Sort failed - Is your IComparer bogus?");

                    if (i > j) {
                        break;
                    }

                    if (i < j) {
                        T key = this.items[i >> this.segmentShift][i & this.offsetMask];
                        this.items[i >> this.segmentShift][i & this.offsetMask] = this.items[j >> this.segmentShift][j & this.offsetMask];
                        this.items[j >> this.segmentShift][j & this.offsetMask] = key;
                    }

                    i++;
                    j--;
                }
                while (i <= j);

                if (j - left <= right - i) {
                    if (left < j) {
                        QuickSort(left, j, comparer);
                    }
                    left = i;
                }
                else {
                    if (i < right) {
                        QuickSort(i, right, comparer);
                    }
                    right = j;
                }
            }
            while (left < right);
        }

        public int IndexOf(T item) {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Enumerator over the segmented list.
        /// </summary>
        public struct Enumerator : IEnumerator<T>, IEnumerator {
            private readonly SegmentedList<T> list;
            private long index;

            /// <summary>
            /// Constructws the Enumerator.
            /// </summary>
            /// <param name="list">List to enumerate.</param>
            internal Enumerator(SegmentedList<T> list) {
                this.list = list;
                this.index = -1;
            }

            /// <summary>
            /// Disposes the Enumerator.
            /// </summary>
            public void Dispose() {
            }

            /// <summary>
            /// Moves to the nest element in the list.
            /// </summary>
            /// <returns>True if move successful, false if there are no more elements.</returns>
            public bool MoveNext() {
                if (this.index < this.list.count - 1) {
                    index++;
                    return true;
                }

                this.index = -1;

                return false;
            }

            /// <summary>
            /// Returns the current element.
            /// </summary>
            public T Current {
                get { return this.list[this.index]; }
            }

            /// <summary>
            /// Returns the current element.
            /// </summary>
            object IEnumerator.Current {
                get { return this.Current; }
            }

            /// <summary>
            /// Resets the enumerator to initial state.
            /// </summary>
            void IEnumerator.Reset() {
                index = -1;
            }
        }
    }
}
