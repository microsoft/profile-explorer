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
        ProfileDataProviderReport report,
        ProfileLoadProgressHandler progressCallback,
        CancelableTask cancelableTask = null);

    Task<ProfileData> LoadTraceAsync(string tracePath, string imageName,
        ProfileDataProviderOptions options,
        SymbolFileSourceOptions symbolOptions,
        ProfileDataProviderReport report,
        ProfileLoadProgressHandler progressCallback,
        CancelableTask cancelableTask = null);
}

public class TraceProcessSummary {
    public ProfileProcess Process { get; set; }
    public TimeSpan Weight { get; set; }
    public double WeightPercentage { get; set; }
    public TimeSpan Duration { get; set; }
    public int SampleCount { get; set; }
    public List<(ProfileImage Image, TimeSpan Weight)> ImageWeights;

    public TraceProcessSummary(ProfileProcess process, int sampleCount) {
        Process = process;
        SampleCount = sampleCount;
        ImageWeights = new List<(ProfileImage Image, TimeSpan Weight)>();
    }

    public override string ToString() {
        return Process.ToString();
    }
}

public class ProfileDataProviderReport {
    //? other errors

    public enum LoadState {
        Loaded,
        NotFound,
        Failed
    }

    public class ModuleStatus {
        public LoadState State { get; set; }
        public BinaryFileDescriptor ImageFile { get; set; }    // Info used for lookup.
        public BinaryFileSearchResult BinaryFile { get; set; } // Lookup result with local file.
        public DebugFileSearchResult DebugInfoFile { get; set; }
    }

    private Dictionary<BinaryFileDescriptor, ModuleStatus> moduleStatusMap_;
    private List<string> errorList_;

    public TraceProcessSummary Process { get; set; }
    public SymbolFileSourceOptions SymbolOptions { get; set; }
    public ProfileRecordingSessionOptions SessionOptions { get; set; } // For recording mode

    // dict -> {bin status, optional, debugSearch}

    public ProfileDataProviderReport(SymbolFileSourceOptions symbolOptions) {
        SymbolOptions = symbolOptions;
        moduleStatusMap_ = new Dictionary<BinaryFileDescriptor, ModuleStatus>();
        errorList_ = new List<string>();
    }

    public void AddModuleInfo(BinaryFileDescriptor binaryInfo, BinaryFileSearchResult binaryFile, LoadState state) {
        var status = GetOrCreateModuleStatus(binaryInfo);
        status.BinaryFile = binaryFile;
        status.State = state;
    }

    private ModuleStatus GetOrCreateModuleStatus(BinaryFileDescriptor binaryInfo) {
        if (!moduleStatusMap_.TryGetValue(binaryInfo, out var status)) {
            status = new ModuleStatus();
            status.ImageFile = binaryInfo;
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
            Trace.WriteLine($"Module {pair.Value.ImageFile.ImageName}");
            Trace.WriteLine($"   - state: {pair.Value.State}");

            if (pair.Value.BinaryFile != null) {
                Trace.WriteLine($"   - found: {pair.Value.BinaryFile.Found}");
                Trace.WriteLine($"   - path: {pair.Value.BinaryFile.FilePath}");
                Trace.WriteLine($"   - details: {pair.Value.BinaryFile.Details}");
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
