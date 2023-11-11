// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using IRExplorerUI.Compilers;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
public class ProfileDataReport : IEquatable<ProfileDataReport> {
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

        public bool HasBinaryLoaded => State == ModuleLoadState.Loaded;
        public bool HasDebugInfoLoaded => DebugInfoFile is { Found: true };
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
    public bool IsStartProcessSession => SessionOptions is { SessionKind: ProfileSessionKind.StartProcess };
    public bool IsAttachToProcessSession => SessionOptions is { SessionKind: ProfileSessionKind.AttachToProcess };
    public List<ModuleStatus> Modules => moduleStatusMap_.ToValueList();

    public TimeSpan SamplingInterval {
        get {
            if (TraceInfo.SamplingInterval.Ticks > 0) {
                return TraceInfo.SamplingInterval;
            }
            else if(SessionOptions != null) {
                return TimeSpan.FromMilliseconds(1.0 / SessionOptions.SamplingFrequency);
            }
            else {
                return TimeSpan.Zero;
            }
        }
    }

    public ProfileDataReport() {
        moduleStatusMap_ = new Dictionary<BinaryFileDescriptor, ModuleStatus>();
    }

    public void AddModuleInfo(BinaryFileDescriptor binaryInfo, BinaryFileSearchResult binaryFile, ModuleLoadState state) {
        lock (this) {
            var status = GetOrCreateModuleStatus(binaryInfo);
            status.BinaryFileInfo = binaryFile;
            status.State = state;
        }
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
        lock (this) {
            var status = GetOrCreateModuleStatus(binaryInfo);
            status.DebugInfoFile = searchResult;
        }
    }

    public ModuleStatus GetModuleStatus(string moduleName) {
        lock (this) {
            return Modules.Find(module => module.ImageFileInfo.ImageName.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool Equals(ProfileDataReport other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Equals(SessionOptions, other.SessionOptions) &&
               TraceInfo.HasSameTraceFilePath(other.TraceInfo);
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

        return Equals((ProfileDataReport)obj);
    }

    public override int GetHashCode() {
        return HashCode.Combine(SessionOptions);
    }

    public static bool operator ==(ProfileDataReport left, ProfileDataReport right) {
        return Equals(left, right);
    }

    public static bool operator !=(ProfileDataReport left, ProfileDataReport right) {
        return !Equals(left, right);
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