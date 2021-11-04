using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
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
    public struct ProfileSample : IEquatable<ProfileSample> {
        [ProtoMember(1)]
        public long RVA { get; set; }
        [ProtoMember(2)]
        public long Time { get; set; }
        [ProtoMember(3)]
        public TimeSpan Weight { get; set; }
        [ProtoMember(7)]
        public int StackFrameId { get; set; }
        [ProtoMember(4)]
        public int ProcessId { get; set; } //? Values after this part could be shared
        [ProtoMember(5)]
        public int ThreadId { get; set; }
        [ProtoMember(6)]
        public int ImageId { get; set; }
        
        [ProtoMember(8)]
        public short ProcessorCore { get; set; }
        [ProtoMember(9)]
        public bool IsUserCode { get; set; }

        public bool Equals(ProfileSample other) {
            return RVA == other.RVA && Time == other.Time && ProcessId == other.ProcessId;
        }

        public override bool Equals(object obj) {
            return obj is ProfileSample other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(RVA, Time, ProcessId);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class ProfileImage {
        [ProtoMember(1)]
        public int Id { get; set; }
        [ProtoMember(2)]
        public string Name { get; set; }
        [ProtoMember(3)]
        public string Path { get; set; }
        [ProtoMember(4)]
        public long AddressStart { get; set; }
        [ProtoMember(5)]
        public long AddressEnd { get; set; }
    }

    [ProtoContract(SkipConstructor = true)]
    public class ProfileProcess {
        [ProtoMember(1)]
        public int Id { get; set; }
        [ProtoMember(2)]
        public string Name { get; set; }
        [ProtoMember(3)]
        public List<ProfileImage> Images { get; set; }

        public ProfileProcess() {
            Images = new List<ProfileImage>();
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public struct PerformanceCounterEvent : IEquatable<PerformanceCounterEvent> {
        [ProtoMember(1)]
        public long Time;
        //? everything after is shared by many, use flyweight
        [ProtoMember(2)]
        public long IP;
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
        [ProtoMember(1)]
        public List<ProfileProcess> Processes { get; set; }
        [ProtoMember(2)]
        public List<ProfileImage> Images { get; set; }
        [ProtoMember(3)]
        public List<ProfileSample> Samples { get; set; }
        [ProtoMember(4)]
        public ChunkedList<PerformanceCounterEvent> PerfCounters { get; set; }

        public ProtoProfile() {
            Processes = new List<ProfileProcess>();
            Images = new List<ProfileImage>();
            Samples = new List<ProfileSample>();
            PerfCounters = new ChunkedList<PerformanceCounterEvent>();
        }

        public ProfileProcess FindProcess(int id) {
            return Processes.Find(p => p.Id == id);
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
        public Dictionary<long, TimeSpan> BlockWeight { get; set; } // 
        [ProtoMember(7)]
        public Dictionary<(Guid, int), TimeSpan> ChildrenWeights { get; set; } // {Summary,Function ID} mapping
        [ProtoMember(8)]
        public Dictionary<(Guid, int), TimeSpan> CallerWeights { get; set; } // {Summary,Function ID} mapping
        [ProtoMember(9)]
        public Dictionary<long, PerformanceCounterSet> InstructionCounters { get; set; }

        public bool HasPerformanceCounters => InstructionCounters.Count > 0;

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
            ChildrenWeights ??= new Dictionary<(Guid, int), TimeSpan>();
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

            if (ChildrenWeights.TryGetValue(key, out var currentWeight)) {
                ChildrenWeights[key] = currentWeight + weight;
            }
            else {
                ChildrenWeights[key] = weight;
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

            var hist = new Dictionary<IRElement, long>();

            foreach (var pair in InstructionWeight) {
                if (TryFindElementForOffset(metadataTag, pair.Key, ir, out var element)) {
                    if (hist.TryGetValue(element, out var prev)) {
                        TryFindElementForOffset(metadataTag, pair.Key, ir, out var element2);
                        ;
                    }
                    else {
                        hist[element] = pair.Key;
                    }

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


    [ProtoContract(SkipConstructor = true)]
    public class ChunkedList<T> : IList<T> {
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

            //if (indexInChunk < 0 || indexInChunk >= 8192) {
            //    return;
            //}

            //if (chunk < 0 || chunk >= chunks_.Count) {
            //    return;
            //}

            chunks_[chunk][indexInChunk] = item;
            count_++;
        }

        public int IndexOf(T item) {
            return 0;
        }

        public void Insert(int index, T item) {

        }

        public void RemoveAt(int index) {

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


    }

}