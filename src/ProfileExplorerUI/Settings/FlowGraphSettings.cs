// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows.Media;
using ProfileExplorer.Core.Settings;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class FlowGraphSettings : GraphSettings {
  public FlowGraphSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue("#F4F4F4")]
  public Color EmptyNodeColor { get; set; }
  [ProtoMember(2)][OptionValue("#0042B6")]
  public Color BranchNodeBorderColor { get; set; }
  [ProtoMember(3)][OptionValue("#8500BE")]
  public Color SwitchNodeBorderColor { get; set; }
  [ProtoMember(4)][OptionValue("#008D00")]
  public Color LoopNodeBorderColor { get; set; }
  [ProtoMember(5)][OptionValue("#B30606")]
  public Color ReturnNodeBorderColor { get; set; }
  [ProtoMember(6)][OptionValue(true)]
  public bool MarkLoopBlocks { get; set; }
  [ProtoMember(7, OverwriteList = true)][OptionValue(new string[] {
    "#FCD1A4", "#FFA56D", "#FF7554", "#FC5B5B"
  })]
  public Color[] LoopNodeColors { get; set; }
  [ProtoMember(8)][OptionValue(true)]
  public bool ShowImmDominatorEdges { get; set; }
  [ProtoMember(9)][OptionValue("#0042B6")]
  public Color DominatorEdgeColor { get; set; }

  public override void Reset() {
    base.Reset();
    ResetAllOptions(this);
  }

  public FlowGraphSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<FlowGraphSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is FlowGraphSettings settings &&
           base.Equals(settings) &&
           AreOptionsEqual(this, settings);
  }

  protected override GraphSettings MakeClone() {
    return Clone();
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}