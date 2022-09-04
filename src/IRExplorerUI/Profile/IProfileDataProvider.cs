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

public delegate void ProfileLoadProgressHandler(ProfileLoadProgress info);
public delegate void ProcessListProgressHandler(ProcessListProgress info);

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
    //[ProtoMember(5)]
    //public List<(ProfileImage Image, TimeSpan Weight)> ImageWeights;
    
    public ProcessSummary(ProfileProcess process, TimeSpan weight) {
        Process = process;
        Weight = weight;
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

public enum ProfileLoadStage {
    TraceLoading,
    BinaryLoading,
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

public class ProcessListProgress {
    public int Total { get; set; }
    public int Current { get; set; }
    public List<ProcessSummary> Processes { get; set; }
}


public enum ProfileSessionKind {
    SystemWide,
    StartProcess,
    AttachToProcess
}
