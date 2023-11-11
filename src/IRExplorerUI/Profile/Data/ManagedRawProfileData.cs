﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using IRExplorerCore.Utilities;
using IRExplorerUI.Compilers;
using ProtoBuf;

namespace IRExplorerUI.Profile;

public class ManagedRawProfileData {
    [ProtoContract(SkipConstructor = true)]
    public class ManagedDataState {
        // list of DotNetDebugInfoProvider {id, file_name, arch}

        public Dictionary<ProfileImage, int /* providerId */> ImageDebugInfo;
        public Dictionary<long /* moduleId */, int /* providerId */> moduleDebugInfoMap_;
        public Dictionary<ManagedMethodId, int /* mappingId */> managedMethodIdMap_;
        public Dictionary<string, int /* mappingId */> managedMethodsMap_;
        public List<ManagedMethodMapping> managedMethods_;

    }

    public Dictionary<ProfileImage, DotNetDebugInfoProvider> imageDebugInfo_;
    public Dictionary<long /* moduleId */, DotNetDebugInfoProvider> moduleDebugInfoMap_;
    public Dictionary<long /* moduleId */, ProfileImage> moduleImageMap_;
    public Dictionary<ManagedMethodId, ManagedMethodMapping> managedMethodIdMap_;
    public Dictionary<ManagedMethodId, DotNetDebugInfoProvider.MethodCode> managedMethodCodeMap_;
    public Dictionary<string, ManagedMethodMapping> managedMethodsMap_;
    public List<ManagedMethodMapping> managedMethods_;
    public List<(long ModuleId, ManagedMethodMapping Mapping)> patchedMappings_;

    public ManagedRawProfileData() {
        imageDebugInfo_ = new Dictionary<ProfileImage, DotNetDebugInfoProvider>();
        moduleDebugInfoMap_ = new Dictionary<long, DotNetDebugInfoProvider>();
        moduleImageMap_ = new Dictionary<long, ProfileImage>();
        managedMethods_ = new List<ManagedMethodMapping>();
        managedMethodsMap_ = new Dictionary<string, ManagedMethodMapping>();
        managedMethodCodeMap_ = new Dictionary<ManagedMethodId, DotNetDebugInfoProvider.MethodCode>();
        managedMethodIdMap_ = new Dictionary<ManagedMethodId, ManagedMethodMapping>();
        patchedMappings_ = new List<(long ModuleId, ManagedMethodMapping Mapping)>();
    }

    public void LoadingCompleted(int processId) {
        managedMethods_.Sort();

        foreach (var debugInfo in imageDebugInfo_.Values) {
            debugInfo.LoadingCompleted();
        }

        foreach(var (methodId, code) in managedMethodCodeMap_) {
            if (managedMethodIdMap_.TryGetValue(methodId, out var mapping) &&
                moduleDebugInfoMap_.TryGetValue(mapping.ModuleId, out var debugInfo)) {
                debugInfo.AddMethodCode(code.Address, code);
            }
        }

        // if (!string.IsNullOrEmpty(managedAsmDir)) {
        //     foreach (var debugInfo in moduleDebugInfoMap_.Values) {
        //         var asmFilePath = Path.Combine(managedAsmDir, $"{processId}.asm");
        //         debugInfo.ManagedAsmFilePath = asmFilePath;
        //     }
        // }

        // A placeholder is created for cases where the method load event
        // is triggered before the module load one, try to assign the image now.
        foreach (var pair in patchedMappings_) {
            pair.Mapping.Image = moduleImageMap_.GetValueOrNull(pair.ModuleId);
        }

        patchedMappings_ = null;
    }
}


[ProtoContract(SkipConstructor = true)]
public class ManagedMethodMapping : IComparable<ManagedMethodMapping>, IComparable<long>, IEquatable<ManagedMethodMapping> {
    public ManagedMethodMapping(FunctionDebugInfo functionDebugInfo, ProfileImage image,
        long moduleId, long ip, int size) {
        FunctionDebugInfo = functionDebugInfo;
        Image = image;
        ModuleId = moduleId;
        IP = ip;
        Size = size;
    }

    [ProtoMember(1)]
    public FunctionDebugInfo FunctionDebugInfo { get; }
    [ProtoMember(2)]
    public ProfileImage Image { get; set; }
    [ProtoMember(3)]
    public long ModuleId { get; }
    [ProtoMember(4)]
    public long IP { get; }
    [ProtoMember(5)]
    public int Size { get; }

    public int CompareTo(long value) {
        if (value < IP) {
            return 1;
        }
        if (value > IP + Size) {
            return -1;
        }

        return 0;
    }

    public bool Equals(ManagedMethodMapping other) {
        if (other == null)
            return false;
        return IP == other.IP;
    }

    public int CompareTo(ManagedMethodMapping other) {
        return CompareTo(other.IP);
    }

    public override bool Equals(object obj) {
        return obj is ManagedMethodMapping other && Equals(other);
    }

    public override int GetHashCode() {
        return IP.GetHashCode();
    }
}

public record ManagedMethodId(long MethodId, long ReJITId);
