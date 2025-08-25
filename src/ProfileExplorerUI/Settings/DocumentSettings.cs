// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows.Media;
using ProfileExplorerCore2.Settings;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class DocumentSettings : TextViewSettingsBase {
  public DocumentSettings() {
    Reset();
  }

  [ProtoMember(3)][OptionValue(true)]
  public bool ShowBlockFolding { get; set; }
  [ProtoMember(4)][OptionValue(true)]
  public bool HighlightSourceDefinition { get; set; }
  [ProtoMember(5)][OptionValue(true)]
  public bool HighlightDestinationUses { get; set; }
  [ProtoMember(6)][OptionValue(false)]
  public bool HighlightInstructionOperands { get; set; }
  [ProtoMember(7)][OptionValue(true)]
  public bool ShowInfoOnHover { get; set; }
  [ProtoMember(8)][OptionValue(false)]
  public bool ShowInfoOnHoverWithModifier { get; set; }
  [ProtoMember(9)][OptionValue(true)]
  public bool ShowPreviewPopup { get; set; }
  [ProtoMember(13)][OptionValue("#EAE4BB")]
  public Color DefinitionValueColor { get; set; }
  [ProtoMember(14)][OptionValue("#B7E5C6")]
  public Color UseValueColor { get; set; }
  [ProtoMember(15)][OptionValue("#000000")]
  public Color BorderColor { get; set; }
  [ProtoMember(16)][OptionValue("")]
  public string SyntaxHighlightingName { get; set; }
  [ProtoMember(17)][OptionValue(0)]
  public int DefaultExpressionsLevel { get; set; }
  [ProtoMember(18)][OptionValue(false)]
  public bool MarkMultipleDefinitionExpressions { get; set; }
  [ProtoMember(19)][OptionValue(true)]
  public bool FilterSourceDefinitions { get; set; }
  [ProtoMember(20)][OptionValue(true)]
  public bool FilterDestinationUses { get; set; }
  [ProtoMember(21)][OptionValue()]
  public SourceDocumentMarkerSettings SourceMarkerSettings { get; set; }

  public override void Reset() {
    base.Reset();
    InitializeReferenceMembers();
    ResetAllOptions(this);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public DocumentSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<DocumentSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is DocumentSettings settings &&
           base.Equals(settings) &&
           AreOptionsEqual(this, settings);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}