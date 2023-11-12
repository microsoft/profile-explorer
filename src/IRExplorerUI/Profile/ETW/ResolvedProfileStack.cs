// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using IRExplorerCore;
using IRExplorerUI.Compilers;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ResolvedProfileStackFrame {
  [ProtoMember(1)]
  public long FrameRVA { get; set; }
  [ProtoMember(2)]
  public ResolvedProfileStackFrameDetails FrameDetails { get; set; }

  public ResolvedProfileStackFrame(long frameRva, ResolvedProfileStackFrameDetails frameDetails) {
    FrameRVA = frameRva;
    FrameDetails = frameDetails;
  }

  public bool IsUnknown => FrameDetails.IsUnknown;
}

[ProtoContract(SkipConstructor = true)]
public sealed class ResolvedProfileStack {
  // Used to deduplicate stack frames for the same function running in the same context.
  public static ConcurrentDictionary<ResolvedProfileStackFrameDetails, ResolvedProfileStackFrameDetails> uniqueFrames_ =
    new ConcurrentDictionary<ResolvedProfileStackFrameDetails, ResolvedProfileStackFrameDetails>();

  // Stack frames with the same IP have a unique instance shared among all call stacks.
  private static ConcurrentDictionary<long, ResolvedProfileStackFrame> frameInstances_ =
    new ConcurrentDictionary<long, ResolvedProfileStackFrame>();
  private static ConcurrentDictionary<long, ResolvedProfileStackFrame> kernelFrameInstances_ =
    new ConcurrentDictionary<long, ResolvedProfileStackFrame>();

  public ResolvedProfileStack(int frameCount, ProfileContext context) {
    StackFrames = new List<ResolvedProfileStackFrame>(frameCount);
    Context = context;
  }

  [ProtoMember(1)]
  public List<ResolvedProfileStackFrame> StackFrames { get; set; }
  [ProtoMember(2)]
  public ProfileContext Context { get; set; }
  public int FrameCount => StackFrames.Count;

  public void AddFrame(long frameIP, long frameRVA, ResolvedProfileStackFrameDetails frameDetails, int frameIndex,
                       ProfileStack stack) {
    // Deduplicate the frame.
    var uniqueFrame = uniqueFrames_.GetOrAdd(frameDetails, frameDetails);
    var rvaFrame = new ResolvedProfileStackFrame(frameRVA, uniqueFrame);

    // A stack frame IP can be called from both user and kernel mode code.
    frameDetails.IsKernelCode = frameIndex < stack.UserModeTransitionIndex;
    var existingFrame = frameDetails.IsKernelCode ?
      kernelFrameInstances_.GetOrAdd(frameIP, rvaFrame) :
      frameInstances_.GetOrAdd(frameIP, rvaFrame);
    StackFrames.Add(existingFrame);
  }
}

[ProtoContract(SkipConstructor = true)]
public sealed class ResolvedProfileStackFrameDetails : IEquatable<ResolvedProfileStackFrameDetails> {
  public static readonly ResolvedProfileStackFrameDetails Unknown = new ResolvedProfileStackFrameDetails();

  public ResolvedProfileStackFrameDetails(FunctionDebugInfo debugInfo, IRTextFunction function,
                                          ProfileImage image, bool isManagedCode) {
    DebugInfo = debugInfo;
    Function = function;
    Image = image;
    IsManagedCode = isManagedCode;
  }

  private ResolvedProfileStackFrameDetails() { }
  [ProtoMember(1)]
  public FunctionDebugInfo DebugInfo { get; set; }
  [ProtoMember(2)]
  //? TODO: Remove Reference, bloated, needed only to serialize
  public IRTextFunctionReference Function { get; set; }
  [ProtoMember(3)]
  public ProfileImage Image { get; set; }
  public bool IsKernelCode { get; set; }
  public bool IsManagedCode { get; set; }
  public bool IsUnknown => Image == null;

  public static bool operator ==(ResolvedProfileStackFrameDetails left, ResolvedProfileStackFrameDetails right) {
    return Equals(left, right);
  }

  public static bool operator !=(ResolvedProfileStackFrameDetails left, ResolvedProfileStackFrameDetails right) {
    return !Equals(left, right);
  }

  public override bool Equals(object obj) {
    return ReferenceEquals(this, obj) || obj is ResolvedProfileStackFrameDetails other && Equals(other);
  }

  public override int GetHashCode() {
    return HashCode.Combine(DebugInfo, Image, IsKernelCode, IsManagedCode);
  }

  public bool Equals(ResolvedProfileStackFrameDetails other) {
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
}
