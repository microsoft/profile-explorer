// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.IO;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class SourceFileSettings : SettingsBase {
  public SourceFileSettings() {
    Reset();
  }

  [ProtoMember(1)]
  public SourceFileFinderSettings FinderSettings { get; set; }
  [ProtoMember(2)]
  public ProfileDocumentMarkerSettings ProfileMarkerSettings { get; set; }
  [ProtoMember(3)]
  public OptionalColumnSettings ColumnSettings { get; set; }

  //? TODO: Options for
  //? - font, font size
  //? - syntax highlighting
  //? - other options from DocumentSettings

  public override void Reset() {
    InitializeReferenceMembers();
    FinderSettings.Reset();
    ProfileMarkerSettings.Reset();
    ColumnSettings.Reset();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    FinderSettings ??= new();
    ProfileMarkerSettings ??= new();
    ColumnSettings ??= new();
  }

  public SourceFileSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SourceFileSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is SourceFileSettings settings &&
           FinderSettings.Equals(settings.FinderSettings) &&
           ProfileMarkerSettings.Equals(settings.ProfileMarkerSettings) &&
           ColumnSettings.Equals(settings.ColumnSettings);
  }
}

[ProtoContract(SkipConstructor = true)]
public class SourceFileFinderSettings : SettingsBase {
  [ProtoMember(1)]
  public Dictionary<string, string> SourceMappings;
  [ProtoMember(2)]
  public HashSet<string> DisabledSourceMappings;

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
}