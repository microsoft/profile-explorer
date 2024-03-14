using System;
using System.Collections.Generic;

namespace IRExplorerUI.Profile;

public class ProfileSampleFilter : IEquatable<ProfileSampleFilter> {
  public SampleTimeRangeInfo TimeRange { get; set; }
  public List<int> ThreadIds { get; set; }
  public List<ProfileCallTreeNode> FunctionInstances { get; set; }
  public bool IncludesAll => TimeRange == null &&
                             (FunctionInstances == null || FunctionInstances.Count == 0) &&
                             (ThreadIds == null || ThreadIds.Count == 0);

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
