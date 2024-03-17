using System;
using System.Collections.Generic;

namespace IRExplorerUI.Profile;

public class ProfileSampleFilter : IEquatable<ProfileSampleFilter> {
  public SampleTimeRangeInfo TimeRange { get; set; }
  public List<int> ThreadIds { get; set; }
  public List<ProfileCallTreeNode> FunctionInstances { get; set; }

  public bool HasThreadFilter => ThreadIds is {Count: > 0};
  public bool HasInstanceFilter => FunctionInstances is {Count: > 0};
  public bool IncludesAll => TimeRange == null &&
                             !HasThreadFilter &&
                             !HasInstanceFilter;

  public ProfileSampleFilter() {

  }

  public ProfileSampleFilter(ProfileCallTreeNode instance) {
    AddInstance(instance);
  }

  public ProfileSampleFilter(int threadId) {
    AddThread(threadId);
  }

  public ProfileSampleFilter AddInstance(ProfileCallTreeNode instance) {
    FunctionInstances ??= new List<ProfileCallTreeNode>();
    FunctionInstances.Add(instance);
    return this;
  }

  public void RemoveInstance(ProfileCallTreeNode instance) {
    FunctionInstances?.Remove(instance);
  }

  public void ClearInstances() {
    FunctionInstances?.Clear();
  }

  public ProfileSampleFilter AddThread(int threadId) {
    ThreadIds ??= new List<int>();
    ThreadIds.Add(threadId);
    return this;
  }

  public void RemoveThread(int threadId) {
    ThreadIds?.Remove(threadId);
  }

  public void ClearThreads() {
    ThreadIds?.Clear();
  }

  public static bool operator ==(ProfileSampleFilter left, ProfileSampleFilter right) {
    return Equals(left, right);
  }

  public static bool operator !=(ProfileSampleFilter left, ProfileSampleFilter right) {
    return !Equals(left, right);
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj))
      return false;
    if (ReferenceEquals(this, obj))
      return true;
    if (obj.GetType() != GetType())
      return false;
    return Equals((ProfileSampleFilter)obj);
  }

  public override int GetHashCode() {
    return HashCode.Combine(TimeRange, ThreadIds, FunctionInstances);
  }

  public override string ToString() {
    return $"TimeRange: {TimeRange}, FunctionInstance: {FunctionInstances}, ThreadIds: {ThreadIds}";
  }

  public bool Equals(ProfileSampleFilter other) {
    if (ReferenceEquals(null, other))
      return false;
    if (ReferenceEquals(this, other))
      return true;
    return Equals(TimeRange, other.TimeRange) && Equals(ThreadIds, other.ThreadIds);
  }
}