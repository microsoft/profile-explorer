// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
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
  [ProtoMember(4)]
  public bool SyncWithDocument { get; set; }
  [ProtoMember(5)] public string FontName { get; set; }
  [ProtoMember(6)] public double FontSize { get; set; }
  [ProtoMember(7)] public Color BackgroundColor { get; set; }
  [ProtoMember(8)] public Color MarginBackgroundColor { get; set; }
  [ProtoMember(9)] public Color TextColor { get; set; }

  //? TODO: Options for
  //? - font, font size
  //? - syntax highlighting
  //? - other options from DocumentSettings

  public override void Reset() {
    InitializeReferenceMembers();
    SyncWithDocument = true;
    FontName = "Consolas";
    FontSize = 12;
    BackgroundColor = Utils.ColorFromString("#FFFAFA");
    TextColor = Colors.Black;
    MarginBackgroundColor = Colors.Gainsboro;
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
           SyncWithDocument == settings.SyncWithDocument &&
           FontName == settings.FontName &&
           Math.Abs(FontSize - settings.FontSize) < double.Epsilon &&
           BackgroundColor == settings.BackgroundColor &&
           MarginBackgroundColor == settings.MarginBackgroundColor &&
           TextColor == settings.TextColor &&
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
}