// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class CallTreeSettings : SettingsBase {
  public static readonly int DefaultNodePopupDuration = (int)HoverPreview.HoverDuration.TotalMilliseconds;

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
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<CallTreeSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}