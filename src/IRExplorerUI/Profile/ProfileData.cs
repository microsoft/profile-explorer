using System;
using System.Collections.Generic;
using IRExplorerCore;
using ProtoBuf;

namespace IRExplorerUI.Profile {
    [ProtoContract(SkipConstructor = true)]
    public class FunctionProfileData {
        [ProtoMember(1)]
        public string SourceFilePath { get; set; }
        [ProtoMember(2)]
        public TimeSpan Weight { get; set; }
        [ProtoMember(3)]
        public TimeSpan ExclusiveWeight { get; set; }
        [ProtoMember(4)]
        public Dictionary<int, TimeSpan> SourceLineWeight { get; set; }
        [ProtoMember(5)]
        public Dictionary<long, TimeSpan> InstructionWeight { get; set; }
        [ProtoMember(6)]
        public Dictionary<long, TimeSpan> BlockWeight { get; set; }
        [ProtoMember(7)]
        public Dictionary<int, TimeSpan> ChildrenWeights { get; set; }
        
        //? TODO
        //? - save unique stacks with inclusive samples for each frame
        //? - save list of calees with  their time

        public FunctionProfileData(string filePath) {
            SourceFilePath = filePath;
            Weight = TimeSpan.Zero;
            InitializeReferenceMembers();
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            SourceLineWeight ??= new Dictionary<int, TimeSpan>();
            InstructionWeight ??= new Dictionary<long, TimeSpan>();
            BlockWeight ??= new Dictionary<long, TimeSpan>();
            ChildrenWeights ??= new Dictionary<int, TimeSpan>();
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

        public double ScaleWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)ExclusiveWeight.Ticks;
        }

        public double ScaleChildWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)Weight.Ticks;
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
        
        public ProfileData(TimeSpan profileWeight) : this() {
            ProfileWeight = profileWeight;
        }

        public ProfileData() {
            ProfileWeight = TimeSpan.Zero;
            FunctionProfiles = new Dictionary<IRTextFunction, FunctionProfileData>();
            ModuleWeights = new Dictionary<string, TimeSpan>();
        }

        public void AddModuleSample(string moduleName, TimeSpan weight) {
            if (ModuleWeights.TryGetValue(moduleName, out var currentWeight)) {
                ModuleWeights[moduleName] = currentWeight + weight;
            }
            else {
                ModuleWeights[moduleName] = weight;
            }
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
    }
}