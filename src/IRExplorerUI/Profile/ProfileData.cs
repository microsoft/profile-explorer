using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Windows.Markup;
using CSScriptLib;
using HarfBuzzSharp;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using Microsoft.Diagnostics.Tracing.Parsers.FrameworkEventSource;
using Microsoft.Windows.EventTracing;
using Microsoft.Windows.EventTracing.Processes;
using ProtoBuf;

namespace IRExplorerUI.Profile {
    [ProtoContract(SkipConstructor = true)]
    public class PerformanceCounterInfo {
        [ProtoMember(1)]
        public int Id { get; set; }
        [ProtoMember(2)]
        public int Number { get; set; }
        [ProtoMember(3)]
        public string Name { get; set; }
        [ProtoMember(4)]
        public string Description { get; set; }
        [ProtoMember(5)]
        public int Frequency { get; set; }
    }

    //? TODO: Can't be changed to struct because updating Value
    //? needs a new instance of the struct when used in List<T>.
    //? Make a List<T> alternative that allows getting the inner array,
    //? then ref locals will work.
    // https://devblogs.microsoft.com/premier-developer/performance-traps-of-ref-locals-and-ref-returns-in-c/
    [ProtoContract(SkipConstructor = true)]
    public class PerformanceCounterValue : IEquatable<PerformanceCounterValue> {
        [ProtoMember(1)]
        public int CounterId { get; set; }
        [ProtoMember(2)]
        public long Value { get; set; }

        public PerformanceCounterValue(int counterId, long value = 0) {
            CounterId = counterId;
            Value = value;
        }

        public bool Equals(PerformanceCounterValue other) {
            return CounterId == other.CounterId && Value == other.Value;
        }

        public override bool Equals(object obj) {
            return obj is PerformanceCounterValue other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(CounterId, Value);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class PerformanceCounterSet {
        //? Use smth like https://github.com/faustodavid/ListPool/blob/main/src/ListPool/ValueListPool.cs
        //? and make PerformanceCounterSet as struct.
        [ProtoMember(1)]
        public List<PerformanceCounterValue> Counters { get; set; }

        public PerformanceCounterSet() {
            InitializeReferenceMembers();
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            Counters ??= new List<PerformanceCounterValue>();
        }

        public void AddCounterSample(int perfCounterId, long value) {
            PerformanceCounterValue counter;
            var index = Counters.FindIndex((item) => item.CounterId == perfCounterId);

            if (index != -1) {
                counter = Counters[index];
            }
            else {
                // Keep the list sorted so that it is in sync
                // with the sorted counter definition list.
                counter = new PerformanceCounterValue(perfCounterId);
                int insertionIndex = 0;

                for(int i = 0; i < Counters.Count; i++, insertionIndex++) {
                    if(Counters[i].CounterId >= perfCounterId) {
                        break;
                    }
                }

                Counters.Insert(insertionIndex, counter);
            }

            counter.Value += value;
        }

        public long FindCounterValue(int perfCounterId) {
            var index = Counters.FindIndex((item) => item.CounterId == perfCounterId);
            return index != -1 ? Counters[index].Value : 0;
        }

        public long FindCounterValue(PerformanceCounterInfo counter) {
            return FindCounterValue(counter.Id);
        }

        //? TODO: Use Utils.Accumulate, and some small dict
        public void Add(PerformanceCounterSet other) {
            foreach(var counter in other.Counters) {
                var index = Counters.FindIndex((item) => item.CounterId == counter.CounterId);

                if(index != -1) {
                    //? TODO: Once List is replaced use a ref local to change only the Value field.
                    Counters[index].Value += counter.Value;
                }
                else {
                    Counters.Add(new PerformanceCounterValue(counter.CounterId, counter.Value));
                }
            }
        }

        public long this[int perfCounterId] => FindCounterValue(perfCounterId);
    }

    [ProtoContract(SkipConstructor = true)]
    public class ProfileStack : IEquatable<ProfileStack> {
        private const int MaxFrameNumber = 256;
        private static long[][] tempFrameArrays_ = new long[MaxFrameNumber + 1][];

        static ProfileStack() {
            // The frame ptr. array is being interned (unique instance)
            // later, use a pre-allocated array initially to reduce GC pressure.
            // Note: this also means creating ProfileStack must be single-threaded.
            for (int i = 0; i <= MaxFrameNumber; i++) {
                tempFrameArrays_[i] = new long[i];
            }
        }

        public ProfileStack() {
            FramePointers = null;
            ContextId = 0;
        }

        public ProfileStack(int contextId, int frameCount) {
            FramePointers = null;
            ContextId = contextId;
            FramePointers = RentArray(frameCount);
        }

        private object optionalData_;

        [ProtoMember(1)]
        public long[] FramePointers { get; set; }
        [ProtoMember(2)]
        public int ContextId { get; set; }

        public bool IsUnknown => FramePointers == null;
        public int FrameCount => FramePointers.Length;

        public static ProfileStack Unknown => new ProfileStack();

        public object GetOptionalData() {
            Interlocked.MemoryBarrierProcessWide();
            return optionalData_;
        }

        public void SetOptionalData(object value) {
            optionalData_ = value;
            Interlocked.MemoryBarrierProcessWide();

        }

        public long[] CloneFramePointers() {
            long[] clone = new long[FramePointers.Length];
            FramePointers.CopyTo(clone, 0);
            return clone;
        }

        public void SetTempFramePointers(int frameCount) {
            FramePointers = RentArray(frameCount);
        }

        public void SubstituteFramePointers(long[] data) {
            ReturnArray(FramePointers);
            FramePointers = data;
        }

        public ProfileImage FindImageForFrame(int frameIndex, RawProfileData profileData) {
            return profileData.FindImageForIP(FramePointers[frameIndex], profileData.FindContext(ContextId));
        }

        private long[] RentArray(int frameCount) {
            // In most cases one of the pre-allocated temporary arrays can be used.
            long[] array;

            if (frameCount <= MaxFrameNumber) {
                array = tempFrameArrays_[frameCount];
#if DEBUG
                Debug.Assert(array != null);
                tempFrameArrays_[frameCount] = null;
#endif
            }
            else {
                array = new long[frameCount];
            }

            return array;
        }

        private void ReturnArray(long[] array) {
#if DEBUG
            if (array.Length <= MaxFrameNumber) {
                tempFrameArrays_[array.Length] = array;
            }
#endif
        }

        public bool Equals(ProfileStack other) {
            Debug.Assert(other != null);

            if (ReferenceEquals(this, other)) {
                return true;
            }

            // FramePointers is allocated using interning, ref. equality is sufficient.
            return ContextId == other.ContextId &&
                   FramePointers == other.FramePointers;
        }

        public override bool Equals(object obj) {
            return obj is ProfileStack other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(FramePointers, ContextId);
        }

        public static bool operator ==(ProfileStack left, ProfileStack right) {
            return Equals(left, right);
        }

        public static bool operator !=(ProfileStack left, ProfileStack right) {
            return !Equals(left, right);
        }

        public override string ToString() {
            return $"#{FrameCount}, ContextId: {ContextId}";
        }
    }
    
    [ProtoContract(SkipConstructor = true)]
    public struct ProfileSample : IEquatable<ProfileSample> {
        [ProtoMember(1)]
        public long IP { get; set; }
        [ProtoMember(2)]
        public TimeSpan Time { get; set; }
        [ProtoMember(3)]
        public TimeSpan Weight { get; set; }
        [ProtoMember(4)]
        public int StackId { get; set; }
        [ProtoMember(5)]
        public int ContextId { get; set; }
        [ProtoMember(6)]
        public bool IsKernelCode { get; set; }

        public bool HasStack => StackId != 0;

        //public ProfileSample() {}

        public ProfileSample(long ip, TimeSpan time, TimeSpan weight, bool isKernelCode, int contextId) {
            IP = ip;
            Time = time;
            Weight = weight;
            StackId = 0;
            IsKernelCode = isKernelCode;
            ContextId = contextId;
        }

        public ProfileStack GetStack(RawProfileData profileData) {
            return StackId != 0 ? profileData.FindStack(StackId) : ProfileStack.Unknown;
        }

        public ProfileContext GetContext(RawProfileData profileData) {
            return profileData.FindContext(ContextId);
        }

        public bool Equals(ProfileSample other) {
            return IP == other.IP &&
                   Time == other.Time &&
                   Weight == other.Weight &&
                   StackId == other.StackId &&
                   ContextId == other.ContextId &&
                   IsKernelCode == other.IsKernelCode;
        }

        public override bool Equals(object obj) {
            return obj is ProfileSample other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(IP, Time, StackId, ContextId);
        }

        public static bool operator ==(ProfileSample left, ProfileSample right) {
            return Equals(left, right);
        }

        public static bool operator !=(ProfileSample left, ProfileSample right) {
            return !Equals(left, right);
        }

        public override string ToString() {
            return $"{IP}, Weight: {Weight.Ticks}, StackId: {StackId}";
        }
    }


    [ProtoContract(SkipConstructor = true)]
    public class ProfileSample2 : IEquatable<ProfileSample2> {
        [ProtoMember(1)]
        public long IP { get; set; }
        [ProtoMember(2)]
        public List<TimeSpan> Time { get; set; }
        [ProtoMember(3)]
        public TimeSpan Weight { get; set; }
        [ProtoMember(4)]
        public int StackId { get; set; }
        [ProtoMember(5)]
        public int ContextId { get; set; }
        [ProtoMember(6)]
        public bool IsKernelCode { get; set; }

        public bool HasStack => StackId != 0;

        public ProfileSample2() {
            Time = new List<TimeSpan>();
        }

        public ProfileSample2(long ip,  TimeSpan weight, bool isKernelCode, int contextId) {
            IP = ip;
            Time = new List<TimeSpan>();
            Weight = weight;;
            StackId = 0;
            IsKernelCode = isKernelCode;
            ContextId = contextId;
        }

        public ProfileStack GetStack(RawProfileData profileData) {
            return StackId != 0 ? profileData.FindStack(StackId) : ProfileStack.Unknown;
        }

        public ProfileContext GetContext(RawProfileData profileData) {
            return profileData.FindContext(ContextId);
        }

        public bool Equals(ProfileSample2 other) {
            return IP == other.IP &&
                   StackId == other.StackId &&
                   ContextId == other.ContextId &&
                   IsKernelCode == other.IsKernelCode;
        }

        public override bool Equals(object obj) {
            return obj is ProfileSample2 other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(IP, StackId, ContextId);
        }

        public static bool operator ==(ProfileSample2 left, ProfileSample2 right) {
            return Equals(left, right);
        }

        public static bool operator !=(ProfileSample2 left, ProfileSample2 right) {
            return !Equals(left, right);
        }

        public override string ToString() {
            return $"{IP}, Weight: {Weight.Ticks}, StackId: {StackId}";
        }
    }


    [ProtoContract(SkipConstructor = true)]
    public sealed class ProfileContext : IEquatable<ProfileContext> {
        public ProfileContext() {}

        public ProfileContext(int processId, int threadId, int processorNumber) {
            ProcessId = processId;
            ThreadId = threadId;
            ProcessorNumber = processorNumber;
        }

        [ProtoMember(1)]
        public int ProcessId { get; set; }
        [ProtoMember(2)]
        public int ThreadId { get; set; }
        [ProtoMember(3)]
        public int ProcessorNumber { get; set; }

        public ProfileProcess GetProcess(RawProfileData profileData) {
            return profileData.GetOrCreateProcess(ProcessId);
        }

        public ProfileThread GetThread(RawProfileData profileData) {
            return profileData.FindThread(ThreadId);
        }

        public bool Equals(ProfileContext other) {
            Debug.Assert(other != null);
            
            if (ReferenceEquals(this, other)) {
                return true;
            }

            return ProcessId == other.ProcessId &&
                   ThreadId == other.ThreadId &&
                   ProcessorNumber == other.ProcessorNumber;
        }

        public override bool Equals(object other) {
            Debug.Assert(other != null);

            if (ReferenceEquals(this, other)) {
                return true;
            }

            if (other.GetType() != this.GetType()) {
                return false;
            }

            return Equals((ProfileContext)other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(ProcessId, ThreadId, ProcessorNumber);
        }

        public static bool operator ==(ProfileContext left, ProfileContext right) {
            return Equals(left, right);
        }

        public static bool operator !=(ProfileContext left, ProfileContext right) {
            return !Equals(left, right);
        }

        public override string ToString() {
            return $"ProcessId: {ProcessId}, ThreadId: {ThreadId}, Processor: {ProcessorNumber}";
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public sealed class ProfileImage : IEquatable<ProfileImage>, IComparable<ProfileImage> {
        [ProtoMember(1)]
        public int Id { get; set; }
        [ProtoMember(2)]
        public int Size { get; set; }
        [ProtoMember(3)]
        public long BaseAddress { get; set; }
        [ProtoMember(4)]
        public long DefaultBaseAddress { get; set; }
        [ProtoMember(5)]
        public string FilePath { get; set; }
        [ProtoMember(6)]
        public string OriginalFileName { get; set; }
        [ProtoMember(7)]
        public long TimeStamp { get; set; }
        [ProtoMember(8)]
        public long Checksum { get; set; }

        public long BaseAddressEnd => BaseAddress + Size;

        public string ModuleName => !string.IsNullOrWhiteSpace(OriginalFileName) ? Utils.TryGetFileName(OriginalFileName) : FilePath;

        public ProfileImage() {}

        public ProfileImage(string filePath, string originalFileName, 
                            long baseAddress, long defaultBaseAddress, 
                            int size, long timeStamp, long checksum) {
            Size = size;
            FilePath = filePath;
            OriginalFileName = originalFileName;
            BaseAddress = baseAddress;
            DefaultBaseAddress = defaultBaseAddress;
            TimeStamp = timeStamp;
            Checksum = checksum;
        }

        public bool HasAddress(long ip) {
            return (ip >= BaseAddress) && (ip < (BaseAddress + Size));
        }

        public override int GetHashCode() {
            return HashCode.Combine(Size, FilePath, BaseAddress, DefaultBaseAddress, Checksum);
        }

        public bool Equals(ProfileImage other) {
            Debug.Assert(other != null);

            if (ReferenceEquals(this, other)) {
                return true;
            }

            return Size == other.Size &&
                   FilePath == other.FilePath &&
                   BaseAddress == other.BaseAddress &&
                   DefaultBaseAddress == other.DefaultBaseAddress &&
                   Checksum == other.Checksum;
        }

        public override bool Equals(object other) {
            Debug.Assert(other != null);

            if (ReferenceEquals(this, other)) {
                return true;
            }

            if (other.GetType() != this.GetType()) {
                return false;
            }

            return Equals((ProfileImage)other);
        }

        public static bool operator ==(ProfileImage left, ProfileImage right) {
            return Equals(left, right);
        }

        public static bool operator !=(ProfileImage left, ProfileImage right) {
            return !Equals(left, right);
        }

        public int CompareTo(ProfileImage other) {
            if (BaseAddress < other.BaseAddress && BaseAddressEnd < other.BaseAddressEnd) {
                return -1;
            }
            if (BaseAddress > other.BaseAddress && BaseAddressEnd > other.BaseAddressEnd) {
                return 1;
            }
            return 0;
        }

        public int CompareTo(long value) {
            if (value < BaseAddress) {
                return 1;
            }
            if (value > BaseAddressEnd) {
                return -1;
            }

            return 0;
        }

        public override string ToString() {
            return $"{FilePath}, Id: {Id}, Base: {BaseAddress:X}, Size: {Size}";
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public sealed class ProfileProcess : IEquatable<ProfileProcess> {
        [ProtoMember(1)]
        public int ProcessId { get; set; }
        [ProtoMember(2)]
        public int ParentId { get; set; }
        [ProtoMember(3)]
        public string Name { get; set; }
        [ProtoMember(4)]
        public string ImageFileName { get; set; }
        [ProtoMember(5)]
        public string CommandLine { get; set; }
        [ProtoMember(6)]
        public List<int> ImageIds { get; set; }
        [ProtoMember(7)]
        public List<int> ThreadIds { get; set; }

        public ProfileProcess() {
            ImageIds = new List<int>();
            ThreadIds = new List<int>();
        }

        public IEnumerable<ProfileImage> Images(RawProfileData profileData) {
            foreach (var id in ImageIds) {
                yield return profileData.FindImage(id);
            }
        }

        public IEnumerable<ProfileThread> Threads(RawProfileData profileData) {
            foreach (var id in ThreadIds) {
                yield return profileData.FindThread(id);
            }
        }

        public ProfileProcess(int processId, int parentId, string name, 
                              string imageFileName, string commandLine) : this() {
            ProcessId = processId;
            ParentId = parentId;
            Name = name;
            ImageFileName = imageFileName;
            CommandLine = commandLine;
        }

        public void AddImage(int imageId) {
            if (!ImageIds.Contains(imageId)) {
                ImageIds.Add(imageId);
            }
        }

        public void AddThread(int threadId) {
            if (!ThreadIds.Contains(threadId)) {
                ThreadIds.Add(threadId);
            }
        }

        public bool Equals(ProfileProcess other) {
            Debug.Assert(other != null);

            if (ReferenceEquals(this, other)) {
                return true;
            }

            return ProcessId == other.ProcessId &&
                   Name == other.Name;
        }

        public override bool Equals(object other) {
            Debug.Assert(other != null);

            if (ReferenceEquals(this, other)) {
                return true;
            }

            if (other.GetType() != this.GetType()) {
                return false;
            }

            return Equals((ProfileProcess)other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(ProcessId, Name);
        }

        public static bool operator ==(ProfileProcess left, ProfileProcess right) {
            return Equals(left, right);
        }

        public static bool operator !=(ProfileProcess left, ProfileProcess right) {
            return !Equals(left, right);
        }

        public override string ToString() {
            return $"{Name}, ID: {ProcessId}, ParentID: {ParentId}, Images: {ImageIds.Count}, Threads: {ThreadIds.Count}";
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public sealed class ProfileThread : IEquatable<ProfileThread> {
        [ProtoMember(1)]
        public int ThreadId { get; set; }
        [ProtoMember(2)]
        public int ProcessId { get; set; }
        [ProtoMember(3)]
        public string Name { get; set; }

        public ProfileThread() { }

        public ProfileThread(int threadId, int processId, string name) {
            ThreadId = threadId;
            ProcessId = processId;
            Name = name;
        }

        public bool Equals(ProfileThread other) {
            Debug.Assert(other != null);

            if (ReferenceEquals(this, other)) {
                return true;
            }

            return ThreadId == other.ThreadId && ProcessId == other.ProcessId;
        }

        public override bool Equals(object other) {
            Debug.Assert(other != null);

            if (ReferenceEquals(this, other)) {
                return true;
            }

            if (other.GetType() != this.GetType()) {
                return false;
            }

            return Equals((ProfileThread)other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(ThreadId, ProcessId);
        }

        public static bool operator ==(ProfileThread left, ProfileThread right) {
            return Equals(left, right);
        }

        public static bool operator !=(ProfileThread left, ProfileThread right) {
            return !Equals(left, right);
        }

        public override string ToString() {
            return $"{ThreadId}, ProcessId: {ProcessId}, Name: {Name}";
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public struct PerformanceCounterEvent : IEquatable<PerformanceCounterEvent> {
        [ProtoMember(1)]
        public long Time;
        [ProtoMember(2)]
        public long IP;
        //? TODO: everything after is shared by many, use flyweight
        //? Use ProfileEventContext
        [ProtoMember(3)]
        public int ProcessId;
        [ProtoMember(4)]
        public int ThreadId;
        [ProtoMember(5)]
        public short ProfilerSource;

        public bool Equals(PerformanceCounterEvent other) {
            return Time == other.Time && IP == other.IP && 
                ProcessId == other.ProcessId &&
                ThreadId == other.ThreadId && 
                ProfilerSource == other.ProfilerSource;
        }

        public override bool Equals(object obj) {
            return obj is PerformanceCounterEvent other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(Time, IP);
        }

        /// enum eventType (pmc, context switch, etc)
    }

    [ProtoContract(SkipConstructor = true)]
    public class RawProfileData {
        public List<ProfileProcess> Processes => processes_.ToValueList();
        public ChunkedList<PerformanceCounterEvent> PerfCounters { get; set; }

        //? Make fields private
        public Dictionary<int, ProfileProcess> processes_;
        public List<ProfileThread> threads_;
        public Dictionary<ProfileThread, int> threadsMap_;

        public List<ProfileContext> contexts_;
        public Dictionary<ProfileContext, int> contextsMap_;

        public List<ProfileImage> images_;
        public Dictionary<ProfileImage, int> imagesMap_;

        public List<ProfileStack> stacks_;
        public Dictionary<int, Dictionary<ProfileStack, int>> stacksMap_;
        private HashSet<long[]> stackData_;
        private Dictionary<ProfileStack, int> lastProcStacks_;
        private int lastProcId_;

        public List<ProfileSample> samples_;

        private static ProfileContext tempContext_ = new ProfileContext();
        private static ProfileStack tempStack_ = new ProfileStack();

        [ThreadStatic] 
        private static List<(int ProcessId, IpToImageCache Cache)> ipImageCache_;
        [ThreadStatic]
        private static ProfileImage lastIpImage_;

        [ThreadStatic] 
        private static IpToImageCache globalIpImageCache_;


        [ProtoContract(SkipConstructor = true)]
        class SampleHolder {
            [ProtoMember(1)]
            public List<ProfileSample> samples { get; set; }
        }

        private class IpToImageCache {
            private List<ProfileImage> images_;
            private long lowestBaseAddress_;

            public IpToImageCache(IEnumerable<ProfileImage> images, long lowestBaseAddress) {
                lowestBaseAddress_ = lowestBaseAddress;
                images_ = new List<ProfileImage>(images);
                images_.Sort();
            }

            public static IpToImageCache Create(IEnumerable<ProfileImage> images) {
                long lowestAddr = Int64.MaxValue;

                foreach (var image in images) {
                    lowestAddr = Math.Min(lowestAddr, image.BaseAddress);
                }

                return new IpToImageCache(images, lowestAddr);
            }

            public bool IsValidAddres(long ip) {
                return ip >= lowestBaseAddress_;
            }
            
            public ProfileImage Find(long ip) {
                Debug.Assert(IsValidAddres(ip));
                return BinarySearch(images_, ip);
            }

            ProfileImage BinarySearch(List<ProfileImage> ranges, long value) {
                int min = 0;
                int max = ranges.Count - 1;

                while (min <= max) {
                    int mid = (min + max) / 2;
                    var range = ranges[mid];
                    int comparison = range.CompareTo(value);

                    if (comparison == 0) {
                        return range;
                    }
                    if (comparison < 0) {
                        min = mid + 1;
                    }
                    else {
                        max = mid - 1;
                    }
                }

                return null;
            }
        }

        public RawProfileData() {
            contexts_ = new List<ProfileContext>();
            contextsMap_ = new Dictionary<ProfileContext, int>();
            images_ = new List<ProfileImage>();
            imagesMap_ = new Dictionary<ProfileImage, int>();
            processes_ = new Dictionary<int, ProfileProcess>();
            threads_ = new List<ProfileThread>();
            threadsMap_ = new Dictionary<ProfileThread, int>();
            stacks_ = new List<ProfileStack>();
            stacksMap_ = new Dictionary<int, Dictionary<ProfileStack, int>>();
            stackData_ = new HashSet<long[]>(new StackComparer());
            samples_ = new List<ProfileSample>();
            PerfCounters = new ChunkedList<PerformanceCounterEvent>();
        }

        public void Done() {
            // Free objects used while reading the profile.
            stacksMap_ = null;
            stackData_ = null;
            lastProcStacks_ = null;
        }

        public ProfileProcess GetOrCreateProcess(int id) {
            return processes_.GetOrAddValue(id);
        }

        public void AddProcess(ProfileProcess process) {
            processes_[process.ProcessId] = process;
        }

        public int AddImage(ProfileImage image) {
            if (!imagesMap_.TryGetValue(image, out var existingImage)) {
                images_.Add(image);
                int id = images_.Count;
                image.Id = id;
                imagesMap_[image] = id;
                existingImage = id;
            }

            return existingImage;
        }

        public ProfileImage FindImage(int id) {
            Debug.Assert(id > 0 && id <= images_.Count);
            return images_[id - 1];
        }

        public int AddThread(ProfileThread thread) {
            if (!threadsMap_.TryGetValue(thread, out var existingThread)) {
                threads_.Add(thread);
                existingThread = threads_.Count;
                threadsMap_[thread] = threads_.Count;
            }

            return existingThread;
        }

        public ProfileThread FindThread(int id) {
            Debug.Assert(id > 0 && id <= threads_.Count);
            return threads_[id - 1];
        }

        public int AddThreadToProcess(int processId, ProfileThread thread) {
            var proc = GetOrCreateProcess(processId);
            var result = AddThread(thread);
            proc.AddThread(result);
            return result;
        }

        public int AddImageToProcess(int processId, ProfileImage image) {
            var proc = GetOrCreateProcess(processId);
            var result = AddImage(image);
            proc.AddImage(result);
            return result;
        }

        //? TODO: Per-process samples, reduces dict pressure
        
        public int AddSample(ProfileSample sample) {
            Debug.Assert(sample.ContextId != 0);
            samples_.Add(sample);
            
            return (int)samples_.Count;
        }

        public bool TrySetSampleStack(int sampleId, int stackId, int contextId) {
            if (sampleId == 0) {
                return false;
            }

            if (samples_[sampleId - 1].ContextId == contextId) {
                SetSampleStack(sampleId, stackId, contextId);
                return true;
            }

            return false;
        }

        public void SetSampleStack(int sampleId, int stackId, int contextId) {
            // Change the stack ID in-place in the array.
            Debug.Assert(samples_[sampleId - 1].ContextId == contextId);
            var span = CollectionsMarshal.AsSpan(samples_);
            ref var sampleRef = ref span[sampleId - 1];
            sampleRef.StackId = stackId;
        }
        
        internal ProfileContext RentTempContext(int processId, int threadId, int processorNumber) {
            tempContext_.ProcessId = processId;
            tempContext_.ThreadId = threadId;
            tempContext_.ProcessorNumber = processorNumber;
            return tempContext_;
        }

        internal void ReturnContext(int contextId) {
            var context = FindContext(contextId);

            if (ReferenceEquals(context, tempContext_)) {
                tempContext_ = new ProfileContext();
            }
        }
        
        internal int AddContext(ProfileContext context) {
            if (!contextsMap_.TryGetValue(context, out var existingContext)) {
                contexts_.Add(context);
                existingContext = contexts_.Count;
                contextsMap_[context] = contexts_.Count;
            }

            return existingContext;
        }

        public ProfileContext FindContext(int id) {
            Debug.Assert(id > 0 && id <= contexts_.Count);
            return contexts_[id - 1];
        }
        
        internal ProfileStack RentTemporaryStack(int frameCount, int contextId) {
            tempStack_.SetTempFramePointers(frameCount);
            tempStack_.ContextId = contextId;
            return tempStack_;
        }

        internal void ReturnStack(int stackId) {
            var stack = FindStack(stackId);

            if (ReferenceEquals(stack, tempStack_)) {
                tempStack_ = new ProfileStack();
            }
        }
        
        //? TODO: Per-process stack, reduces dict pressure
        public int AddStack(ProfileStack stack, ProfileContext context) {
            Debug.Assert(stack.ContextId != 0);
            
            if (!stackData_.TryGetValue(stack.FramePointers, out var framePtrData)) {
                // Make a clone since the temporary stack uses a static array.
                framePtrData = stack.CloneFramePointers();
                stackData_.Add(framePtrData);
            }

            stack.SubstituteFramePointers(framePtrData);
            Dictionary<ProfileStack, int> procStacks = null;

            if (lastProcId_ == context.ProcessId && lastProcStacks_ != null) {
                procStacks = lastProcStacks_;
            }
            else {
                if (!stacksMap_.TryGetValue(context.ProcessId, out procStacks)) {
                    procStacks = new Dictionary<ProfileStack, int>();
                    stacksMap_[context.ProcessId] = procStacks;
                }

                lastProcId_ = context.ProcessId;
                lastProcStacks_ = procStacks;
            }

            if (!procStacks.TryGetValue(stack, out var existingStack)) {
                stacks_.Add(stack);
                procStacks[stack] = (int)stacks_.Count;
                existingStack = (int)stacks_.Count;
            }
            else {
                Debug.Assert(stack == FindStack(existingStack));
            }

            return existingStack;
        }
        
        public ProfileStack FindStack(int id) {
            Debug.Assert(id > 0 && id <= stacks_.Count);
            return stacks_[id - 1];
        }

        public ProfileProcess FindProcess(string name, bool allowSubstring = false) {
            foreach (var process in processes_.Values) {
                if (process.Name == name) {
                    return process;
                }
            }

            if (allowSubstring) {
                foreach (var process in processes_.Values) {
                    if (process.Name.Contains(name)) {
                        return process;
                    }
                }
            }

            return null;
        }

        public ProfileImage FindImage(ProfileProcess process, BinaryFileDescription info) {
            foreach (var image in process.Images(this)) {
                if (image.FilePath == info.ImageName &&
                    image.TimeStamp == info.TimeStamp &&
                    image.Checksum == info.Checksum) {
                    return image;
                }
            }

            return null;
        }

        public ProfileImage FindImageForIP(long ip, ProfileContext context) {
            return FindImageForIP(ip, context.ProcessId, context.ThreadId);
        }

        private ProfileImage FindImageForIP(long ip, int processId, int threadId) {
            // lastIpImage_ and ipImageCache_ are thread-local,
            // making this function thread-safe.
            if (lastIpImage_ != null && lastIpImage_.HasAddress(ip)) {
                return lastIpImage_;
            }

            ipImageCache_ ??= new List<(int ProcessId, IpToImageCache Cache)>();
            IpToImageCache cache = null;

            foreach (var entry in ipImageCache_) {
                if (entry.ProcessId == processId) {
                    cache = entry.Cache;
                    break;
                }
            }

            if (cache == null) {
                var process = GetOrCreateProcess(processId);
                cache = IpToImageCache.Create(process.Images(this));
                ipImageCache_.Add((processId, cache));
            }

            if (!cache.IsValidAddres(ip)) {
                return null;
            }

            var result = cache.Find(ip);

            if (result != null) {
                lastIpImage_ = result;
                return result;
            }
            
            // Trace.WriteLine($"No image for ip {ip:X}");
            return null;
        }

        public ProfileImage FindImageForIP(long ip) {
            if (globalIpImageCache_ == null) {
                globalIpImageCache_ = IpToImageCache.Create(images_);
            }

            return globalIpImageCache_.Find(ip);
        }

        private HashSet<ProfileSample2> sampleSet_ = new HashSet<ProfileSample2>();


        public void PrintSamples(int processId) {
            //SampleHolder h = new SampleHolder() { samples = samples_ };

            //var d = StateSerializer.Serialize(h);
            //Trace.WriteLine($"Serialized to {d.Length}b");
            //Trace.Flush();
            //File.WriteAllBytes(@"C:\test\samples.dat", d);
            
            foreach (var s in samples_) {
                var s2 = new ProfileSample2() {
                    IP = s.IP,
                    StackId = s.StackId,
                    ContextId = s.ContextId,
                    IsKernelCode = s.IsKernelCode
                };

                if (!sampleSet_.TryGetValue(s2, out var es2)) {
                    sampleSet_.Add(s2);
                    es2 = s2;
                }

                es2.Weight += s.Weight;
                es2.Time.Add(s.Time);
            }

            List<int> counts = sampleSet_.Select(s => s.Time.Count).ToList();
            int max = sampleSet_.Max(s => s.Time.Count);
            counts.Sort();
            int med = counts[counts.Count / 2];

            int memUsage = samples_.Count * Unsafe.SizeOf<ProfileSample>();
            int memUsage2 = sampleSet_.Count * Unsafe.SizeOf<ProfileSample>() +
                            sampleSet_.Select(s => s.Time.Count * 8).Sum();
            int memUsage3 = sampleSet_.Count * Unsafe.SizeOf<ProfileSample>() +
                            sampleSet_.Select(s => s.Time.Capacity * 8).Sum();

            Trace.WriteLine($"Total samples: {samples_.Count}");
            Trace.WriteLine($"    same IP: {sampleSet_.Count}, {100*(double)sampleSet_.Count / samples_.Count}%, median {med}, max {max}");
            Trace.WriteLine($"    memUsage:           {memUsage}, {(double)memUsage / (1024 * 1024)} MB");
            Trace.WriteLine($"    memUsage2 size:     {memUsage2}, {(double)memUsage2 / (1024 * 1024)} MB");
            Trace.WriteLine($"    memUsage2 capacity: {memUsage3}, {(double)memUsage3 / (1024 * 1024)} MB");

        }

        //? TODO Perf
        //? - use chunked list for samples and stack
        //? - use chunked dict?  
        //? - compress stacks?
        //? - Per-process stacks and samples, reduces dict pressure
        //?     - also removes need to have ProcessId in sample

        private class StackComparer : IEqualityComparer<long[]> {
            public unsafe bool Equals(long[] data1, long[] data2) {
                fixed (long* p1 = data1, p2 = data2) {
                    return new Span<long>(p1, data1.Length).
                        SequenceEqual(new Span<long>(p2, data2.Length));
                }
            }

            public int GetHashCode(long[] data) {
                int hash = 0;
                int left = data.Length;
                int i = 0;

                while (left >= 4) {
                    hash = HashCode.Combine(hash, data[i], data[i + 1], data[i + 2], data[i + 3]);
                    left -= 4;
                    i += 4;
                }

                while (left > 0) {
                    hash = HashCode.Combine(hash, data[i]);
                    left--;
                    i++;
                }

                return hash;
            }
        }
    }

    //! Sparse BV could be used to keep track of samples for each function
    //     should work at least for total func, but maybe small enough for TreeNode
    public class ProfileCallTreeNode : IEquatable<ProfileCallTreeNode> {
      
        public IRTextFunction Function { get; set; }
        public DebugFunctionInfo DebugInfo { get; set; }
        public TimeSpan Weight { get; set; }
        public TimeSpan ExclusiveWeight { get; set; }
        
        private List<ProfileCallTreeNode> children_;
        private List<ProfileCallTreeNode> callers_;
        private ReaderWriterLockSlim lock_;

        public List<ProfileCallTreeNode> Callers => callers_;
        public List<ProfileCallTreeNode> Children => children_;

        public bool HasChildren => Children != null && Children.Count > 0;
        public string FunctionName => DebugInfo.Name;

        public ProfileCallTreeNode(DebugFunctionInfo funcInfo, IRTextFunction function) {
            DebugInfo = funcInfo;
            Function = function;
            lock_ = new ReaderWriterLockSlim();
        }

        public void AccumulateWeight(TimeSpan weight) {
            lock_.EnterWriteLock();
            Weight += weight;
            lock_.ExitWriteLock();
        }

        public void AccumulateExclusiveWeight(TimeSpan weight) {
            lock_.EnterWriteLock();
            ExclusiveWeight += weight;
            lock_.ExitWriteLock();
        }

        private (ProfileCallTreeNode, bool) GetOrCreateNode(ref List<ProfileCallTreeNode> list,
                                                    DebugFunctionInfo debugInfo, IRTextFunction function) {
            lock_.EnterUpgradeableReadLock();

            try {
                if (list != null) {
                    foreach (var child in list) {
                        if (child.Equals(debugInfo, function)) {
                            return (child, false);
                        }
                    }
                }
                
                lock_.EnterWriteLock();
                try {
                    list ??= new List<ProfileCallTreeNode>();
                    var childNode = new ProfileCallTreeNode(debugInfo, function);
                    list.Add(childNode);
                    return (childNode, true);
                }
                finally {
                    lock_.ExitWriteLock();
                }
            }
            finally {
                lock_.ExitUpgradeableReadLock();
            }
        }

        public (ProfileCallTreeNode, bool) AddChild(DebugFunctionInfo debugInfo, IRTextFunction function) {
            var (childNode, isNewNode) = GetOrCreateNode(ref children_, debugInfo, function);

            if (isNewNode) {
                childNode.AddParent(this);
            }

            return (childNode, isNewNode);
        }

        private Dictionary<long, ProfileSample> samples_;
        private int sampleCount_;

        public void RecordSample(ProfileSample sample, ResolvedProfileStackFrame stackFrame) {
            //lock_.EnterWriteLock();
            //try {
            //    samples_ ??= new Dictionary<long, ProfileSample>();
            //    samples_[stackFrame.FrameIP] = sample;
            //    sampleCount_++;
            //}
            //finally {
            //    lock_.ExitWriteLock();
            //}
        }

        private void AddParent(ProfileCallTreeNode parentNode) {
            GetOrCreateNode(ref callers_, parentNode.DebugInfo, parentNode.Function);
        }

        internal void Print(StringBuilder builder, int level = 0) {
            builder.Append(new string(' ', level * 4));
            builder.AppendLine($"{DebugInfo.Name}, RVA {DebugInfo.RVA}");
            builder.Append(new string(' ', level * 4));
            builder.AppendLine($"        weight {Weight.TotalMilliseconds}");
            builder.Append(new string(' ', level * 4));
            builder.AppendLine($"    exc weight {ExclusiveWeight.TotalMilliseconds}");
            builder.Append(new string(' ', level * 4));
            builder.AppendLine($"    callees: {(Children != null ? Children.Count : 0)}");

            if (Children != null) {
                foreach (var child in Children) {
                    child.Print(builder, level + 1);
                }
            }
        }

        public void CollectSamples(List<(int total, int unique, string name)> list) {
            list.Add((sampleCount_, samples_.Count, FunctionName));

            if (HasChildren) {
                foreach (var child in Children) {
                    child.CollectSamples(list);
                }
            }
        }
        
        public bool Equals(DebugFunctionInfo debugInfo, IRTextFunction function) {
            return Function.Equals(function) &&
                   DebugInfo.Equals(debugInfo);
        }

        public bool Equals(ProfileCallTreeNode other) {
            if (ReferenceEquals(null, other)) {
                return false;
            }

            if (ReferenceEquals(this, other)) {
                return true;
            }

            return Function.Equals(other.Function) &&
                   DebugInfo.Equals(other.DebugInfo);
        }

        public override bool Equals(object obj) {
            if (ReferenceEquals(null, obj)) {
                return false;
            }

            if (ReferenceEquals(this, obj)) {
                return true;
            }

            if (obj.GetType() != this.GetType()) {
                return false;
            }

            return Equals((ProfileCallTreeNode)obj);
        }

        public override int GetHashCode() {
            return HashCode.Combine(Function, DebugInfo);
        }

        public static bool operator ==(ProfileCallTreeNode left, ProfileCallTreeNode right) {
            return Equals(left, right);
        }

        public static bool operator !=(ProfileCallTreeNode left, ProfileCallTreeNode right) {
            return !Equals(left, right);
        }
    }

    public class ProfileCallTree {
        private HashSet<ProfileCallTreeNode> rootNodes_;
        private Dictionary<IRTextFunction, List<ProfileCallTreeNode>> funcToNodesMap_;
        private ReaderWriterLockSlim lock_;
        private ReaderWriterLockSlim funcLock_;

        public HashSet<ProfileCallTreeNode> RootNodes => rootNodes_;

        public ProfileCallTree() {
            rootNodes_ = new HashSet<ProfileCallTreeNode>();
            funcToNodesMap_ = new Dictionary<IRTextFunction, List<ProfileCallTreeNode>>();
            lock_ = new ReaderWriterLockSlim();
            funcLock_ = new ReaderWriterLockSlim();
        }

        public ProfileCallTreeNode AddRootNode(DebugFunctionInfo funcInfo, IRTextFunction function) {
            var node = new ProfileCallTreeNode(funcInfo, function);
            lock_.EnterUpgradeableReadLock();

            try {
                if (rootNodes_.TryGetValue(node, out var existingNode)) {
                    return existingNode;
                }

                lock_.EnterWriteLock();
                try {
                    rootNodes_.Add(node);
                    RegisterFunctionTreeNode(node);
                }
                finally {
                    lock_.ExitWriteLock();
                }   
            }
            finally {
                lock_.ExitUpgradeableReadLock();
            }

            return node;
        }

        public ProfileCallTreeNode AddChildNode(ProfileCallTreeNode node, DebugFunctionInfo funcInfo, IRTextFunction function) {
            var (childNode, isNewNode) = node.AddChild(funcInfo, function);

            if (isNewNode) {
                RegisterFunctionTreeNode(childNode);
            }

            return childNode;
        }

        public void RegisterFunctionTreeNode(ProfileCallTreeNode node) {
            List<ProfileCallTreeNode> nodeList = null;
            funcLock_.EnterUpgradeableReadLock();

            try {
                if (!funcToNodesMap_.TryGetValue(node.Function, out nodeList)) {
                    funcLock_.EnterWriteLock();
                    try {
                        nodeList = new List<ProfileCallTreeNode>();
                        funcToNodesMap_[node.Function] = nodeList;
                    }
                    finally {
                        funcLock_.ExitWriteLock();
                    }
                }
            }
            finally {
                funcLock_.ExitUpgradeableReadLock();
            }

            lock (nodeList) {
                nodeList.Add(node);
            }
        }

        public List<ProfileCallTreeNode> GetCallTreeNodes(IRTextFunction function) {
            funcLock_.EnterReadLock();

            try {
                if (funcToNodesMap_.TryGetValue(function, out var nodeList)) {
                    return nodeList;
                }
            }
            finally {
                funcLock_.ExitReadLock();
            }

            return null;
        }

        public string Print() {
            var builder = new StringBuilder();

            foreach (var node in rootNodes_) {
                builder.AppendLine("Call tree root node");
                builder.AppendLine("-----------------------");
                node.Print(builder);
            }

            return builder.ToString();
        }

        public string PrintSamples() {
            var samples = new List<(int total, int unique, string name)>();
            var builder = new StringBuilder();

            foreach (var node in rootNodes_) {
                node.CollectSamples(samples);
            }

            samples.Sort((a, b) => b.total.CompareTo(a.total));
            int total = 0;
            int unique = 0;

            foreach (var sample in samples) {
                builder.AppendLine($"{sample.name}");
                builder.AppendLine($"   {sample.total} samples, {sample.unique} unique");
                builder.AppendLine($"   {100*(double)sample.unique / sample.total:F4} unique");
                total += sample.total;
                unique += sample.unique;
            }

            builder.AppendLine($"{total} total samples, {unique} unique");
            builder.AppendLine($"     {100 * (double)unique / total:F4} unique");
            return builder.ToString();
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class FunctionProfileData {
        [ProtoMember(1)]
        public string SourceFilePath { get; set; }
        [ProtoMember(2)]
        public TimeSpan Weight { get; set; }
        [ProtoMember(3)]
        public TimeSpan ExclusiveWeight { get; set; }
        [ProtoMember(4)]
        public Dictionary<int, TimeSpan> SourceLineWeight { get; set; } // Line number mapping
        [ProtoMember(5)]
        public Dictionary<long, TimeSpan> InstructionWeight { get; set; } // Instr. offset mapping
        [ProtoMember(6)]
        public Dictionary<long, TimeSpan> BlockWeight { get; set; } //? TODO: Unused
        [ProtoMember(7)]
        public Dictionary<(Guid, int), TimeSpan> CalleesWeights { get; set; } // {Summary,Function ID} mapping
        [ProtoMember(8)]
        public Dictionary<(Guid, int), TimeSpan> CallerWeights { get; set; } // {Summary,Function ID} mapping
        [ProtoMember(9)]
        public Dictionary<long, PerformanceCounterSet> InstructionCounters { get; set; }

        public DebugFunctionInfo DebugInfo { get; set; }

        public bool HasSourceLines => SourceLineWeight != null && SourceLineWeight.Count > 0;
        public bool HasPerformanceCounters => InstructionCounters.Count > 0;
        public bool HasCallers => CallerWeights != null && CallerWeights.Count > 0;
        public bool HasCallees => CalleesWeights != null && CalleesWeights.Count > 0;
        public List<(int LineNumber, TimeSpan Weight)> SourceLineWeightList => SourceLineWeight.ToKeyValueList();

        public class ProcessingResult {
            public List<Tuple<IRElement, TimeSpan>> SampledElements { get; set; }
            public Dictionary<BlockIR, TimeSpan> BlockSampledElementsMap { get; set; }
            public List<Tuple<BlockIR, TimeSpan>> BlockSampledElements { get; set; }
            public List<Tuple<IRElement, PerformanceCounterSet>> CounterElements { get; set; }
            public List<Tuple<BlockIR, PerformanceCounterSet>> BlockCounterElements { get; set; }

            public PerformanceCounterSet FunctionCounters { get; set; }

            public ProcessingResult(int capacity = 0) {
                SampledElements = new List<Tuple<IRElement, TimeSpan>>(capacity);
                BlockSampledElementsMap = new Dictionary<BlockIR, TimeSpan>(capacity);
                CounterElements = new List<Tuple<IRElement, PerformanceCounterSet>>(capacity);
                FunctionCounters = new PerformanceCounterSet();
            }

            public double ScaleCounterValue(long value, PerformanceCounterInfo counter) {
                var total = FunctionCounters.FindCounterValue(counter);
                return total > 0 ? (double)value / (double)total : 0;
            }
        }

        //? TODO: Module ID referencing ProfileData

        //? TODO
        //? - save unique stacks with inclusive samples for each frame

        public FunctionProfileData(string filePath) {
            SourceFilePath = filePath;
            Weight = TimeSpan.Zero;
            InitializeReferenceMembers();
        }

        public void AddCounterSample(long instrOffset, int perfCounterId, long value) {
            var counterSet = InstructionCounters.GetOrAddValue(instrOffset);
            counterSet.AddCounterSample(perfCounterId, value);
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            SourceLineWeight ??= new Dictionary<int, TimeSpan>();
            InstructionWeight ??= new Dictionary<long, TimeSpan>();
            BlockWeight ??= new Dictionary<long, TimeSpan>();
            CalleesWeights ??= new Dictionary<(Guid, int), TimeSpan>();
            CallerWeights ??= new Dictionary<(Guid, int), TimeSpan>();
            InstructionCounters ??= new Dictionary<long, PerformanceCounterSet>();
        }

        public void AddLineSample(int sourceLine, TimeSpan weight) {
            if (SourceLineWeight.TryGetValue(sourceLine, out var currentWeight)) {
                SourceLineWeight[sourceLine] = currentWeight + weight;
            }
            else {
                SourceLineWeight[sourceLine] = weight;
            }
        }

        public void AddInstructionSample(long instrOffset, TimeSpan weight) {
            if (InstructionWeight.TryGetValue(instrOffset, out var currentWeight)) {
                InstructionWeight[instrOffset] = currentWeight + weight;
            }
            else {
                InstructionWeight[instrOffset] = weight;
            }
        }

        public void AddChildSample(IRTextFunction childFunc, TimeSpan weight) {
            lock (CalleesWeights) {
                var key = (childFunc.ParentSummary.Id, childFunc.Number);

                if (CalleesWeights.TryGetValue(key, out var currentWeight)) {
                    CalleesWeights[key] = currentWeight + weight;
                }
                else {
                    CalleesWeights[key] = weight;
                }
            }
        }

        public void AddCallerSample(IRTextFunction callerFunc, TimeSpan weight) {
            lock (CallerWeights) {
                var key = (callerFunc.ParentSummary.Id, callerFunc.Number);

                if (CallerWeights.TryGetValue(key, out var currentWeight)) {
                    CallerWeights[key] = currentWeight + weight;
                }
                else {
                    CallerWeights[key] = weight;
                }
            }
        }

        public double ScaleWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)Weight.Ticks;
        }

        public double ScaleChildWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)Weight.Ticks;
        }

        public ProcessingResult Process(FunctionIR function, ICompilerIRInfo ir) {
            var metadataTag = function.GetTag<AssemblyMetadataTag>();
            bool hasInstrOffsetMetadata = metadataTag != null && metadataTag.OffsetToElementMap.Count > 0;

            if (!hasInstrOffsetMetadata) {
                return null;
            }

            var result = new ProcessingResult(metadataTag.OffsetToElementMap.Count);

            foreach (var pair in InstructionWeight) {
                if (TryFindElementForOffset(metadataTag, pair.Key, ir, out var element)) {
                    result.SampledElements.Add(new Tuple<IRElement, TimeSpan>(element, pair.Value));
                    result.BlockSampledElementsMap.AccumulateValue(element.ParentBlock, pair.Value);
                }
            }

            foreach (var pair in InstructionCounters) {
                if (TryFindElementForOffset(metadataTag, pair.Key, ir, out var element)) {
                    result.CounterElements.Add(new Tuple<IRElement, PerformanceCounterSet>(element, pair.Value));
                }

                result.FunctionCounters.Add(pair.Value);
            }

            result.BlockSampledElements = result.BlockSampledElementsMap.ToList();
            result.BlockSampledElements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            result.SampledElements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return result;
        }

        public void ProcessSourceLines(IDebugInfoProvider debugInfo) {
            if (HasSourceLines) {
                return;
            }

            SourceLineWeight ??= new Dictionary<int, TimeSpan>();
            var funcInfo = debugInfo.FindFunctionByRVA(DebugInfo.RVA);

            foreach (var pair in InstructionWeight) {
                long rva = pair.Key + funcInfo.RVA;
                var lineInfo = debugInfo.FindSourceLineByRVA(rva);

                if (!lineInfo.IsUnknown) {
                    SourceLineWeight.AccumulateValue(lineInfo.Line, pair.Value);
                }
            }
        }

        public PerformanceCounterSet ComputeFunctionCounters() {
            var result = new PerformanceCounterSet();

            foreach (var pair in InstructionCounters) {
                result.Add(pair.Value);
            }

            return result;
        }

        private bool TryFindElementForOffset(AssemblyMetadataTag metadataTag, long offset,
                                                    ICompilerIRInfo ir, out IRElement element) {
            int multiplier = 1;
            var offsetData = ir.InstructionOffsetData;

            do {
                if (metadataTag.OffsetToElementMap.TryGetValue(offset - multiplier * offsetData.OffsetAdjustIncrement, out element)) {
                    return true;
                }
                ++multiplier;
            } while (multiplier * offsetData.OffsetAdjustIncrement < offsetData.MaxOffsetAdjust);

            return false;
        }
    }

    public class ProfileData {
        [ProtoContract(SkipConstructor = true)]
        public class ProfileDataState {
            [ProtoMember(1)]
            public TimeSpan ProfileWeight { get; set; }
            
            [ProtoMember(2)]
            public TimeSpan TotalWeight { get; set; }

            [ProtoMember(3)]
            public Dictionary<(Guid summaryId, int funcNumber), FunctionProfileData> FunctionProfiles { get; set; }

            [ProtoMember(4)]
            public Dictionary<int, PerformanceCounterInfo> PerformanceCounters { get; set; }
            
            [ProtoMember(5)]
            public Dictionary<string, TimeSpan> ModuleWeights { get; set; }

            public ProfileDataState(TimeSpan profileWeight, TimeSpan totalWeight) {
                ProfileWeight = profileWeight;
                TotalWeight = totalWeight;
                FunctionProfiles = new Dictionary<(Guid summaryId, int funcNumber), FunctionProfileData>();
            }
        }

        public TimeSpan ProfileWeight { get; set; }
        public TimeSpan TotalWeight { get; set; }
        public ConcurrentDictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; set; }
        public Dictionary<string, TimeSpan> ModuleWeights { get; set; }
        public Dictionary<string, PerformanceCounterSet> ModuleCounters { get; set; }
        public Dictionary<int, PerformanceCounterInfo> PerformanceCounters { get; set; }
        
        public ProfileCallTree CallTree { get; set; }

        public List<PerformanceCounterInfo> SortedPerformanceCounters {
            get {
                var list = PerformanceCounters.ToValueList();
                list.Sort((a, b) => b.Id.CompareTo(a.Id));
                return list;
            }
        }

        public ProfileData(TimeSpan profileWeight, TimeSpan totalWeight) : this() {
            ProfileWeight = profileWeight;
            TotalWeight = totalWeight;
        }

        public ProfileData() {
            ProfileWeight = TimeSpan.Zero;
            FunctionProfiles = new ConcurrentDictionary<IRTextFunction, FunctionProfileData>();
            ModuleWeights = new Dictionary<string, TimeSpan>();
            PerformanceCounters = new Dictionary<int, PerformanceCounterInfo>();
            ModuleCounters = new Dictionary<string, PerformanceCounterSet>();
        }

        public void AddModuleSample(string moduleName, TimeSpan weight) {
            if (ModuleWeights.TryGetValue(moduleName, out var currentWeight)) {
                ModuleWeights[moduleName] = currentWeight + weight;
            }
            else {
                ModuleWeights[moduleName] = weight;
            }
        }
        
        public void AddModuleCounter(string moduleName, int perfCounterId, long value) {
            if (!ModuleCounters.TryGetValue(moduleName, out var counterSet)) {
                counterSet = new PerformanceCounterSet();
                ModuleCounters[moduleName] = counterSet;
            }
            
            counterSet.AddCounterSample(perfCounterId, value);
        }

        public void RegisterPerformanceCounter(PerformanceCounterInfo perfCounter) {
            perfCounter.Number = PerformanceCounters.Count;
            PerformanceCounters[perfCounter.Id] = perfCounter;
        }

        public PerformanceCounterInfo GetPerformanceCounter(int id) {
            if (PerformanceCounters.TryGetValue(id, out var counter)) {
                return counter;
            }

            return null;
        }
        
        public double ScaleFunctionWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)ProfileWeight.Ticks;
        }

        public double ScaleModuleWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)TotalWeight.Ticks;
        }

        public FunctionProfileData GetFunctionProfile(IRTextFunction function) {
            if (FunctionProfiles.TryGetValue(function, out var profile)) {
                return profile;
            }

            return null;
        }

        public bool HasFunctionProfile(IRTextFunction function) {
            return GetFunctionProfile(function) != null;
        }

        public FunctionProfileData GetOrCreateFunctionProfile(IRTextFunction function, string sourceFile) {
            if (!FunctionProfiles.TryGetValue(function, out var profile)) {
                profile = new FunctionProfileData(sourceFile);
                FunctionProfiles.TryAdd(function, profile);
            }

            return profile;
        }

        public byte[] Serialize() {
            var profileState = new ProfileDataState(ProfileWeight, TotalWeight);
            profileState.PerformanceCounters = PerformanceCounters;
            profileState.ModuleWeights = ModuleWeights;

            foreach (var pair in FunctionProfiles) {
                var func = pair.Key;
                profileState.FunctionProfiles[(func.ParentSummary.Id, func.Number)] = pair.Value;
            }

            return StateSerializer.Serialize(profileState);
        }

        public static ProfileData Deserialize(byte[] data, List<IRTextSummary> summaries) {
            var state = StateSerializer.Deserialize<ProfileDataState>(data);
            var profileData = new ProfileData(state.ProfileWeight, state.TotalWeight);
            profileData.PerformanceCounters = state.PerformanceCounters;
            profileData.ModuleWeights = state.ModuleWeights;

            var summaryMap = new Dictionary<Guid, IRTextSummary>();

            foreach (var summary in summaries) {
                summaryMap[summary.Id] = summary;
            }

            foreach(var pair in state.FunctionProfiles) {
                var summary = summaryMap[pair.Key.summaryId];
                var function = summary.GetFunctionWithId(pair.Key.funcNumber);

                if (function == null) {
                    Trace.TraceWarning($"No func for {pair.Value.SourceFilePath}");
                    continue;
                }

                profileData.FunctionProfiles[function] = pair.Value;
            }

            return profileData;
        }

        public List<Tuple<IRTextFunction, FunctionProfileData>> GetSortedFunctions() {
            var list = FunctionProfiles.ToList();
            list.Sort((a, b) => -a.Item2.ExclusiveWeight.CompareTo(b.Item2.ExclusiveWeight));
            return list;
        }
    }
}