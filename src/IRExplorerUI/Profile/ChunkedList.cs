using System;
using System.Collections;
using System.Collections.Generic;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
public sealed class ChunkedList<T> : IList<T> {
    private const int ChunkSize = 8192;

    [ProtoMember(1)]
    private readonly List<T[]> chunks_ = new List<T[]>();
    [ProtoMember(2)]
    private int count_ = 0;

    public int Count => count_;
    public bool IsReadOnly => false;

    public void Add(T item) {
        int chunk = count_ >> 14;
        int indexInChunk = count_ & (ChunkSize - 1);

        if (indexInChunk == 0) {
            chunks_.Add(new T[ChunkSize]);
        }

#if DEBUG
            if (indexInChunk < 0 || indexInChunk >= ChunkSize) {
                throw new IndexOutOfRangeException();
            }

            if (chunk < 0 || chunk >= chunks_.Count) {
                throw new IndexOutOfRangeException();
            }
#endif

        chunks_[chunk][indexInChunk] = item;
        count_++;
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

    public void Clear() {
        count_ = 0;
        chunks_.Clear();
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

    public IEnumerator<T> GetEnumerator() {
        return new ChunkEnumerator<T>(this);
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return new ChunkEnumerator<T>(this);
    }

    public class ChunkEnumerator<T> : IEnumerator<T> {
        private ChunkedList<T> instance_;
        private int index_;

        public ChunkEnumerator(ChunkedList<T> instance) {
            instance_ = instance;
            index_ = -1;
        }

        public bool MoveNext() {
            if (index_ < instance_.Count && instance_.Count > 0) {
                index_++;
                return true;
            }

            return false;
        }

        public void Reset() {
            index_ = -1;
        }

        public T Current => instance_[index_];

        object IEnumerator.Current => Current;

        public void Dispose() {

        }
    }

    public T this[int index] {
        get {
            int chunk = index >> 14;
            int indexInChunk = index & (ChunkSize - 1);
            return chunks_[chunk][indexInChunk];
        }
        set {
            int chunk = index >> 14;
            int indexInChunk = index & (ChunkSize - 1);
            chunks_[chunk][indexInChunk] = value;
        }
    }

    public ref T GetRef(int index) {
        int chunk = index >> 14;
        int indexInChunk = index & (ChunkSize - 1);
        return ref chunks_[chunk][indexInChunk];
    }
}