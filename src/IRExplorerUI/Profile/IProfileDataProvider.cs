using System.Collections.Generic;
using System.IO;
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

    public enum ProfileSessionKind {
        SystemWide,
        StartProcess,
        AttachToProcess
    }

    [ProtoContract(SkipConstructor = true)]    
    public class ProfileRecordingSessionOptions : SettingsBase {
        [ProtoMember(1)]
        public ProfileSessionKind SessionKind { get; set; }
        [ProtoMember(2)]
        public string ApplicationPath { get; set; }
        [ProtoMember(3)]
        public string ApplicationArguments { get; set; }
        [ProtoMember(4)]
        public string WorkingDirectory { get; set; }
        //? Env vars
        //? Child procs
        //? Pmc
        //? PMC config
        [ProtoMember(5)]
        public int SamplingFrequency { get; set; }
        [ProtoMember(6)]
        public bool ProfileDotNet { get; set; }

        public bool HasWorkingDirectory => Directory.Exists(WorkingDirectory);

        public ProfileRecordingSessionOptions() {
            Reset();
        }

        public override void Reset() {
            SessionKind = ProfileSessionKind.SystemWide;
            SamplingFrequency = 4000; // 4 kHz, Xperf default is 1 kHz.
        }
    }

    public delegate void ProfileLoadProgressHandler(ProfileLoadProgress info);

    [ProtoContract(SkipConstructor = true)]
    public class ProfileDataProviderOptions : SettingsBase {
        [ProtoMember(1)]
        public bool BinarySearchPathsEnabled { get; set; }
        [ProtoMember(2)]
        public bool BinaryNameWhitelistEnabled { get; set; }
        [ProtoMember(3)]
        public bool DownloadBinaryFiles { get; set; }
        [ProtoMember(4)]
        public List<string> BinarySearchPaths { get; set; }
        [ProtoMember(5)]
        public List<string> BinaryNameWhitelist { get; set; }
        [ProtoMember(6)]
        public bool MarkInlinedFunctions { get; set; }
        [ProtoMember(7)]
        public bool IncludeKernelEvents { get; set; }
        [ProtoMember(8)]
        public bool IncludeAllProcesses { get; set; }
        [ProtoMember(9)]
        public bool IncludePerformanceCounters { get; set; }
        [ProtoMember(10)] 
        public ProfileRecordingSessionOptions RecordingSessionOptions { get; set; }
        //? Recent profile sessions

        public bool HasBinaryNameWhitelist => BinaryNameWhitelistEnabled && BinaryNameWhitelist.Count > 0;
        public bool HasBinarySearchPaths => BinarySearchPathsEnabled && BinarySearchPaths.Count > 0;

        public ProfileDataProviderOptions() {
            Reset();
        }

        public override void Reset() {
            InitializeReferenceMembers();
            DownloadBinaryFiles = true;
        }
        
        public bool HasBinaryPath(string path) {
            path = Utils.TryGetDirectoryName(path).ToLowerInvariant();
            return BinarySearchPaths.Find(item => item.ToLowerInvariant() == path) != null;
        }

        public void InsertBinaryPath(string path) {
            if (string.IsNullOrEmpty(path) || HasBinaryPath(path)) {
                return;
            }

            path = Utils.TryGetDirectoryName(path);

            if (!string.IsNullOrEmpty(path)) {
                BinarySearchPaths.Insert(0, path);
            }
        }

        [ProtoAfterDeserialization]
        private void InitializeReferenceMembers() {
            BinarySearchPaths ??= new List<string>();
            BinaryNameWhitelist ??= new List<string>();
            RecordingSessionOptions ??= new ProfileRecordingSessionOptions();
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
