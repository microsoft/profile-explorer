// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Settings;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class CallTreeSettings : SettingsBase {
  public static readonly int DefaultNodePopupDuration = (int)HoverPreview.HoverDurationMs;

  public CallTreeSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(true)]
  public bool CombineInstances { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool PrependModuleToFunction { get; set; }
  [ProtoMember(3)][OptionValue(true)]
  public bool ShowTimeAfterPercentage { get; set; }
  [ProtoMember(4)][OptionValue(false)]
  public bool ShowDetailsPanel { get; set; }
  [ProtoMember(5)][OptionValue(false)]
  public bool SyncSourceFile { get; set; }
  [ProtoMember(6)][OptionValue(true)]
  public bool SyncSelection { get; set; }
  [ProtoMember(7)][OptionValue(true)]
  public bool ShowNodePopup { get; set; }
  [ProtoMember(8)][OptionValue(0)]
  public int NodePopupDuration { get; set; }
  [ProtoMember(9)][OptionValue()]
  public ColumnSettings TreeListColumns { get; set; }
  [ProtoMember(10)][OptionValue(true)]
  public bool ExpandHottestPath { get; set; }

  public override void Reset() {
    ResetAllOptions(this);
    NodePopupDuration = DefaultNodePopupDuration;
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public CallTreeSettings Clone() {
    byte[] serialized = UIStateSerializer.Serialize(this);
    return UIStateSerializer.Deserialize<CallTreeSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}