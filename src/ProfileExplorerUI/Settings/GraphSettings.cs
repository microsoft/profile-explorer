// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows.Media;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
[ProtoInclude(100, typeof(FlowGraphSettings))]
[ProtoInclude(200, typeof(ExpressionGraphSettings))]
public class GraphSettings : SettingsBase {
  public GraphSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(true)]
  public bool SyncSelectedNodes { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool SyncMarkedNodes { get; set; }
  [ProtoMember(3)][OptionValue(true)]
  public bool BringNodesIntoView { get; set; }
  [ProtoMember(4)][OptionValue(true)]
  public bool ShowPreviewPopup { get; set; }
  [ProtoMember(5)][OptionValue(false)]
  public bool ShowPreviewPopupWithModifier { get; set; }
  [ProtoMember(6)][OptionValue(true)]
  public bool ColorizeNodes { get; set; }
  [ProtoMember(7)][OptionValue(true)]
  public bool ColorizeEdges { get; set; }
  [ProtoMember(8)][OptionValue(true)]
  public bool HighlightConnectedNodesOnHover { get; set; }
  [ProtoMember(9)][OptionValue(true)]
  public bool HighlightConnectedNodesOnSelection { get; set; }
  [ProtoMember(10)][OptionValue("#F0F0F0")]
  public Color BackgroundColor { get; set; }
  [ProtoMember(11)][OptionValue("#000000")]
  public Color TextColor { get; set; }
  [ProtoMember(12)][OptionValue("#CBCBCB")]
  public Color NodeColor { get; set; }
  [ProtoMember(13)][OptionValue("#000000")]
  public Color NodeBorderColor { get; set; }
  [ProtoMember(14)][OptionValue("#000000")]
  public Color EdgeColor { get; set; }
  [ProtoMember(15)][OptionValue("#6927CC")]
  public Color PredecessorNodeBorderColor { get; set; }
  [ProtoMember(16)][OptionValue("#008230")]
  public Color SuccessorNodeBorderColor { get; set; }
  [ProtoMember(17)][OptionValue("#AEDCF4")]
  public Color SelectedNodeColor { get; set; }

  public GraphSettings Clone() {
    return MakeClone();
  }

  public override void Reset() {
    ResetAllOptions(this, typeof(GraphSettings));
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj, typeof(GraphSettings));
  }

  protected virtual GraphSettings MakeClone() {
    throw new NotImplementedException();
  }

  public override string ToString() {
    return PrintOptions(this, typeof(GraphSettings));
  }
}