// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class DocumentSettings : TextViewSettingsBase {
  public DocumentSettings() {
    Reset();
  }

  [ProtoMember(3), OptionValue(true)] 
  public bool ShowBlockFolding { get; set; }
  [ProtoMember(4), OptionValue(true)] 
  public bool HighlightSourceDefinition { get; set; }
  [ProtoMember(5), OptionValue(true)] 
  public bool HighlightDestinationUses { get; set; }
  [ProtoMember(6), OptionValue(true)] 
  public bool HighlightInstructionOperands { get; set; }
  [ProtoMember(7), OptionValue(true)] 
  public bool ShowInfoOnHover { get; set; }
  [ProtoMember(8), OptionValue(false)] 
  public bool ShowInfoOnHoverWithModifier { get; set; }
  [ProtoMember(9), OptionValue(true)] 
  public bool ShowPreviewPopup { get; set; }
  [ProtoMember(13), OptionValue(typeof(Color), "#EAE4BB")] 
  public Color DefinitionValueColor { get; set; }
  [ProtoMember(14), OptionValue(typeof(Color), "#B7E5C6")] 
  public Color UseValueColor { get; set; }
  [ProtoMember(15), OptionValue(typeof(Color), "#000000")] 
  public Color BorderColor { get; set; }
  [ProtoMember(16), OptionValue("")] 
  public string SyntaxHighlightingName { get; set; }
  [ProtoMember(17), OptionValue(0)] 
  public int DefaultExpressionsLevel { get; set; }
  [ProtoMember(18), OptionValue(false)] 
  public bool MarkMultipleDefinitionExpressions { get; set; }
  [ProtoMember(19), OptionValue(true)] 
  public bool FilterSourceDefinitions { get; set; }
  [ProtoMember(20), OptionValue(true)] 
  public bool FilterDestinationUses { get; set; }
  [ProtoMember(21)] 
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
           AreSettingsOptionsEqual(this, settings);
  }

  public override string ToString() {
    return base.ToString() + PrintOptions(this);
  }
}