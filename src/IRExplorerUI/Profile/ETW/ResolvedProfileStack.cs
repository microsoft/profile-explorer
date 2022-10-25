using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
public sealed class ResolvedProfileStack {
    [ProtoMember(1)]
    public List<ResolvedProfileStackFrame> StackFrames { get; set; }
    [ProtoMember(2)]
    public ProfileContext Context { get; set; }
    
    public int FrameCount => StackFrames.Count;

    // Used to deduplicate stack frames for the same function running in the same context.
    public static ConcurrentDictionary<ResolvedProfileStackInfo, ResolvedProfileStackInfo> uniqueFrames_ = new();

    // Stack frames with the same IP have a unique instance shared among all call stacks.
    private static ConcurrentDictionary<long, ResolvedProfileStackFrame> frameInstances_ = new();
    private static ConcurrentDictionary<long, ResolvedProfileStackFrame> kernelFrameInstances_ = new();
    
    public void AddFrame(long frameIP, long frameRVA, ResolvedProfileStackInfo info, int frameIndex, ProfileStack stack) {
        // Deduplicate the frame.
        var uniqueFrame = uniqueFrames_.GetOrAdd(info, info);
        var rvaFrame = new ResolvedProfileStackFrame(frameRVA, uniqueFrame);

        // A stack frame IP can be called from both user and kernel mode code.
        info.IsKernelCode = frameIndex < stack.UserModeTransitionIndex;
        var existingFrame = info.IsKernelCode ?
            kernelFrameInstances_.GetOrAdd(frameIP, rvaFrame) :
            frameInstances_.GetOrAdd(frameIP, rvaFrame);
        StackFrames.Add(existingFrame);
    }

    public ResolvedProfileStack(int frameCount, ProfileContext context) {
        StackFrames = new List<ResolvedProfileStackFrame>(frameCount);
        Context = context;
    }
}


[ProtoContract(SkipConstructor = true)]
public sealed class ResolvedProfileStackInfo : IEquatable<ResolvedProfileStackInfo> {
    [ProtoMember(1)]
    public FunctionDebugInfo DebugInfo { get; set; }
    [ProtoMember(2)]
    public IRTextFunctionReference Function { get; set; }
    [ProtoMember(3)]
    public ProfileImage Image { get; set; }
    public FunctionProfileData Profile { get; set; }
    public bool IsKernelCode { get; set; }
    public bool IsManagedCode { get; set; }

    public bool IsUnknown => Image == null;

    private ResolvedProfileStackInfo() { }

    public ResolvedProfileStackInfo(FunctionDebugInfo debugInfo, IRTextFunction function,
        ProfileImage image, bool isManagedCode, FunctionProfileData profile = null) {
        DebugInfo = debugInfo;
        Function = function;
        Image = image;
        IsManagedCode = isManagedCode;
        Profile = profile;
    }

    public static readonly ResolvedProfileStackInfo Unknown = new();

    public bool Equals(ResolvedProfileStackInfo other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Equals(DebugInfo, other.DebugInfo) &&
               Equals(Image, other.Image) &&
               IsKernelCode == other.IsKernelCode &&
               IsManagedCode == other.IsManagedCode;
    }

    public override bool Equals(object obj) {
        return ReferenceEquals(this, obj) || obj is ResolvedProfileStackInfo other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(DebugInfo, Image, IsKernelCode, IsManagedCode);
    }

    public static bool operator ==(ResolvedProfileStackInfo left, ResolvedProfileStackInfo right) {
        return Equals(left, right);
    }

    public static bool operator !=(ResolvedProfileStackInfo left, ResolvedProfileStackInfo right) {
        return !Equals(left, right);
    }
}

[ProtoContract(SkipConstructor = true)]
public struct ResolvedProfileStackFrame {
    [ProtoMember(1)]
    public long FrameRVA { get; set; }
    [ProtoMember(2)]
    public ResolvedProfileStackInfo Info { get; set; }

    public ResolvedProfileStackFrame(long frameRva, ResolvedProfileStackInfo info) {
        FrameRVA = frameRva;
        Info = info;
    }

    public bool IsUnknown => Info.IsUnknown;
}