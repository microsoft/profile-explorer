﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Policy;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using Microsoft.Windows.EventTracing;
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
        public ProfileStack() {
            FramePointers = null;
            ContextId = 0;
        }

        public ProfileStack(int frameCount) {
            FramePointers = new long[frameCount];
            ContextId = 0;
        }

        [ProtoMember(1)]
        public long[] FramePointers { get; set; }
        [ProtoMember(2)]
        public int ContextId { get; set; }

        public int FrameCount => FramePointers.Length;

        public bool Equals(ProfileStack other) {
            return ContextId == other.ContextId &&
                   FramePointers == other.FramePointers;
        }

        public override bool Equals(object obj) {
            return obj is ProfileStack other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(FramePointers, ContextId);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public struct ProfileSample : IEquatable<ProfileSample> {
        [ProtoMember(1)]
        public long IP { get; set; }
        [ProtoMember(2)]
        public long RVA { get; set; } //? Cache
        [ProtoMember(3)]
        public TimeSpan Time { get; set; }
        [ProtoMember(4)]
        public TimeSpan Weight { get; set; }
        [ProtoMember(5)]
        public int StackId { get; set; }
        [ProtoMember(6)]
        public int ContextId { get; set; }
        //? public bool IsUserCode { get; set; }

        public ProfileStack GetStack(ProtoProfile profile) {
            return profile.FindStack(StackId);
        }

        public ProfileContext GetContext(ProtoProfile profile) {
            return profile.FindContext(StackId);
        }

        public bool Equals(ProfileSample other) {
            return RVA == other.RVA && Time == other.Time && Weight == other.Weight &&
                   StackId == other.StackId && ContextId == other.ContextId;
        }

        public override bool Equals(object obj) {
            return obj is ProfileSample other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(RVA, Time, StackId, ContextId);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class ProfileContext : IEquatable<ProfileContext> {
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
        
        public bool Equals(ProfileContext other) {
            if (ReferenceEquals(null, other)) {
                return false;
            }

            if (ReferenceEquals(this, other)) {
                return true;
            }

            return ProcessId == other.ProcessId &&
                   ThreadId == other.ThreadId &&
                   ProcessorNumber == other.ProcessorNumber;
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

            return Equals((ProfileContext)obj);
        }

        public override int GetHashCode() {
            return HashCode.Combine(ProcessId, ThreadId, ProcessorNumber);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class ProfileImage : IEquatable<ProfileImage> {
        [ProtoMember(1)]
        public int Id { get; set; }
        [ProtoMember(2)]
        public int Size { get; set; }

        [ProtoMember(3)]
        public string Name { get; set; }
        [ProtoMember(4)]
        public long BaseAddress { get; set; }
        [ProtoMember(5)]
        public long DefaultBaseAddress { get; set; }
        [ProtoMember(6)]
        public long TimeStamp { get; set; }
        [ProtoMember(7)]
        public long Checksum { get; set; }

        public ProfileImage() {}

        public ProfileImage(string name, long baseAddress, long defaultBaseAddress, 
                            int size, long timeStamp, long checksum) {
            Size = size;
            Name = name;
            BaseAddress = baseAddress;
            DefaultBaseAddress = defaultBaseAddress;
            TimeStamp = timeStamp;
            Checksum = checksum;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Size, Name, BaseAddress, DefaultBaseAddress,
                                    TimeStamp, Checksum);
        }

        public bool Equals(ProfileImage other) {
            if (ReferenceEquals(null, other)) {
                return false;
            }

            if (ReferenceEquals(this, other)) {
                return true;
            }

            return Size == other.Size &&
                   Name == other.Name &&
                   BaseAddress == other.BaseAddress &&
                   DefaultBaseAddress == other.DefaultBaseAddress &&
                   TimeStamp == other.TimeStamp &&
                   Checksum == other.Checksum;
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

            return Equals((ProfileImage)obj);
        }

    }

    [ProtoContract(SkipConstructor = true)]
    public class ProfileProcess {
        [ProtoMember(1)]
        public int Id { get; set; }
        [ProtoMember(2)]
        public int ParentId { get; set; }
        [ProtoMember(3)]
        public string Name { get; set; }
        [ProtoMember(4)]
        public string ImageFileName { get; set; }
        [ProtoMember(5)]
        public string CommandLine { get; set; }
        [ProtoMember(6)]
        public List<int> Images { get; set; }
        [ProtoMember(7)]
        public List<int> Threads { get; set; }

        public ProfileProcess() {
            Images = new List<int>();
            Threads = new List<int>();
        }

        public ProfileProcess(int id, int parentId, string name, 
                              string imageFileName, string commandLine) : this() {
            Id = id;
            ParentId = parentId;
            Name = name;
            ImageFileName = imageFileName;
            CommandLine = commandLine;
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class ProfileThread : IEquatable<ProfileThread> {
        [ProtoMember(1)]
        public int Id { get; set; }
        [ProtoMember(2)]
        public int ProcessId { get; set; }
        [ProtoMember(3)]
        public string Name { get; set; }

        public ProfileThread() { }

        public ProfileThread(int id, int processId, string name) {
            Id = id;
            ProcessId = processId;
            Name = name;
        }

        public bool Equals(ProfileThread other) {
            if (ReferenceEquals(null, other)) {
                return false;
            }

            if (ReferenceEquals(this, other)) {
                return true;
            }

            return Id == other.Id && ProcessId == other.ProcessId;
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

            return Equals((ProfileThread)obj);
        }

        public override int GetHashCode() {
            return HashCode.Combine(Id, ProcessId);
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
    public class ProtoProfile {
        public List<ProfileProcess> Processes => processes_.ToValueList();
        public ChunkedList<PerformanceCounterEvent> PerfCounters { get; set; }

        public Dictionary<int, ProfileProcess> processes_;
        public List<ProfileThread> threads_;
        public Dictionary<ProfileThread, int> threadsMap_;

        public List<ProfileContext> contexts_;
        public Dictionary<ProfileContext, int> contextsMap_;

        public List<ProfileImage> images_;
        public Dictionary<ProfileImage, int> imagesMap_;

        public ChunkedList<ProfileStack> stacks_;
        public Dictionary<ProfileStack, int> stacksMap_;
        private HashSet<long[]> stackData_;

        public ChunkedList<ProfileSample> samples_;
        public ChunkedList<PerformanceCounterEvent> events_;

        public ProtoProfile() {
            contexts_ = new List<ProfileContext>();
            contextsMap_ = new Dictionary<ProfileContext, int>();
            images_ = new List<ProfileImage>();
            imagesMap_ = new Dictionary<ProfileImage, int>();
            processes_ = new Dictionary<int, ProfileProcess>();
            threads_ = new List<ProfileThread>();
            threadsMap_ = new Dictionary<ProfileThread, int>();
            stacks_ = new ChunkedList<ProfileStack>();
            stacksMap_ = new Dictionary<ProfileStack, int>();
            stackData_ = new HashSet<long[]>(new StackComparer());
            samples_ = new ChunkedList<ProfileSample>();
            events_ = new ChunkedList<PerformanceCounterEvent>();

            PerfCounters = new ChunkedList<PerformanceCounterEvent>();
        }

        public ProfileProcess GetOrCreateProcess(int id) {
            return processes_.GetOrAddValue(id);
        }

        public void AddProcess(ProfileProcess process) {
            processes_[process.Id] = process;
        }

        public int AddImage(ProfileImage image) {
            if (!imagesMap_.TryGetValue(image, out var existingImage)) {
                images_.Add(image);
                imagesMap_[image] = images_.Count;
                existingImage = images_.Count;
            }

            return existingImage;
        }

        public ProfileImage FindImage(int id) {
            if (id <= images_.Count) {
                return images_[id - 1];
            }

#if DEBUG
            Debug.Assert(false, "Invalid data index!");
#endif
            return null;
        }

        public int AddThread(ProfileThread thread) {
            if (!threadsMap_.TryGetValue(thread, out var existingThread)) {
                threads_.Add(thread);
                threadsMap_[thread] = threads_.Count;
                existingThread = threads_.Count;
            }

            return existingThread;
        }

        public ProfileThread FindThread(int id) {
            if (id <= threads_.Count) {
                return threads_[id - 1];
            }

#if DEBUG
            Debug.Assert(false, "Invalid data index!");
#endif
            return null;
        }

        public int AddThreadToProcess(int processId, ProfileThread thread) {
            var proc = GetOrCreateProcess(processId);
            var result = AddThread(thread);
            proc.Threads.Add(result);
            return result;
        }

        public int AddImageToProcess(int processId, ProfileImage image) {
            var proc = GetOrCreateProcess(processId);
            var result = AddImage(image);
            proc.Images.Add(result);
            return result;
        }

        public int AddSample(ProfileSample sample) {
            samples_.Add(sample);
            return samples_.Count;
        }

        public bool SetLastSampleStack(int stackId, int contextId) {
            for (int i = samples_.Count - 1, steps = 0; i >= 0 && steps < 3; i--, steps++) {
                if (samples_[i].ContextId == contextId) {
                    ref var sampleRef = ref samples_.GetRef(i);
                    sampleRef.StackId = stackId;
                    return true;
                }
            }

            return false;
        }

        public void SetContext(ref ProfileSample sample, ProfileContext context) {
            sample.ContextId = AddContext(context);
        }

        public int SetContext(ref ProfileStack stack, ProfileContext context) {
            stack.ContextId = AddContext(context);
            return stack.ContextId;
        }

        private int AddContext(ProfileContext context) {
            if (!contextsMap_.TryGetValue(context, out var existingContext)) {
                contexts_.Add(context);
                contextsMap_[context] = contexts_.Count;
                existingContext = contexts_.Count;
            }

            return existingContext;
        }

        public ProfileContext FindContext(int id) {
            if (id <= contexts_.Count) {
                return contexts_[id - 1];
            }

#if DEBUG
            Debug.Assert(false, "Invalid data index!");
#endif
            return null;
        }

        public int AddStack(ProfileStack stack) {
            if (stackData_.TryGetValue(stack.FramePointers, out var existingData)) {
                stack.FramePointers = existingData;
            }
            else stackData_.Add(stack.FramePointers);

            if (!stacksMap_.TryGetValue(stack, out var existingStack)) {
                stacks_.Add(stack);
                stacksMap_[stack] = stacks_.Count;
                existingStack = stacks_.Count;
            }

            return existingStack;
        }

        public ProfileStack FindStack(int id) {
            if (id <= stacks_.Count) {
                return stacks_[id - 1];
            }

#if DEBUG
            Debug.Assert(false, "Invalid data index!");
#endif
            return new ProfileStack();
        }

        private class StackComparer : IEqualityComparer<long[]> {
            public bool Equals(long[] x, long[] y) {
                return Enumerable.SequenceEqual(x, y);
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
            var key = (childFunc.ParentSummary.Id, childFunc.Number);

            if (CalleesWeights.TryGetValue(key, out var currentWeight)) {
                CalleesWeights[key] = currentWeight + weight;
            }
            else {
                CalleesWeights[key] = weight;
            }
        }

        public void AddCallerSample(IRTextFunction callerFunc, TimeSpan weight) {
            var key = (callerFunc.ParentSummary.Id, callerFunc.Number);

            if (CallerWeights.TryGetValue(key, out var currentWeight)) {
                CallerWeights[key] = currentWeight + weight;
            }
            else {
                CallerWeights[key] = weight;
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
        public Dictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; set; }
        public Dictionary<string, TimeSpan> ModuleWeights { get; set; }
        public Dictionary<string, PerformanceCounterSet> ModuleCounters { get; set; }
        public Dictionary<int, PerformanceCounterInfo> PerformanceCounters { get; set; }
        
        public List<PerformanceCounterInfo> SortedPerformanceCounters {
            get {
                var list = PerformanceCounters.ToValueList();
                list.Sort((a, b) => b.Id.CompareTo(a.Id));
                return list;
            }
        }

        //public List<PerformanceCounterInfo> SortedPerf

        public ProfileData(TimeSpan profileWeight, TimeSpan totalWeight) : this() {
            ProfileWeight = profileWeight;
            TotalWeight = totalWeight;
        }

        public ProfileData() {
            ProfileWeight = TimeSpan.Zero;
            FunctionProfiles = new Dictionary<IRTextFunction, FunctionProfileData>();
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
                FunctionProfiles[function] = profile;
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