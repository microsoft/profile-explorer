// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.IO;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class SourceFileSettings : TextViewSettingsBase {
  public SourceFileSettings() {
    Reset();
  }

  [ProtoMember(1)]
  public SourceFileFinderSettings FinderSettings { get; set; }
  [ProtoMember(2)]
  public bool SyncStyleWithDocument { get; set; }
  [ProtoMember(3)]
  public bool SyncLineWithDocument { get; set; }
  [ProtoMember(4)]
  public bool SyncInlineeWithDocument { get; set; }

  public override void Reset() {
    base.Reset();
    InitializeReferenceMembers();
    ProfileMarkerSettings.JumpToHottestElement = true;
    SyncStyleWithDocument = true;
    SyncLineWithDocument = true;
    FinderSettings.Reset();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    FinderSettings ??= new();
  }

  public SourceFileSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SourceFileSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is SourceFileSettings settings &&
           base.Equals(settings) &&
           SyncStyleWithDocument == settings.SyncStyleWithDocument &&
           SyncLineWithDocument == settings.SyncLineWithDocument &&
           SyncInlineeWithDocument == settings.SyncInlineeWithDocument &&
           FinderSettings.Equals(settings.FinderSettings);
  }
}

[ProtoContract(SkipConstructor = true)]
public class SourceFileFinderSettings : SettingsBase {
  [ProtoMember(1)]
  public Dictionary<string, string> SourceMappings;
  [ProtoMember(2)]
  public List<string> DisabledSourceMappings;

  public SourceFileFinderSettings() {
    Reset();
  }

  public override void Reset() {
    InitializeReferenceMembers();
    SourceMappings.Clear();
    DisabledSourceMappings.Clear();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    SourceMappings ??= new();
    DisabledSourceMappings ??= new();
  }
  protected bool Equals(SourceFileFinderSettings other) {
    return SourceMappings.AreEqual(other.SourceMappings) &&
           DisabledSourceMappings.AreEqual(other.DisabledSourceMappings);
  }

  public override bool Equals(object obj) {
    if (ReferenceEquals(null, obj))
      return false;
    if (ReferenceEquals(this, obj))
      return true;
    if (obj.GetType() != this.GetType())
      return false;
    return Equals((SourceFileFinderSettings)obj);
  }

  public override string ToString() {
    return $"SourceMappings: {SourceMappings.Count}\n" +
           $"DisabledSourceMappings: {DisabledSourceMappings.Count}";
  }
}