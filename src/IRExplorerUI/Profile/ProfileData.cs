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
        //? TODO: InclusiveWeight, show in CG
        [ProtoMember(3)]
        public Dictionary<int, TimeSpan> SourceLineWeight { get; set; }
        [ProtoMember(4)]
        public Dictionary<long, TimeSpan> InstructionWeight { get; set; }
        [ProtoMember(5)]
        public Dictionary<long, TimeSpan> BlockWeight { get; set; }

        //? TODO
        //? - have both inclusive/exclusive sample info
        //? - save unique stacks with inclusive samples for each frame

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

        public double ScaleWeight(TimeSpan weight) {
            return (double)weight.Ticks / (double)Weight.Ticks;
        }
    }

    public class ProfileData {
        [ProtoContract(SkipConstructor = true)]
        public class ProfileDataState {
            [ProtoMember(1)]
            public TimeSpan TotalWeight { get; set; }

            [ProtoMember(2)]
            public Dictionary<int, FunctionProfileData> FunctionProfiles { get; set; }

            public ProfileDataState(TimeSpan totalWeight) {
                TotalWeight = totalWeight;
                FunctionProfiles = new Dictionary<int, FunctionProfileData>();
            }
        }

        public TimeSpan TotalWeight { get; set; }
        public Dictionary<IRTextFunction, FunctionProfileData> FunctionProfiles { get; set; }

        public ProfileData(TimeSpan totalWeight) : this() {
            TotalWeight = totalWeight;
        }

        public ProfileData() {
            TotalWeight = TimeSpan.Zero;
            FunctionProfiles = new Dictionary<IRTextFunction, FunctionProfileData>();
        }

        public double ScaleFunctionWeight(TimeSpan weight) {
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
            var profileState = new ProfileDataState(TotalWeight);

            foreach (var pair in FunctionProfiles) {
                profileState.FunctionProfiles[pair.Key.Number] = pair.Value;
            }

            return StateSerializer.Serialize(profileState);
        }

        public static ProfileData Deserialize(byte[] data, IRTextSummary summary) {
            var state = StateSerializer.Deserialize<ProfileDataState>(data);
            var profileData = new ProfileData(state.TotalWeight);

            foreach(var pair in state.FunctionProfiles) {
                var function = summary.GetFunctionWithId(pair.Key);
                profileData.FunctionProfiles[function] = pair.Value;
            }

            return profileData;
        }
    }
}