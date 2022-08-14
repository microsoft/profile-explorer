using System;
using System.Collections.Generic;
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
    //? list of failed module loads
    //? list of PDB status (found, file, hash)
    //? other errors
    //? - similar to the UTC parser errors

    public enum LoadStatus {
        Success,
        NotFound,
        Failed
    }

    // SymbolFileSourceOptions
    // ProfileRecordingSessionOptions

    // dict -> {bin status, optional, debugSearch}

    public void AddModuleInfo(BinaryFileDescriptor binaryInfo, LoadStatus status, string optional = "") {

    }

    public void AddDebugInfo(BinaryFileDescriptor binaryInfo, DebugFileSearchResult searchResult) {

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
