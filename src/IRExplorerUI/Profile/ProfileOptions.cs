using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
public class ProfileRecordingSessionOptions : SettingsBase, IEquatable<ProfileRecordingSessionOptions> {
    public const int DefaultSamplingFrequency = 4000;
    public const int MaximumSamplingFrequency = 8000;

    [ProtoMember(1)]
    public ProfileSessionKind SessionKind { get; set; }
    [ProtoMember(2)]
    public string ApplicationPath { get; set; }
    [ProtoMember(3)]
    public string ApplicationArguments { get; set; }
    [ProtoMember(4)]
    public string WorkingDirectory { get; set; }
    [ProtoMember(5)]
    public int SamplingFrequency { get; set; }
    [ProtoMember(6)]
    public bool ProfileDotNet { get; set; }
    [ProtoMember(7)]
    public bool ProfileChildProcesses { get; set; }
    [ProtoMember(8)]
    public bool RecordPerformanceCounters { get; set; }
    [ProtoMember(9)]
    public bool EnableEnvironmentVars { get; set; }
    [ProtoMember(10)]
    public List<(string Variable, string Value)> EnvironmentVariables { get; set; }
    [ProtoMember(11)]
    public List<PerformanceCounterConfig> PerformanceCounters { get; set; }
    [ProtoMember(12)]
    public string Title { get; set; }

    public List<PerformanceCounterConfig> EnabledPerformanceCounters => PerformanceCounters.FindAll(c => c.IsEnabled);
    public bool HasWorkingDirectory => Directory.Exists(WorkingDirectory);
    public bool HasTitle => !string.IsNullOrEmpty(Title);

    public ProfileRecordingSessionOptions() {
        Reset();
    }

    public override void Reset() {
        ResetAndInitializeReferenceMembers();
        SessionKind = ProfileSessionKind.StartProcess;
        SamplingFrequency = 4000; // 4 kHz, Xperf default is 1 kHz.
    }

    [ProtoAfterDeserialization]
    private void InitializeReferenceMembers() {
        EnvironmentVariables ??= new List<(string Variable, string Value)>();
        PerformanceCounters ??= new List<PerformanceCounterConfig>();
    }

    private void ResetAndInitializeReferenceMembers() {
        EnvironmentVariables?.Clear();
        InitializeReferenceMembers();
    }

    public bool AddPerformanceCounter(PerformanceCounterConfig config) {
        if (!PerformanceCounters.Contains(config)) {
            PerformanceCounters.Add(config);
            return true;
        }

        return false;
    }

    public ProfileRecordingSessionOptions Clone() {
        var serialized = StateSerializer.Serialize(this);
        return StateSerializer.Deserialize<ProfileRecordingSessionOptions>(serialized);
    }

    public bool Equals(ProfileRecordingSessionOptions other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return SessionKind == other.SessionKind && ApplicationPath == other.ApplicationPath && Title == other.Title &&
               ApplicationArguments == other.ApplicationArguments && WorkingDirectory == other.WorkingDirectory && 
               SamplingFrequency == other.SamplingFrequency && ProfileDotNet == other.ProfileDotNet &&
               ProfileChildProcesses == other.ProfileChildProcesses && 
               RecordPerformanceCounters == other.RecordPerformanceCounters && 
               EnableEnvironmentVars == other.EnableEnvironmentVars && 
               Enumerable.SequenceEqual(EnvironmentVariables, other.EnvironmentVariables) &&
               Enumerable.SequenceEqual(PerformanceCounters, other.PerformanceCounters);
    }

    public override bool Equals(object obj) {
        if (ReferenceEquals(null, obj)) {
            return false;
        }

        if (ReferenceEquals(this, obj)) {
            return true;
        }

        if (obj.GetType() != this.GetType()) {
            return false;
        }

        return Equals((ProfileRecordingSessionOptions)obj);
    }

    public override int GetHashCode() {
        var hashCode = new HashCode();
        hashCode.Add((int)SessionKind);
        hashCode.Add(Title);
        hashCode.Add(ApplicationPath);
        hashCode.Add(ApplicationArguments);
        hashCode.Add(WorkingDirectory);
        hashCode.Add(ProfileChildProcesses);
        hashCode.Add(RecordPerformanceCounters);
        hashCode.Add(EnableEnvironmentVars);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(ProfileRecordingSessionOptions left, ProfileRecordingSessionOptions right) {
        return Equals(left, right);
    }

    public static bool operator !=(ProfileRecordingSessionOptions left, ProfileRecordingSessionOptions right) {
        return !Equals(left, right);
    }
}

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
    public bool IncludePerformanceCounters { get; set; }
    [ProtoMember(9)]
    public ProfileRecordingSessionOptions RecordingSessionOptions { get; set; }
    [ProtoMember(10)]
    public List<PerformanceMetricConfig> PerformanceMetrics { get; set; }
    [ProtoMember(11)]
    public List<ProfileDataReport> PreviousRecordingSessions { get; set; }
    [ProtoMember(12)]
    public List<ProfileDataReport> PreviousLoadedSessions { get; set; }

    public bool HasBinaryNameWhitelist => BinaryNameWhitelistEnabled && BinaryNameWhitelist.Count > 0;
    public bool HasBinarySearchPaths => BinarySearchPathsEnabled && BinarySearchPaths.Count > 0;

    public ProfileDataProviderOptions() {
        Reset();
    }

    public override void Reset() {
        ResetAndInitializeReferenceMembers();
        DownloadBinaryFiles = true;
    }

    public bool AddPerformanceMetric(PerformanceMetricConfig config) {
        if (!PerformanceMetrics.Contains(config)) {
            PerformanceMetrics.Add(config);
            return true;
        }

        return false;
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
        PerformanceMetrics ??= new List<PerformanceMetricConfig>();
        PreviousRecordingSessions ??= new List<ProfileDataReport>();
        PreviousLoadedSessions ??= new List<ProfileDataReport>();
    }

    private void ResetAndInitializeReferenceMembers() {
        BinarySearchPaths?.Clear();
        BinaryNameWhitelist?.Clear();
        RecordingSessionOptions?.Reset();
        InitializeReferenceMembers();
    }

    public ProfileDataProviderOptions Clone() {
        var serialized = StateSerializer.Serialize(this);
        return StateSerializer.Deserialize<ProfileDataProviderOptions>(serialized);
    }
}
