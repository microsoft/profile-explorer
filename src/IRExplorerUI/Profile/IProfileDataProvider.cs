using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI.Profile;

public interface IProfileDataProvider {
    ProfileData LoadTrace(string tracePath, string imageName,
        ProfileDataProviderOptions options,
        SymbolFileSourceOptions symbolOptions,
        ProfileDataReport report,
        ProfileLoadProgressHandler progressCallback,
        CancelableTask cancelableTask = null);

    Task<ProfileData> LoadTraceAsync(string tracePath, string imageName,
        ProfileDataProviderOptions options,
        SymbolFileSourceOptions symbolOptions,
        ProfileDataReport report,
        ProfileLoadProgressHandler progressCallback,
        CancelableTask cancelableTask = null);
}

[ProtoContract(SkipConstructor = true)]
public class ProcessSummary {
    [ProtoMember(1)]
    public ProfileProcess Process { get; set; }
    [ProtoMember(2)]
    public TimeSpan Weight { get; set; }
    [ProtoMember(3)]
    public double WeightPercentage { get; set; }
    [ProtoMember(4)]
    public TimeSpan Duration { get; set; }
    [ProtoMember(5)]
    public int SampleCount { get; set; }
    //[ProtoMember(6)]
    //public List<(ProfileImage Image, TimeSpan Weight)> ImageWeights;

    public ProcessSummary() {
        
    }

    public ProcessSummary(ProfileProcess process, int sampleCount) {
        Process = process;
        SampleCount = sampleCount;
        //ImageWeights = new List<(ProfileImage Image, TimeSpan Weight)>();
    }

    public override string ToString() {
        return Process.ToString();
    }
}

public enum ModuleLoadState {
    Loaded,
    NotFound,
    Failed
}

[ProtoContract(SkipConstructor = true)]
public class ProfileDataReport {
    [ProtoContract(SkipConstructor = true)]
    public class ModuleStatus {
        [ProtoMember(1)]
        public ModuleLoadState State { get; set; }
        [ProtoMember(2)]
        public BinaryFileDescriptor ImageFileInfo { get; set; }    // Info used for lookup.
        [ProtoMember(3)]
        public BinaryFileSearchResult BinaryFileInfo { get; set; } // Lookup result with local file.
        [ProtoMember(4)]
        public DebugFileSearchResult DebugInfoFile { get; set; }
    }

    [ProtoMember(1)]
    private Dictionary<BinaryFileDescriptor, ModuleStatus> moduleStatusMap_;
    [ProtoMember(2)]
    public ProfileTraceInfo TraceInfo { get; set; }
    [ProtoMember(3)]
    public List<ProcessSummary> RunningProcesses { get; set; }
    [ProtoMember(4)]
    public ProfileProcess Process { get; set; }
    [ProtoMember(5)]
    public SymbolFileSourceOptions SymbolOptions { get; set; }
    [ProtoMember(6)]
    public ProfileRecordingSessionOptions SessionOptions { get; set; } // For recording mode

    public bool IsRecordingSession => SessionOptions != null;
    public List<ModuleStatus> Modules => moduleStatusMap_.ToValueList();

    public ProfileDataReport() {
        moduleStatusMap_ = new Dictionary<BinaryFileDescriptor, ModuleStatus>();
    }

    public void AddModuleInfo(BinaryFileDescriptor binaryInfo, BinaryFileSearchResult binaryFile, ModuleLoadState state) {
        var status = GetOrCreateModuleStatus(binaryInfo);
        status.BinaryFileInfo = binaryFile;
        status.State = state;
    }

    private ModuleStatus GetOrCreateModuleStatus(BinaryFileDescriptor binaryInfo) {
        if (!moduleStatusMap_.TryGetValue(binaryInfo, out var status)) {
            status = new ModuleStatus();
            status.ImageFileInfo = binaryInfo;
            moduleStatusMap_[binaryInfo] = status;
        }

        return status;
    }

    public void AddDebugInfo(BinaryFileDescriptor binaryInfo, DebugFileSearchResult searchResult) {
        var status = GetOrCreateModuleStatus(binaryInfo);
        status.DebugInfoFile = searchResult;
    }

    public void Dump() {
        foreach (var pair in moduleStatusMap_) {
            Trace.WriteLine($"Module {pair.Value.ImageFileInfo.ImageName}");
            Trace.WriteLine($"   - state: {pair.Value.State}");

            if (pair.Value.BinaryFileInfo != null) {
                Trace.WriteLine($"   - found: {pair.Value.BinaryFileInfo.Found}");
                Trace.WriteLine($"   - path: {pair.Value.BinaryFileInfo.FilePath}");
                Trace.WriteLine($"   - details: {pair.Value.BinaryFileInfo.Details}");
            }
            
            if (pair.Value.DebugInfoFile != null) {
                Trace.WriteLine($"   - debug: {pair.Value.DebugInfoFile.Found}");
                Trace.WriteLine($"   - path: {pair.Value.DebugInfoFile.FilePath}");
                Trace.WriteLine($"   - details: {pair.Value.DebugInfoFile.Details}");
            }
        }
    }
}

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
