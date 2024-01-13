// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class ProfileDocumentSettings : SettingsBase {
  public ProfileDocumentSettings() {
    Reset();
  }

  //? TODO:

  [ProtoMember(1)] public bool MarkElements { get; set; }
  [ProtoMember(1)] public bool MarkBlocks { get; set; }
  [ProtoMember(1)] public bool MarkBlocksInFlowGraph { get; set; }
  [ProtoMember(1)] public bool MarkCallTargets { get; set; }
  [ProtoMember(1)] public bool JumpToHottestElement { get; set; }
  public OptionalColumnSettings ColumnSettings { get; set; }
  public ProfileDocumentMarkerSettings MarkerSettings { get; set; }

  public override void Reset() {
    InitializeReferenceMembers();

    MarkElements = true;
    MarkBlocks = true;
    MarkBlocksInFlowGraph = true;
    MarkCallTargets = true;
    ColumnSettings.Reset();
    MarkerSettings.Reset();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    ColumnSettings ??= new OptionalColumnSettings();
    MarkerSettings ??= new ProfileDocumentMarkerSettings();
  }

  public ProfileDocumentSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<ProfileDocumentSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is ProfileDocumentSettings settings &&
           MarkElements == settings.MarkElements &&
           MarkBlocks == settings.MarkBlocks &&
           MarkBlocksInFlowGraph == settings.MarkBlocksInFlowGraph &&
           MarkCallTargets == settings.MarkCallTargets &&
           JumpToHottestElement == settings.JumpToHottestElement &&
           ColumnSettings.Equals(settings.ColumnSettings);
  }
}