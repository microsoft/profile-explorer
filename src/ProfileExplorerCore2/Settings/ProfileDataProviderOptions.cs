// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using ProfileExplorerCore2.Profile.Data;
using ProfileExplorerCore2.Session;
using ProfileExplorerCore2.Utilities;
using ProtoBuf;

namespace ProfileExplorerCore2.Settings;

[ProtoContract(SkipConstructor = true)]
public class ProfileDataProviderOptions : SettingsBase {
  public ProfileDataProviderOptions() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(false)]
  public bool BinarySearchPathsEnabled { get; set; }
  [ProtoMember(2)][OptionValue(false)]
  public bool BinaryNameAllowedListEnabled { get; set; }
  [ProtoMember(3)][OptionValue(true)]
  public bool DownloadBinaryFiles { get; set; }
  [ProtoMember(4)][OptionValue()]
  public List<string> BinarySearchPaths { get; set; }
  [ProtoMember(5)][OptionValue()]
  public List<string> BinaryNameAllowedList { get; set; }
  [ProtoMember(6)][OptionValue(false)]
  public bool MarkInlinedFunctions { get; set; }
  [ProtoMember(7)][OptionValue(true)]
  public bool IncludeKernelEvents { get; set; }
  [ProtoMember(8)][OptionValue(true)]
  public bool IncludePerformanceCounters { get; set; }
  [ProtoMember(9)][OptionValue()]
  public ProfileRecordingSessionOptions RecordingSessionOptions { get; set; }
  [ProtoMember(10)][OptionValue()]
  public List<PerformanceMetricConfig> PerformanceMetrics { get; set; }
  [ProtoMember(11)][OptionValue()]
  public List<ProfileDataReport> PreviousRecordingSessions { get; set; }
  [ProtoMember(12)][OptionValue()]
  public List<ProfileDataReport> PreviousLoadedSessions { get; set; }
  public bool HasBinaryNameAllowedList => BinaryNameAllowedListEnabled && BinaryNameAllowedList.Count > 0;
  public bool HasBinarySearchPaths => BinarySearchPathsEnabled && BinarySearchPaths.Count > 0;

  public override void Reset() {
    ResetAllOptions(this);
  }

  public bool AddPerformanceMetric(PerformanceMetricConfig config) {
    if (!PerformanceMetrics.Contains(config)) {
      PerformanceMetrics.Add(config);
      return true;
    }

    return false;
  }

  public bool HasBinaryPath(string path) {
    path = Utils.TryGetDirectoryName(path).ToLowerInvariant();
    return BinarySearchPaths.Find(item => item.ToLowerInvariant() == path) != null;
  }

  public void InsertBinaryPath(string path) {
    if (string.IsNullOrEmpty(path) || HasBinaryPath(path)) {
      return;
    }

    path = Utils.TryGetDirectoryName(path);

    if (!string.IsNullOrEmpty(path)) {
      BinarySearchPaths.Insert(0, path);
    }
  }

  public ProfileDataProviderOptions Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<ProfileDataProviderOptions>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }
}