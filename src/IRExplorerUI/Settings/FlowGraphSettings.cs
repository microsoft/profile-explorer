// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class FlowGraphSettings : GraphSettings {
  public FlowGraphSettings() {
    Reset();
  }

  [ProtoMember(1), OptionValue(typeof(Color), "#F4F4F4")]
  public Color EmptyNodeColor { get; set; }
  [ProtoMember(2), OptionValue(typeof(Color), "#0042B6")]
  public Color BranchNodeBorderColor { get; set; }
  [ProtoMember(3), OptionValue(typeof(Color), "#8500BE")]
  public Color SwitchNodeBorderColor { get; set; }
  [ProtoMember(4), OptionValue(typeof(Color), "#008D00")]
  public Color LoopNodeBorderColor { get; set; }
  [ProtoMember(5), OptionValue(typeof(Color), "#B30606")]
  public Color ReturnNodeBorderColor { get; set; }
  [ProtoMember(6), OptionValue(true)]
  public bool MarkLoopBlocks { get; set; }
  [ProtoMember(7, OverwriteList = true), OptionValue(typeof(Color), 
     "#FCD1A4", "#FFA56D", "#FF7554", "#FC5B5B")]
  public Color[] LoopNodeColors { get; set; }
  [ProtoMember(8), OptionValue(true)]
  public bool ShowImmDominatorEdges { get; set; }
  [ProtoMember(9), OptionValue(typeof(Color), "#0042B6")]
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
    return AreOptionsEqual(this, obj);
    
  }

  protected override GraphSettings MakeClone() {
    return Clone();
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}
