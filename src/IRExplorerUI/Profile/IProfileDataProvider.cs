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

public class ProfileDataProviderReport {
    //? other errors

    public enum LoadState {
        Loaded,
        NotFound,
        Failed
    }

    public class ModuleStatus {
        public LoadState State { get; set; }
        public BinaryFileDescriptor ImageFile { get; set; }
        public DebugFileSearchResult DebugInfoFile { get; set; }
    }

    private Dictionary<BinaryFileDescriptor, ModuleStatus> moduleStatusMap_;
    private List<string> errorList_;

    public SymbolFileSourceOptions SymbolOptions { get; set; }
    public ProfileRecordingSessionOptions SessionOptions { get; set; } // For recording mode
    public string Executable { get; set; }

    // dict -> {bin status, optional, debugSearch}

    public ProfileDataProviderReport(SymbolFileSourceOptions symbolOptions) {
        SymbolOptions = symbolOptions;
        moduleStatusMap_ = new Dictionary<BinaryFileDescriptor, ModuleStatus>();
        errorList_ = new List<string>();
    }

    public void AddModuleInfo(BinaryFileDescriptor binaryInfo, LoadState state, string optional = "") {
        var status = GetOrCreateModuleStatus(binaryInfo);
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
            Trace.WriteLine($"   - path: {pair.Value.ImageFile.ImagePath}");
            
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
