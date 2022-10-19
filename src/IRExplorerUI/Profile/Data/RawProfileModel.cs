﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
public class ProfileStack : IEquatable<ProfileStack> {
    private const int MaxFrameNumber = 512;
    private static long[][] TempFrameArrays = new long[MaxFrameNumber + 1][];

    static ProfileStack() {
        // The frame ptr. array is being interned (unique instance)
        // later, use a pre-allocated array initially to reduce GC pressure.
        // Note: this also means creating ProfileStack must be single-threaded.
        for (int i = 0; i <= MaxFrameNumber; i++) {
            TempFrameArrays[i] = new long[i];
        }
    }

    public ProfileStack() {
        FramePointers = null;
        ContextId = 0;
    }

    public ProfileStack(int contextId, int frameCount) {
        FramePointers = null;
        ContextId = contextId;
        FramePointers = RentArray(frameCount);
    }

    private object optionalData_;

    [ProtoMember(1)]
    public long[] FramePointers { get; set; }
    [ProtoMember(2)]
    public int ContextId { get; set; }
    [ProtoMember(3)]
    public int UserModeTransitionIndex{ get; set; }

    public bool IsUnknown => FramePointers == null;
    public int FrameCount => FramePointers.Length;
    public static readonly ProfileStack Unknown = new ProfileStack();

    public object GetOptionalData() {
        Interlocked.MemoryBarrierProcessWide();
        return optionalData_;
    }

    public void SetOptionalData(object value) {
        optionalData_ = value;
        Interlocked.MemoryBarrierProcessWide();
    }

    public long[] CloneFramePointers() {
        long[] clone = new long[FramePointers.Length];
        FramePointers.CopyTo(clone, 0);
        return clone;
    }

    public void SetTempFramePointers(int frameCount) {
        FramePointers = RentArray(frameCount);
    }

    public void SubstituteFramePointers(long[] data) {
        ReturnArray(FramePointers);
        FramePointers = data;
    }

    public ProfileImage FindImageForFrame(int frameIndex, RawProfileData profileData) {
        return profileData.FindImageForIP(FramePointers[frameIndex], profileData.FindContext(ContextId));
    }

    private long[] RentArray(int frameCount) {
        // In most cases one of the pre-allocated temporary arrays can be used.
        long[] array;

        if (frameCount <= MaxFrameNumber) {
            array = TempFrameArrays[frameCount];
#if DEBUG
                Debug.Assert(array != null);
                TempFrameArrays[frameCount] = null;
#endif
        }
        else {
            array = new long[frameCount];
        }

        return array;
    }

    private void ReturnArray(long[] array) {
#if DEBUG
            if (array.Length <= MaxFrameNumber) {
                TempFrameArrays[array.Length] = array;
            }
#endif
    }

    public bool Equals(ProfileStack other) {
        Debug.Assert(other != null);

        if (ReferenceEquals(this, other)) {
            return true;
        }

        // FramePointers is allocated using interning, ref. equality is sufficient.
        return ContextId == other.ContextId &&
               FramePointers == other.FramePointers;
    }

    public override bool Equals(object obj) {
        return obj is ProfileStack other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(FramePointers, ContextId);
    }

    public static bool operator ==(ProfileStack left, ProfileStack right) {
        return Equals(left, right);
    }

    public static bool operator !=(ProfileStack left, ProfileStack right) {
        return !Equals(left, right);
    }

    public override string ToString() {
        return $"#{FrameCount}, ContextId: {ContextId}";
    }
}

[ProtoContract(SkipConstructor = true)]
public class ProfileTraceInfo {
    [ProtoMember(1)]
    public DateTime ProfileStartTime { get; set; }
    [ProtoMember(2)]
    public DateTime ProfileEndTime { get; set; }
    [ProtoMember(3)]
    public int CpuCount { get; set; }
    [ProtoMember(4)]
    public int CpuSpeed { get; set; }
    [ProtoMember(5)]
    public int PointerSize { get; set; }
    [ProtoMember(6)]
    public int MemorySize { get; set; }
    [ProtoMember(7)]
    public string ComputerName { get; set; }
    [ProtoMember(8)]
    public string DomainName { get; set; }
    [ProtoMember(9)]
    public string TraceFilePath { get; set; }

    public bool Is64Bit => PointerSize == 8;
    public TimeSpan ProfileDuration => ProfileEndTime - ProfileStartTime;

    public bool HasSameTraceFilePath(ProfileTraceInfo other) {
        if (!string.IsNullOrEmpty(TraceFilePath) &&
            !string.IsNullOrEmpty(other.TraceFilePath)) {
            return string.Equals(TraceFilePath, other.TraceFilePath, StringComparison.OrdinalIgnoreCase);
        }

        return string.IsNullOrEmpty(TraceFilePath) &&
               string.IsNullOrEmpty(other.TraceFilePath);
    }
}

[ProtoContract(SkipConstructor = true)]
public struct ProfileSample : IEquatable<ProfileSample> {
    [ProtoMember(1)]
    public long IP { get; set; }
    [ProtoMember(2)]
    public TimeSpan Time { get; set; }
    [ProtoMember(3)]
    public TimeSpan Weight { get; set; }
    [ProtoMember(4)]
    public int StackId { get; set; }
    [ProtoMember(5)]
    public int ContextId { get; set; }
    [ProtoMember(6)]
    public bool IsKernelCode { get; set; }

    public bool HasStack => StackId != 0;

    //public ProfileSample() {}

    public ProfileSample(long ip, TimeSpan time, TimeSpan weight, bool isKernelCode, int contextId) {
        IP = ip;
        Time = time;
        Weight = weight;
        StackId = 0;
        IsKernelCode = isKernelCode;
        ContextId = contextId;
    }

    public ProfileStack GetStack(RawProfileData profileData) {
        return StackId != 0 ? profileData.FindStack(StackId) : ProfileStack.Unknown;
    }

    public ProfileContext GetContext(RawProfileData profileData) {
        return profileData.FindContext(ContextId);
    }

    public bool Equals(ProfileSample other) {
        return IP == other.IP &&
               Time == other.Time &&
               Weight == other.Weight &&
               StackId == other.StackId &&
               ContextId == other.ContextId &&
               IsKernelCode == other.IsKernelCode;
    }

    public override bool Equals(object obj) {
        return obj is ProfileSample other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(IP, Time, StackId, ContextId);
    }

    public static bool operator ==(ProfileSample left, ProfileSample right) {
        return Equals(left, right);
    }

    public static bool operator !=(ProfileSample left, ProfileSample right) {
        return !Equals(left, right);
    }

    public override string ToString() {
        return $"{IP:X}, Weight: {Weight.Ticks}, StackId: {StackId}";
    }
}

[ProtoContract(SkipConstructor = true)]
public sealed class ProfileContext : IEquatable<ProfileContext> {
    public ProfileContext() { }

    public ProfileContext(int processId, int threadId, int processorNumber) {
        ProcessId = processId;
        ThreadId = threadId;
        ProcessorNumber = processorNumber;
    }

    [ProtoMember(1)]
    public int ProcessId { get; set; }
    [ProtoMember(2)]
    public int ThreadId { get; set; }
    [ProtoMember(3)]
    public int ProcessorNumber { get; set; }

    public ProfileProcess GetProcess(RawProfileData profileData) {
        return profileData.GetOrCreateProcess(ProcessId);
    }

    public ProfileThread GetThread(RawProfileData profileData) {
        return profileData.FindThread(ThreadId);
    }

    public bool Equals(ProfileContext other) {
        Debug.Assert(other != null);

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return ProcessId == other.ProcessId &&
               ThreadId == other.ThreadId &&
               ProcessorNumber == other.ProcessorNumber;
    }

    public override bool Equals(object other) {
        Debug.Assert(other != null);

        if (ReferenceEquals(this, other)) {
            return true;
        }

        if (other.GetType() != this.GetType()) {
            return false;
        }

        return Equals((ProfileContext)other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(ProcessId, ThreadId, ProcessorNumber);
    }

    public static bool operator ==(ProfileContext left, ProfileContext right) {
        return Equals(left, right);
    }

    public static bool operator !=(ProfileContext left, ProfileContext right) {
        return !Equals(left, right);
    }

    public override string ToString() {
        return $"ProcessId: {ProcessId}, ThreadId: {ThreadId}, Processor: {ProcessorNumber}";
    }
}

[ProtoContract(SkipConstructor = true)]
public sealed class ProfileImage : IEquatable<ProfileImage>, IComparable<ProfileImage> {
    [ProtoMember(1)]
    public int Id { get; set; }
    [ProtoMember(2)]
    public int Size { get; set; }
    [ProtoMember(3)]
    public long BaseAddress { get; set; }
    [ProtoMember(4)]
    public long DefaultBaseAddress { get; set; }
    [ProtoMember(5)]
    public string FilePath { get; set; }
    [ProtoMember(6)]
    public string OriginalFileName { get; set; }
    [ProtoMember(7)]
    public int TimeStamp { get; set; }
    [ProtoMember(8)]
    public long Checksum { get; set; }

    public long BaseAddressEnd => BaseAddress + Size;

    public string ModuleName => !string.IsNullOrWhiteSpace(OriginalFileName) ?
                                Utils.TryGetFileName(OriginalFileName) :
                                Utils.TryGetFileName(FilePath);

    public ProfileImage() { }

    public ProfileImage(string filePath, string originalFileName,
        long baseAddress, long defaultBaseAddress,
        int size, int timeStamp, long checksum) {
        Size = size;
        FilePath = filePath;
        OriginalFileName = originalFileName;
        BaseAddress = baseAddress;
        DefaultBaseAddress = defaultBaseAddress;
        TimeStamp = timeStamp;
        Checksum = checksum;
    }

    public bool HasAddress(long ip) {
        return (ip >= BaseAddress) && (ip < (BaseAddress + Size));
    }

    public override int GetHashCode() {
        return HashCode.Combine(Size, FilePath, BaseAddress, DefaultBaseAddress, Checksum);
    }

    public bool Equals(ProfileImage other) {
        Debug.Assert(other != null);

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Size == other.Size &&
               FilePath == other.FilePath &&
               BaseAddress == other.BaseAddress &&
               DefaultBaseAddress == other.DefaultBaseAddress &&
               Checksum == other.Checksum;
    }

    public override bool Equals(object other) {
        Debug.Assert(other != null);

        if (ReferenceEquals(this, other)) {
            return true;
        }

        if (other.GetType() != this.GetType()) {
            return false;
        }

        return Equals((ProfileImage)other);
    }

    public static bool operator ==(ProfileImage left, ProfileImage right) {
        return Equals(left, right);
    }

    public static bool operator !=(ProfileImage left, ProfileImage right) {
        return !Equals(left, right);
    }

    public int CompareTo(ProfileImage other) {
        if (BaseAddress < other.BaseAddress && BaseAddressEnd < other.BaseAddressEnd) {
            return -1;
        }
        if (BaseAddress > other.BaseAddress && BaseAddressEnd > other.BaseAddressEnd) {
            return 1;
        }
        return 0;
    }

    public int CompareTo(long value) {
        if (value < BaseAddress) {
            return 1;
        }
        if (value > BaseAddressEnd) {
            return -1;
        }

        return 0;
    }

    public override string ToString() {
        return $"{FilePath}, Id: {Id}, Base: {BaseAddress:X}, DefaultBase: {DefaultBaseAddress:X} Size: {Size}";
    }
}

[ProtoContract(SkipConstructor = true)]
public sealed class ProfileProcess : IEquatable<ProfileProcess> {
    [ProtoMember(1)]
    public int ProcessId { get; set; }
    [ProtoMember(2)]
    public int ParentId { get; set; }
    [ProtoMember(3)]
    public string Name { get; set; }
    [ProtoMember(4)]
    public string ImageFileName { get; set; }
    [ProtoMember(5)]
    public string CommandLine { get; set; }
    [ProtoMember(6)]
    public List<int> ImageIds { get; set; }
    [ProtoMember(7)]
    public List<int> ThreadIds { get; set; }

    public ProfileProcess() {
        InitializeReferenceMembers();
    }

    [ProtoAfterDeserialization]
    private void InitializeReferenceMembers() {
        ImageIds ??= new List<int>();
        ThreadIds ??= new List<int>();
    }

    public IEnumerable<ProfileImage> Images(RawProfileData profileData) {
        foreach (var id in ImageIds) {
            yield return profileData.FindImage(id);
        }
    }

    public IEnumerable<ProfileThread> Threads(RawProfileData profileData) {
        foreach (var id in ThreadIds) {
            yield return profileData.FindThread(id);
        }
    }

    public ProfileProcess(int processId, int parentId, string name,
        string imageFileName, string commandLine) : this() {
        ProcessId = processId;
        ParentId = parentId;
        Name = name;
        ImageFileName = imageFileName;
        CommandLine = commandLine;
    }

    public void AddImage(int imageId) {
        if (!ImageIds.Contains(imageId)) {
            ImageIds.Add(imageId);
        }
    }

    public void AddThread(int threadId) {
        if (!ThreadIds.Contains(threadId)) {
            ThreadIds.Add(threadId);
        }
    }

    public bool Equals(ProfileProcess other) {
        Debug.Assert(other != null);

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return ProcessId == other.ProcessId &&
               Name == other.Name;
    }

    public override bool Equals(object other) {
        Debug.Assert(other != null);

        if (ReferenceEquals(this, other)) {
            return true;
        }

        if (other.GetType() != this.GetType()) {
            return false;
        }

        return Equals((ProfileProcess)other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(ProcessId, Name);
    }

    public static bool operator ==(ProfileProcess left, ProfileProcess right) {
        return Equals(left, right);
    }

    public static bool operator !=(ProfileProcess left, ProfileProcess right) {
        return !Equals(left, right);
    }

    public override string ToString() {
        return $"{Name}, ImageFileName {ImageFileName}, ID: {ProcessId}, ParentID: {ParentId}, Images: {ImageIds.Count}, Threads: {ThreadIds.Count}";
    }
}

[ProtoContract(SkipConstructor = true)]
public sealed class ProfileThread : IEquatable<ProfileThread> {
    [ProtoMember(1)]
    public int ThreadId { get; set; }
    [ProtoMember(2)]
    public int ProcessId { get; set; }
    [ProtoMember(3)]
    public string Name { get; set; }

    public ProfileThread() { }

    public ProfileThread(int threadId, int processId, string name) {
        ThreadId = threadId;
        ProcessId = processId;
        Name = name;
    }

    public bool Equals(ProfileThread other) {
        Debug.Assert(other != null);

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return ThreadId == other.ThreadId && ProcessId == other.ProcessId;
    }

    public override bool Equals(object other) {
        Debug.Assert(other != null);

        if (ReferenceEquals(this, other)) {
            return true;
        }

        if (other.GetType() != this.GetType()) {
            return false;
        }

        return Equals((ProfileThread)other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(ThreadId, ProcessId);
    }

    public static bool operator ==(ProfileThread left, ProfileThread right) {
        return Equals(left, right);
    }

    public static bool operator !=(ProfileThread left, ProfileThread right) {
        return !Equals(left, right);
    }

    public override string ToString() {
        return $"{ThreadId}, ProcessId: {ProcessId}, Name: {Name}";
    }
}

[ProtoContract(SkipConstructor = true)]
public struct PerformanceCounterEvent : IEquatable<PerformanceCounterEvent> {
    [ProtoMember(1)]
    public long IP { get; set; }
    [ProtoMember(2)]
    public TimeSpan Time { get; set; }
    [ProtoMember(3)]
    public int ContextId { get; set; }
    [ProtoMember(4)]
    public short CounterId;

    public PerformanceCounterEvent(long ip, TimeSpan time, int contextId, short counterId) {
        IP = ip;
        Time = time;
        ContextId = contextId;
        CounterId = counterId;
    }

    public ProfileContext GetContext(RawProfileData profileData) {
        return profileData.FindContext(ContextId);
    }

    public bool Equals(PerformanceCounterEvent other) {
        return Time == other.Time && IP == other.IP &&
               ContextId == other.ContextId &&
               CounterId == other.CounterId;
    }

    public override bool Equals(object obj) {
        return obj is PerformanceCounterEvent other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(Time, IP, ContextId, CounterId);
    }

    public override string ToString() {
        return $"PMC {CounterId}: {IP:X}, {Time}";
    }

    /// enum eventType (pmc, context switch, etc)
}

[ProtoContract(SkipConstructor = true)]
public class PerformanceCounter : IEquatable<PerformanceCounter> {
    [ProtoMember(1)]
    public int Id { get; set; }
    [ProtoMember(2)]
    public string Name { get; set; }
    [ProtoMember(3)]
    public int Frequency { get; set; }

    public PerformanceCounter(int id, string name, int frequency) {
        Id = id;
        Name = name;
        Frequency = frequency;
    }

    public bool Equals(PerformanceCounter other) {
        if (ReferenceEquals(null, other)) {
            return false;
        }

        if (ReferenceEquals(this, other)) {
            return true;
        }

        return Id == other.Id && Name == other.Name && Frequency == other.Frequency;
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

        return Equals((PerformanceCounter)obj);
    }

    public override int GetHashCode() {
        return HashCode.Combine(Id, Name, Frequency);
    }
}