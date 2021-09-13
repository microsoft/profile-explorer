using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using IRExplorerCore.IR.Tags;
using ProtoBuf;

namespace IRExplorerUI.Profile {
    public class PerformanceCounterInfo {
        public int Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Frequency { get; set; }
    }

    public class PerformanceCounterValue {
        public int CounterId { get; set; }
        public long Value { get; set; }

        public PerformanceCounterValue(int counterId, long value = 0) {
            CounterId = counterId;
            Value = value;
        }
    }

    public class PerformanceCounterSet {
        public List<PerformanceCounterValue> Counters { get; set; }

        public PerformanceCounterSet() {
            Counters = new List<PerformanceCounterValue>();
        }

        public void AddCounterSample(int perfCounterId, long value) {
            PerformanceCounterValue counter;
            var index = Counters.FindIndex((item) => item.CounterId == perfCounterId);

            if (index != -1) {
                counter = Counters[index];
            }
            else {
                counter = new PerformanceCounterValue(perfCounterId);
                Counters.Add(counter);
            }

            counter.Value += value;
        }

        public long FindCounterSamples(int perfCounterId) {
            var index =Counters.FindIndex((item) => item.CounterId == perfCounterId);
            return index != -1 ? Counters[index].Value : 0;
        }
    }

    public class ProfileSample {
        public long RVA;
        public TimeSpan Weight;
        public StackFrame Stack;
        public int Thread;
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
        public Dictionary<int, TimeSpan> ChildrenWeights { get; set; } // Function ID mapping
        [ProtoMember(8)]
        public Dictionary<int, TimeSpan> CallerWeights { get; set; } // Function ID mapping

        public Dictionary<long, PerformanceCounterSet> InstructionCounters { get; set; }


        public class ProcessingResult {
            public List<Tuple<IRElement, TimeSpan>> SampledElements { get; set; }
            public Dictionary<BlockIR, TimeSpan> BlockSampledElementsMap { get; set; }
            public List<Tuple<BlockIR, TimeSpan>> BlockSampledElements { get; set; }
            public List<Tuple<IRElement, PerformanceCounterSet>> CounterElements { get; set; }
            public Dictionary<BlockIR, PerformanceCounterSet> BlockCounterElementsMap { get; set; }
            public List<Tuple<BlockIR, PerformanceCounterSet>> BlockCounterElements { get; set; }

            public ProcessingResult(int capacity = 0) {
                SampledElements = new List<Tuple<IRElement, TimeSpan>>(capacity);
                BlockSampledElementsMap = new Dictionary<BlockIR, TimeSpan>(capacity);
                CounterElements = new List<Tuple<IRElement, PerformanceCounterSet>>(capacity);
                BlockCounterElementsMap = new Dictionary<BlockIR, PerformanceCounterSet>(capacity);
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
            if (!InstructionCounters.TryGetValue(instrOffset, out var counterSet)) {
                counterSet = new PerformanceCounterSet();
                InstructionCounters[instrOffset] = counterSet;
            }

            counterSet.AddCounterSample(perfCounterId, value);
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            SourceLineWeight ??= new Dictionary<int, TimeSpan>();
            InstructionWeight ??= new Dictionary<long, TimeSpan>();
            BlockWeight ??= new Dictionary<long, TimeSpan>();
            ChildrenWeights ??= new Dictionary<int, TimeSpan>();
            CallerWeights ??= new Dictionary<int, TimeSpan>();
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
            if (ChildrenWeights.TryGetValue(childFunc.Number, out var currentWeight)) {
                ChildrenWeights[childFunc.Number] = currentWeight + weight;
            }
            else {
                ChildrenWeights[childFunc.Number] = weight;
            }
        }

        public void AddCallerSample(IRTextFunction callerFunc, TimeSpan weight) {
            if (CallerWeights.TryGetValue(callerFunc.Number, out var currentWeight)) {
                CallerWeights[callerFunc.Number] = currentWeight + weight;
            }
            else {
                CallerWeights[callerFunc.Number] = weight;
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
                    result.BlockSampledElementsMap.AccummulateValue(element.ParentBlock, pair.Value);
                }
            }

            foreach (var pair in InstructionCounters) {
                if (TryFindElementForOffset(metadataTag, pair.Key, ir, out var element)) {
                    result.CounterElements.Add(new Tuple<IRElement, PerformanceCounterSet>(element, pair.Value));
                    //? TODO per block
                }
            }

            result.BlockSampledElements = result.BlockSampledElementsMap.ToList();
            result.BlockSampledElements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            result.SampledElements.Sort((a, b) => b.Item2.CompareTo(a.Item2));
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
            public TimeSpan TotalExclusiveWeight { get; set; }

            [ProtoMember(3)]
            public Dictionary<int, FunctionProfileData> FunctionProfiles { get; set; }

            public ProfileDataState(TimeSpan profileWeight) {
                ProfileWeight = profileWeight;
                FunctionProfiles = new Dictionary<int, FunctionProfileData>();
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
                list.Sort((a, b) => a.Id - b.Id);
                return list;
            }
        }

        //public List<PerformanceCounterInfo> SortedPerf

        public ProfileData(TimeSpan profileWeight) : this() {
            ProfileWeight = profileWeight;
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
            var profileState = new ProfileDataState(ProfileWeight);

            foreach (var pair in FunctionProfiles) {
                profileState.FunctionProfiles[pair.Key.Number] = pair.Value;
            }

            return StateSerializer.Serialize(profileState);
        }

        public static ProfileData Deserialize(byte[] data, IRTextSummary summary) {
            var state = StateSerializer.Deserialize<ProfileDataState>(data);
            var profileData = new ProfileData(state.ProfileWeight);

            foreach(var pair in state.FunctionProfiles) {
                var function = summary.GetFunctionWithId(pair.Key);
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