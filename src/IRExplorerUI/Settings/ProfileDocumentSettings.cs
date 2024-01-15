// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows.Media;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class SourceDocumentMarkerSettings : SettingsBase {
  public SourceDocumentMarkerSettings() {
    Reset();
  }

  [ProtoMember(1)]
  public bool AnnotateSourceLines { get; set; }
  [ProtoMember(2)]
  public bool AnnotateInlinees { get; set; }
  [ProtoMember(3)]
  public double VirtualColumnPosition { get; set; }
  [ProtoMember(4)]
  public Color ElementOverlayTextColor { get; set; }
  [ProtoMember(5)]
  public Color ElementOverlayBackColor { get; set; }
  [ProtoMember(6)]
  public Color InlineeOverlayTextColor { get; set; }
  [ProtoMember(7)]
  public Color InlineeOverlayBackColor { get; set; }

  public override void Reset() {
    AnnotateSourceLines = true;
    AnnotateInlinees = true;
    VirtualColumnPosition = 0.5;
    ElementOverlayTextColor = Colors.DimGray;
    ElementOverlayBackColor = Colors.Transparent;
    InlineeOverlayTextColor = Colors.Green;
    InlineeOverlayBackColor = Colors.Transparent;
  }

  public SourceDocumentMarkerSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SourceDocumentMarkerSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is SourceDocumentMarkerSettings settings &&
            AnnotateSourceLines == settings.AnnotateSourceLines &&
            AnnotateInlinees == settings.AnnotateInlinees &&
            VirtualColumnPosition == settings.VirtualColumnPosition &&
            ElementOverlayTextColor == settings.ElementOverlayTextColor &&
            ElementOverlayBackColor == settings.ElementOverlayBackColor &&
            InlineeOverlayTextColor == settings.InlineeOverlayTextColor &&
            InlineeOverlayBackColor == settings.InlineeOverlayBackColor;
  }
}