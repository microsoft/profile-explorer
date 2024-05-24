// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.IO;
using ProtoBuf;

namespace IRExplorerUI.Profile;

[ProtoContract(SkipConstructor = true)]
public class ProfileRecordingSessionOptions : SettingsBase, IEquatable<ProfileRecordingSessionOptions> {
  public const int DefaultSamplingFrequency = 4000;
  public const int MaximumSamplingFrequency = 8000;

  public ProfileRecordingSessionOptions() {
    Reset();
  }

  [ProtoMember(1), OptionValue(ProfileSessionKind.StartProcess)]
  public ProfileSessionKind SessionKind { get; set; }
  [ProtoMember(2), OptionValue("")]
  public string ApplicationPath { get; set; }
  [ProtoMember(3), OptionValue("")]
  public string ApplicationArguments { get; set; }
  [ProtoMember(4), OptionValue("")]
  public string WorkingDirectory { get; set; }
  [ProtoMember(5), OptionValue(4000)] // 4 kHz, Xperf default is 1 kHz.
  public int SamplingFrequency { get; set; }
  [ProtoMember(6), OptionValue(false)]
  public bool ProfileDotNet { get; set; }
  [ProtoMember(7), OptionValue(false)]
  public bool ProfileChildProcesses { get; set; }
  [ProtoMember(8), OptionValue(false)]
  public bool RecordPerformanceCounters { get; set; }
  [ProtoMember(9), OptionValue(false)]
  public bool EnableEnvironmentVars { get; set; }
  [ProtoMember(10), OptionValue()]
  public List<(string Variable, string Value)> EnvironmentVariables { get; set; }
  [ProtoMember(11), OptionValue()]
  public List<PerformanceCounterConfig> PerformanceCounters { get; set; }
  [ProtoMember(12), OptionValue("")]
  public string Title { get; set; }
  [ProtoMember(13), OptionValue(0)]
  public int TargetProcessId { get; set; }
  [ProtoMember(14), OptionValue(false)]
  public bool RecordDotNetAssembly { get; set; }
  public List<PerformanceCounterConfig> EnabledPerformanceCounters => PerformanceCounters.FindAll(c => c.IsEnabled);
  public bool HasWorkingDirectory => Directory.Exists(WorkingDirectory);
  public bool HasTitle => !string.IsNullOrEmpty(Title);

  public static bool operator ==(ProfileRecordingSessionOptions left, ProfileRecordingSessionOptions right) {
    return Equals(left, right);
  }

  public static bool operator !=(ProfileRecordingSessionOptions left, ProfileRecordingSessionOptions right) {
    return !Equals(left, right);
  }

  public override void Reset() {
    ResetAllOptions(this);
  }

  public bool AddPerformanceCounter(PerformanceCounterConfig config) {
    if (!PerformanceCounters.Contains(config)) {
      PerformanceCounters.Add(config);
      return true;
    }

    return false;
  }

  public ProfileRecordingSessionOptions Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<ProfileRecordingSessionOptions>(serialized);
  }

  public bool Equals(ProfileRecordingSessionOptions other) {
    return AreOptionsEqual(this, other);
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