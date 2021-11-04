using System.Collections.Generic;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using ProtoBuf;

namespace IRExplorerUI.Profile {
    public enum ProfileLoadStage {
        TraceLoading,
        SymbolLoading,
        TraceProcessing,
        PerfCounterProcessing
    }

    public class ProfileLoadProgress {
        public ProfileLoadProgress(ProfileLoadStage stage) {
            Stage = stage;
        }

        public ProfileLoadStage Stage { get; set; }
        public int Total { get; set; }
        public int Current { get; set; }
    }

    public delegate void ProfileLoadProgressHandler(ProfileLoadProgress info);

    [ProtoContract(SkipConstructor = true)]
    public class ProfileDataProviderOptions : SettingsBase {
        [ProtoMember(1)]
        public SymbolFileSourceOptions Symbols { get; set; }
        [ProtoMember(2)]
        public List<string> BinarySearchPaths { get; set; }
        [ProtoMember(3)]
        public List<string> BinaryNameWhitelist { get; set; }
        [ProtoMember(4)]
        public bool MarkInlineFunctions { get; set; }


        public ProfileDataProviderOptions() {
            Reset();
        }

        public override void Reset() {
            InitializeReferenceMembers();
            MarkInlineFunctions = true;
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            Symbols ??= new SymbolFileSourceOptions();
            BinarySearchPaths ??= new List<string>();
            BinaryNameWhitelist ??= new List<string>();
        }


        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<ProfileDataProviderOptions>(serialized);
        }
    }
    
    public interface IProfileDataProvider {
        ProfileData LoadTrace(string tracePath, string imageName,
                              ProfileDataProviderOptions options, 
                              ProfileLoadProgressHandler progressCallback,
                              CancelableTask cancelableTask = null);

        Task<ProfileData> LoadTraceAsync(string tracePath, string imageName,
                                         ProfileDataProviderOptions options,
                                         ProfileLoadProgressHandler progressCallback,
                                         CancelableTask cancelableTask = null);
    }
}
