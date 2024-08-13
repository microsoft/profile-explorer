// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ProfileExplorer.Core;
using ProfileExplorer.UI.Compilers;
using ProtoBuf;

namespace ProfileExplorer.UI.Profile;

// Represents a resolved stack frame with details about the function and image it belongs to.
// To reduce memory usage, the RVA field is stored in a derived class with the smallest possible size,
// with the most common RVAs being values that fit in 16 or 32 bits.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public class ResolvedProfileStackFrame {
  public ResolvedProfileStackFrameDetails FrameDetails { get; set; }
  public virtual long FrameRVA { get; set; }
  public long FrameIP { get; set; }

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

// Stack frame with 16-bit RVA, used to reduce memory usage.
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

// Stack frame with 32-bit RVA, used to reduce memory usage.
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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public sealed class ResolvedProfileStack {
  // Used to deduplicate stack frames for the same function running in the same context.
  public static ConcurrentDictionary<ResolvedProfileStackFrameKey, ResolvedProfileStackFrameDetails> uniqueFrames_ =
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

  public void AddFrame(IRTextFunction function, long frameIP, long frameRVA, int frameIndex,
                       ResolvedProfileStackFrameKey frameDetails, ProfileStack stack, int pointerSize) {
    // Deduplicate the frame.
    var uniqueFrame = uniqueFrames_.GetOrAdd(frameDetails, CreateResolvedProfileStackFrameDetails, function);
    var rvaFrame = ResolvedProfileStackFrame.CreateStackFrame(frameRVA, uniqueFrame);
    rvaFrame.FrameIP = frameIP;

    // A stack frame IP can be called from both user and kernel mode code.
    uniqueFrame.IsKernelCode = frameIndex < stack.UserModeTransitionIndex;

    if (ETWEventProcessor.IsKernelAddress((ulong)frameIP, 8) && !uniqueFrame.IsKernelCode) {
      uniqueFrame.IsKernelCode = true;
    }

    var existingFrame = uniqueFrame.IsKernelCode ?
      kernelFrameInstances_.GetOrAdd(frameIP, rvaFrame) :
      frameInstances_.GetOrAdd(frameIP, rvaFrame);
    StackFrames.Add(existingFrame);
  }

  private static ResolvedProfileStackFrameDetails CreateResolvedProfileStackFrameDetails(
    ResolvedProfileStackFrameKey frameDetails, IRTextFunction function) {
    return new ResolvedProfileStackFrameDetails(frameDetails.DebugInfo, function, frameDetails.Image,
                                                frameDetails.IsManagedCode);
  }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
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

// Stack-allocated version of ResolvedProfileStackFrameDetails, used only
// when adding a new stack frame to reduce GC pressure if it already exists.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ResolvedProfileStackFrameKey : IEquatable<ResolvedProfileStackFrameKey> {
  public static readonly ResolvedProfileStackFrameKey Unknown = new();

  public ResolvedProfileStackFrameKey(FunctionDebugInfo debugInfo,
                                      ProfileImage image, bool isManagedCode) {
    DebugInfo = debugInfo;
    Image = image;
    IsManagedCode = isManagedCode;
  }

  public ResolvedProfileStackFrameKey() {}

  public FunctionDebugInfo DebugInfo;
  public ProfileImage Image;
  public bool IsManagedCode;

  public override bool Equals(object obj) {
    return ReferenceEquals(this, obj) || obj is ResolvedProfileStackFrameKey other && Equals(other);
  }

  public override int GetHashCode() {
    return HashCode.Combine(DebugInfo, Image);
  }

  public bool Equals(ResolvedProfileStackFrameKey other) {
    if (ReferenceEquals(null, other)) {
      return false;
    }

    if (ReferenceEquals(this, other)) {
      return true;
    }

    return Equals(DebugInfo, other.DebugInfo) &&
           Equals(Image, other.Image);
  }
}