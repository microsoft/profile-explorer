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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class ResolvedProfileStackFrame {
  public ResolvedProfileStackFrameDetails FrameDetails { get; set; }
  public virtual long FrameRVA { get; set; }

  public ResolvedProfileStackFrame(long frameRva, ResolvedProfileStackFrameDetails frameDetails) {
    FrameRVA = frameRva;
    FrameDetails = frameDetails;
  }

  protected ResolvedProfileStackFrame(ResolvedProfileStackFrameDetails frameDetails) {
    FrameDetails = frameDetails;
  }

  public static ResolvedProfileStackFrame
    CreateStackFrame(long frameRVA, ResolvedProfileStackFrameDetails frameDetails) {
    // Pick a type of frame that has an RVA field just large enough
    // to hold the value, this reduces memory usage since most RVAs don't need 64 bits.
    if ((ulong)frameRVA <= 0xFFFF) {
      return new ResolvedProfileStackFrame16((ushort)frameRVA, frameDetails);
    }
    else if ((ulong)frameRVA <= 0xFFFFFFFF) {
      return new ResolvedProfileStackFrame32((uint)frameRVA, frameDetails);
    }

    return new ResolvedProfileStackFrame(frameRVA, frameDetails);
  }

  public bool IsUnknown => FrameDetails.IsUnknown;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class ResolvedProfileStackFrame16 : ResolvedProfileStackFrame {
  private ushort frameRva_;

  public override long FrameRVA {
    get => frameRva_;
    set => frameRva_ = (ushort)value;
  }

  public ResolvedProfileStackFrame16(ushort frameRva, ResolvedProfileStackFrameDetails frameDetails) :
    base(frameDetails) {
    frameRva_ = frameRva;
  }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class ResolvedProfileStackFrame32 : ResolvedProfileStackFrame {
  private uint frameRva_;

  public override long FrameRVA {
    get => frameRva_;
    set => frameRva_ = (ushort)value;
  }

  public ResolvedProfileStackFrame32(uint frameRva, ResolvedProfileStackFrameDetails frameDetails) :
    base(frameDetails) {
    frameRva_ = frameRva;
  }
}

public sealed class ResolvedProfileStack {
  // Used to deduplicate stack frames for the same function running in the same context.
  public static ConcurrentDictionary<ResolvedProfileStackFrameDetails, ResolvedProfileStackFrameDetails> uniqueFrames_ =
    new();

  // Stack frames with the same IP have a unique instance shared among all call stacks.
  public static ConcurrentDictionary<long, ResolvedProfileStackFrame> frameInstances_ = new();
  public static ConcurrentDictionary<long, ResolvedProfileStackFrame> kernelFrameInstances_ = new();

  public ResolvedProfileStack(int frameCount, ProfileContext context) {
    StackFrames = new List<ResolvedProfileStackFrame>(frameCount);
    Context = context;
  }

  public List<ResolvedProfileStackFrame> StackFrames { get; set; }
  public ProfileContext Context { get; set; }
  public int FrameCount => StackFrames.Count;

  public void AddFrame(long frameIP, long frameRVA, ResolvedProfileStackFrameDetails frameDetails, int frameIndex,
                       ProfileStack stack) {
    // Deduplicate the frame.
    var uniqueFrame = uniqueFrames_.GetOrAdd(frameDetails, frameDetails);
    var rvaFrame = ResolvedProfileStackFrame.CreateStackFrame(frameRVA, uniqueFrame);

    // A stack frame IP can be called from both user and kernel mode code.
    frameDetails.IsKernelCode = frameIndex < stack.UserModeTransitionIndex;
    var existingFrame = frameDetails.IsKernelCode ?
      kernelFrameInstances_.GetOrAdd(frameIP, rvaFrame) :
      frameInstances_.GetOrAdd(frameIP, rvaFrame);
    StackFrames.Add(existingFrame);
  }
}

public sealed class ResolvedProfileStackFrameDetails : IEquatable<ResolvedProfileStackFrameDetails> {
  public static readonly ResolvedProfileStackFrameDetails Unknown = new();

  public ResolvedProfileStackFrameDetails(FunctionDebugInfo debugInfo, IRTextFunction function,
                                          ProfileImage image, bool isManagedCode) {
    DebugInfo = debugInfo;
    Function = function;
    Image = image;
    IsManagedCode = isManagedCode;
  }

  private ResolvedProfileStackFrameDetails() { }
  public FunctionDebugInfo DebugInfo { get; set; }
  public IRTextFunction Function { get; set; }
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
