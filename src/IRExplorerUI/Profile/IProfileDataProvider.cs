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
        public List<string> BinarySearchPaths { get; set; }
        [ProtoMember(2)]
        public List<string> BinaryNameWhitelist { get; set; }
        [ProtoMember(3)]
        public bool MarkInlineFunctions { get; set; }
        [ProtoMember(4)]
        public bool IncludeKernelEvents { get; set; }
        [ProtoMember(5)]
        public bool IncludeAllProcesses { get; set; }
        [ProtoMember(6)]
        public bool IncludePerformanceCounters { get; set; }

        public ProfileDataProviderOptions() {
            Reset();
        }

        public override void Reset() {
            InitializeReferenceMembers();
            MarkInlineFunctions = true;
            IncludePerformanceCounters = true;
        }
        public bool HasBinaryPath(string path) {
            path = Utils.TryGetDirectoryName(path).ToLowerInvariant();
            return BinarySearchPaths.Find(item => item.ToLowerInvariant() == path) != null;
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
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
                              SymbolFileSourceOptions symbolOptions,
                              ProfileLoadProgressHandler progressCallback,
                              CancelableTask cancelableTask = null);

        Task<ProfileData> LoadTraceAsync(string tracePath, string imageName,
                                         ProfileDataProviderOptions options,
                                         SymbolFileSourceOptions symbolOptions,
                                         ProfileLoadProgressHandler progressCallback,
                                         CancelableTask cancelableTask = null);
    }
}
