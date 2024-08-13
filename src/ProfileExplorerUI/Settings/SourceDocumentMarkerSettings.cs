// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows.Media;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class SourceDocumentMarkerSettings : SettingsBase {
  public SourceDocumentMarkerSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(true)]
  public bool AnnotateSourceLines { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool AnnotateInlinees { get; set; }
  [ProtoMember(3)][OptionValue(0.5)]
  public double VirtualColumnPosition { get; set; }
  [ProtoMember(4)][OptionValue("#696969")]
  public Color SourceLineTextColor { get; set; }
  [ProtoMember(5)][OptionValue("#00FFFFFF")]
  public Color SourceLineBackColor { get; set; }
  [ProtoMember(6)][OptionValue("#008000")]
  public Color InlineeOverlayTextColor { get; set; }
  [ProtoMember(7)][OptionValue("#00FFFFFF")]
  public Color InlineeOverlayBackColor { get; set; }
  [ProtoMember(8)][OptionValue(true)]
  public bool MarkCallTargets { get; set; }
  [ProtoMember(9)][OptionValue("#00008B")]
  public Color CallTargetTextColor { get; set; }
  [ProtoMember(10)][OptionValue("#00FFFFFF")]
  public Color CallTargetBackColor { get; set; }

  public override void Reset() {
    ResetAllOptions(this);
  }

  public SourceDocumentMarkerSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SourceDocumentMarkerSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}