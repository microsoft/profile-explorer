// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProfileExplorerCore.Profile.CallTree;
using ProfileExplorerCore.Profile.Timeline;
using ProfileExplorerCore.Utilities;

namespace ProfileExplorerCore.Profile.Processing;

public class ProfileSampleFilter : IEquatable<ProfileSampleFilter> {
  public ProfileSampleFilter() {
  }

  public ProfileSampleFilter(ProfileCallTreeNode instance) {
    AddInstance(instance);
  }

  public ProfileSampleFilter(int threadId) {
    AddThread(threadId);
  }

  public SampleTimeRangeInfo TimeRange { get; set; }
  public List<int> ThreadIds { get; set; }
  public List<ProfileCallTreeNode> FunctionInstances { get; set; }
  public bool HasThreadFilter => ThreadIds is {Count: > 0};
  public bool HasInstanceFilter => FunctionInstances is {Count: > 0};
  public bool IncludesAll => TimeRange == null &&
                             !HasThreadFilter &&
                             !HasInstanceFilter;

  public bool Equals(ProfileSampleFilter other) {
    if (ReferenceEquals(null, other))
      return false;
    if (ReferenceEquals(this, other))
      return true;
    return Equals(TimeRange, other.TimeRange) &&
           ThreadIds.AreEqual(other.ThreadIds) &&
           FunctionInstances.AreEqual(other.FunctionInstances);
  }

  public ProfileSampleFilter AddInstance(ProfileCallTreeNode instance) {
    FunctionInstances ??= new List<ProfileCallTreeNode>();
    FunctionInstances.Add(instance);
    return this;
  }

  public bool IncludesInstance(ProfileCallTreeNode node) {
    return HasInstanceFilter && FunctionInstances.Contains(node);
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

  public bool IncludesThread(int threadId) {
    return HasThreadFilter && ThreadIds.Contains(threadId);
  }

  public void RemoveThread(int threadId) {
    ThreadIds?.Remove(threadId);
  }

  public void ClearThreads() {
    ThreadIds?.Clear();
  }

  public ProfileSampleFilter Clone() {
    var clone = new ProfileSampleFilter();
    clone.TimeRange = TimeRange;
    clone.ThreadIds = ThreadIds.CloneList();
    clone.FunctionInstances = FunctionInstances.CloneList();
    return clone;
  }

  public ProfileSampleFilter CloneForCallTarget(IRTextFunction targetFunc) {
    var targetFilter = Clone();

    if (HasInstanceFilter) {
      targetFilter.ClearInstances();

      foreach (var instance in FunctionInstances) {
        // Try to add the instance node that is a child
        // of the current instance in the profile filter.
        var targetInstance = instance.FindChildNode(targetFunc);

        if (targetInstance != null) {
          targetFilter.AddInstance(targetInstance);
        }
      }
    }

    return targetFilter;
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
    string text = $"TimeRange: {TimeRange}, HasInstanceFilter: {HasInstanceFilter}, HasThreadFilter: {HasThreadFilter}";

    if (HasInstanceFilter) {
      foreach (var item in FunctionInstances) {
        text += $"\n - instance: {item.FunctionName}";
      }
    }

    if (HasThreadFilter) {
      foreach (int item in ThreadIds) {
        text += $"\n - thread: {item}";
      }
    }

    return text;
  }
}

public class ProfileFilterState {
  public ProfileFilterState(ProfileSampleFilter filter = null) {
    Filter = filter ?? new ProfileSampleFilter();
  }

  public bool HasAnyFilter => HasThreadFilter || HasFilter;
  public ProfileSampleFilter Filter { get; set; }
  public bool HasFilter { get; set; }
  public TimeSpan FilteredTime { get; set; }
  public bool HasThreadFilter { get; set; }
  public string ThreadFilterText { get; set; }
  public Func<Task> RemoveThreadFilter { get; set; }
  public Func<Task> RemoveTimeRangeFilter { get; set; }
  public Func<Task> RemoveAllFilters { get; set; }
}