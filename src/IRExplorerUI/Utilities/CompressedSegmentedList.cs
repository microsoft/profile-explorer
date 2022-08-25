using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Diagnostics;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace IRExplorerUI.Utilities;

public sealed class CompressedSegmentedList<T> : IList<T> where T : struct {
    sealed class Segment {
        CompressedSegmentedList<T> parent_;
        Task activeTask_; // Task used to (de)compress, null if no pending task.
        byte[] data_;     // Compressed value data.
        T[] values_;      // Decompressed values, if null in compressed state.
        int count_;       // Number of values < capacity.
        int size_;        // Size in bytes of uncompressed capacity.

        public Segment(CompressedSegmentedList<T> parent, int capacity) {
            parent_ = parent;
            values_ = ArrayPool<T>.Shared.Rent(capacity);
            size_ = capacity * Unsafe.SizeOf<T>();
        }

        public int Count => count_;
        public bool WasCompressed => data_ != null;
        public bool IsCompressed => values_ == null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetValue(int index) {
            Debug.Assert(index < count_);
            if (IsCompressed) {
                DecompressValues();
            }

            return values_[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal T GetValueDirect(int index) {
            return values_[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddValue(T value) {
            Debug.Assert(count_ < values_.Length);
            values_[count_++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetValue(int index, T value) {
            Debug.Assert(index < count_);
            DecompressValues();
            values_[index] = value;

            // If the values were compressed before, discard the data
            // since it doesn't match the current values anymore.
            if(WasCompressed) {
                lock(this) {
                    data_ = null;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetValueRef(int index) {
            Debug.Assert(index < count_);
            DecompressValues();

            // If the values were compressed before, discard the data
            // since it doesn't match the current values anymore.
            if (WasCompressed) {
                lock (this) {
                    data_ = null;
                }
            }

            return ref values_[index];
        }

        public void DecompressValues(bool prefetch = false) {
            if (!IsCompressed) {
                // Already decompressed.
                Debug.Assert(values_ != null);
                return;
            }

            // Check if another (de)compression task is running and wait for it.
            // activeTask_ is being reseet by the thread, use a copy.
            //? TODO: Could use Interlocked.MemoryBarrierProcessWide?
            Task task = null;

            lock(this) {
                task = activeTask_;
            }

            if (task != null) {
                task.Wait();

                if (!IsCompressed) {
                    Debug.Assert(values_ != null);
                    return;
                }
            }

            // Decompress the data. In prefetch mode run it on another thread.
            lock (this) {
                if (!IsCompressed) {
                    Debug.Assert(values_ != null);
                    return;
                }

                if (prefetch) {
                    activeTask_ = parent_.ScheduleCompressionTask(() => {
                        var result = DecompressImpl(data_, size_);

                        lock(this) {
                            values_ = result;
                            activeTask_ = null;
                        }
                    });
                }
                else {
                    values_ = DecompressImpl(data_, size_);
                }
            }
        }

        private T[] DecompressImpl(byte[] data, int segmentSize) {
            var dataSpan = data.AsSpan();
            int length = segmentSize / Unsafe.SizeOf<T>();

            // Use an ArrayPool to reduce GC pressure, values are usually used temporarely.
            // Since the pooled array can be larger than needed, slice it to the right length.
            var values = ArrayPool<T>.Shared.Rent(length);
            var decompressedSpan = MemoryMarshal.AsBytes(values.AsSpan()).Slice(0, segmentSize);
            var result = BrotliDecoder.TryDecompress(dataSpan, decompressedSpan, out int readBytes);
            Debug.Assert(result);
            Debug.Assert(readBytes == segmentSize);
            return values;
        }

        public void CompressValues() {
            lock (this) {
                if (WasCompressed) {
                    values_ = null; // Free array.
                    return;
                }

                Debug.Assert(data_ == null);
                var valuesCopy = values_;
                values_ = null; // Put into the compressed state.

                // Compress on another thread.
                activeTask_ = parent_.ScheduleCompressionTask(() => {
                    var result = CompressImpl(valuesCopy, size_);

                    lock (this) {
                        data_ = result;
                        activeTask_ = null;
                    }
                });
            }
        }

        private static byte[] CompressImpl(T[] values, int segmentSize) {
            // Since the pooled array can be larger than needed, slice it to the right length.
            var arraySpan = values.AsSpan();
            var byteSpan = MemoryMarshal.AsBytes(arraySpan).Slice(0, segmentSize);

            int bufferSize = BrotliEncoder.GetMaxCompressedLength(segmentSize);
            var tempBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            var bufferSpan = tempBuffer.AsSpan();

            var encoder = new BrotliEncoder(5, 10);
            var result = encoder.Compress(byteSpan, bufferSpan, out int bytesConsumed, out int bytesWritten, true);
            Debug.Assert(result == OperationStatus.Done);
            encoder.Flush(bufferSpan, out int extraBytesWritten);
            bytesWritten += extraBytesWritten; // Seems to be always 0.

            // Copy to another array, the initial estimate is as large as the input.
            var outBuffer = new byte[bytesWritten];
            Array.Copy(tempBuffer, 0, outBuffer, 0, bytesWritten);

            // Return array to the pool.
            ArrayPool<byte>.Shared.Return(tempBuffer);
            ArrayPool<T>.Shared.Return(values);
            encoder.Dispose();
            return outBuffer;
        }

        public T this[int index] {
            get => GetValue(index);
            set => SetValue(index, value);
        }

        internal void CopyTo(T[] array, int arrayIndex) {
            DecompressValues();
            values_.CopyTo(array, arrayIndex);
        }
    }

    private List<Segment> segments_;
    private Segment activeSegment_;
    private int count_;         // Total number of items in all segments.
    private int segmentLength_; // Number of items per segment.
    private int prefetchLimit_; // Segments to prefetch ahead.
    private BlockingCollection<Task> taskQueue_; // Pending compression tasks.
    private List<Task> taskQueueThreadTasks_;    // Tasks representing the compression threads.
#if DEBUG
    private int version_;
#endif

    // The GC Large Object Heap is used for allocations > 85 KB,
    // ensure each segments stays below that limit.
    static int LOHObjectSize = 80 * 1024;

    public CompressedSegmentedList(bool useThreads = true, bool prefetch = true, int prefetchLimit = 2) :
        this(Math.Max(1, LOHObjectSize / Unsafe.SizeOf<T>()), useThreads, prefetch, prefetchLimit) { }

    public CompressedSegmentedList(int segmentLength, bool useThreads = true,
                                   bool prefetch = true, int prefetchLimit = 2) {
        segmentLength_ = segmentLength;
        segments_ = new List<Segment>();

        if (useThreads) {
            prefetchLimit_ = prefetch ? prefetchLimit : 0;
            SetupCompressionThreads();
        }
    }

    public void Wait(bool reset = true) {
        taskQueue_.CompleteAdding();
        Task.WhenAll(taskQueueThreadTasks_);

        if (reset) {
            SetupCompressionThreads();
        }
    }

    public void CompressRange(int startIndex, int endIndex) {
        Debug.Assert(endIndex >= startIndex);
        int startSegment = startIndex / segmentLength_;
        int endSegment = endIndex / segmentLength_;

        for (int i = startIndex; i <= endSegment; i++) {
            segments_[i].CompressValues();
        }
    }

    public int Count => count_;
    public bool IsReadOnly => false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T value) {
        EnsureSegment();
        activeSegment_.AddValue(value);
        count_++;
#if DEBUG
        version_++;
#endif
    }

    private void EnsureSegment() {
        // Allocate a new segment if the current one is full,
        // then compress the previous one.
        if (activeSegment_ == null || activeSegment_.Count == segmentLength_) {
            if (activeSegment_ != null) {
                activeSegment_.CompressValues();
            }

            activeSegment_ = new Segment(this, segmentLength_);
            segments_.Add(activeSegment_);
        }
    }

    private Task ScheduleCompressionTask(Action action) {
        var task = new Task(action);

        if(!taskQueue_.TryAdd(task)) {
            task.RunSynchronously();
        }
        
        return task;
    }

    private void SetupCompressionThreads() {
        taskQueue_ = new BlockingCollection<Task>();
        taskQueueThreadTasks_ = new List<Task>();
        int threads = 1 + prefetchLimit_; // 1 used for compression.

        for (int i = 0; i < threads; i++) {
            taskQueueThreadTasks_.Add(Task.Run(() => {
                try {
                    while (!taskQueue_.IsCompleted) {
                        if (taskQueue_.TryTake(out var task, 1000)) {
                            // Execute the actual (de)compression task.
                            task.RunSynchronously();
                        }
                    }
                }
                catch (Exception ex) {
                    Trace.WriteLine($"Failure executing compression task: {ex.Message}");
                }
            }));
        }
    }

    public void Clear() {
        segments_.Clear();
        activeSegment_ = null;
        count_ = 0;
#if DEBUG
        version_++;
#endif
    }

    public bool Contains(T item) {
        foreach (var value in this) {
            if (value.Equals(item)) {
                return true;
            }
        }

        return false;
    }

    public int IndexOf(T item) {
        int index = 0;

        foreach (var value in this) {
            if (value.Equals(item)) {
                return index;
            }

            index++;
        }

        return -1;
    }

    public void Insert(int index, T item) {
        throw new NotImplementedException();
    }

    public void RemoveAt(int index) {
        throw new NotImplementedException();
    }

    public bool Remove(T item) {
        throw new NotImplementedException();
    }

    public void CopyTo(T[] array, int arrayIndex) {
        if(array.Length + arrayIndex < count_) {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex), "Array length too small");
        }

        foreach(var segment in segments_) {
            segment.CopyTo(array, arrayIndex);
            arrayIndex += segment.Count;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetValue(int index) {
        Debug.Assert(index < count_);
        int activeIndex = GetActiveSegmentIndex(index);

        if (activeIndex != -1) {
            return activeSegment_.GetValue(activeIndex);
        }

        var segment = segments_[index / segmentLength_];
        return segment.GetValue(index % segmentLength_);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetValueRef(int index) {
        Debug.Assert(index < count_);
        int activeIndex = GetActiveSegmentIndex(index);

        if (activeIndex != -1) {
            return ref activeSegment_.GetValueRef(activeIndex);
        }

        var segment = segments_[index / segmentLength_];
        return ref segment.GetValueRef(index % segmentLength_);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValue(int index, T value) {
        Debug.Assert(index < count_);
        int activeIndex = GetActiveSegmentIndex(index);

        if (activeIndex != -1) {
            activeSegment_.SetValue(activeIndex, value);
        }
        else {
            var segment = segments_[index / segmentLength_];
            segment.SetValue(index % segmentLength_, value);
        }
#if DEBUG
        version_++;
#endif
    }

    private int GetActiveSegmentIndex(int index) {
        // Check if index is in the active segment to avoid the expensive DIV below.
        if (activeSegment_ != null) {
            int startIndex = segments_.Count * segmentLength_;

            if (index >= startIndex &&
                index < startIndex + segments_[^1].Count) {
                return index - startIndex;
            }
        }

        return -1;
    }

    public T this[int index] {
        get => GetValue(index);
        set => SetValue(index, value);
    }

    public IEnumerator<T> GetEnumerator() {
        int segmentIndex = 0;
        int lastPrefetchIndex = 0;

#if DEBUG
        int initialVersion = version_;
#endif

        foreach (var segment in segments_) {
            segment.DecompressValues();
            int count = segment.Count;

            for (int i = 0; i < count; i++) {
#if DEBUG
                if(initialVersion != version_) {
                    throw new InvalidOperationException("List modified, potential thread racing bug");
                }
#endif

                yield return segment.GetValueDirect(i);
            }

            // Prefetch segments in advance on another thread
            // to hide the delay of decompressing the data.
            if(prefetchLimit_ > 0 && segmentIndex + prefetchLimit_ < segments_.Count) {
                for(; lastPrefetchIndex < segmentIndex + prefetchLimit_; lastPrefetchIndex++) {
                    segments_[lastPrefetchIndex].DecompressValues(true);
                }
            }

            // Re-compress the values, done on another thread.
            segment.CompressValues();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    public override bool Equals(object? obj) {
        return obj is CompressedSegmentedList<T> list &&
               EqualityComparer<List<CompressedSegmentedList<T>.Segment>>.Default.Equals(segments_, list.segments_) &&
               count_ == list.count_ &&
               segmentLength_ == list.segmentLength_;
    }

    public override int GetHashCode() {
        return HashCode.Combine(segments_, count_, segmentLength_);
    }
}
