﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using IRExplorerUI.Compilers;
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