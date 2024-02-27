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

  [ProtoMember(2)] public bool HighlightCurrentLine { get; set; }
  [ProtoMember(3)] public bool ShowBlockFolding { get; set; }
  [ProtoMember(4)] public bool HighlightSourceDefinition { get; set; }
  [ProtoMember(5)] public bool HighlightDestinationUses { get; set; }
  [ProtoMember(6)] public bool HighlightInstructionOperands { get; set; }
  [ProtoMember(7)] public bool ShowInfoOnHover { get; set; }
  [ProtoMember(8)] public bool ShowInfoOnHoverWithModifier { get; set; }
  [ProtoMember(9)] public bool ShowPreviewPopup { get; set; }
  [ProtoMember(13)] public Color DefinitionValueColor { get; set; }
  [ProtoMember(14)] public Color UseValueColor { get; set; }
  [ProtoMember(15)] public Color BorderColor { get; set; }
  [ProtoMember(16)] public string SyntaxHighlightingName { get; set; }
  [ProtoMember(17)] public int DefaultExpressionsLevel { get; set; }
  [ProtoMember(18)] public bool MarkMultipleDefinitionExpressions { get; set; }
  [ProtoMember(19)] public bool FilterSourceDefinitions { get; set; }
  [ProtoMember(20)] public bool FilterDestinationUses { get; set; }
  [ProtoMember(21)] public SourceDocumentMarkerSettings SourceMarkerSettings { get; set; }

  public override void Reset() {
    base.Reset();
    InitializeReferenceMembers();
    HighlightCurrentLine = true;
    ShowBlockFolding = true;
    HighlightSourceDefinition = true;
    HighlightDestinationUses = true;
    HighlightInstructionOperands = true;
    ShowInfoOnHover = true;
    ShowInfoOnHoverWithModifier = false;
    ShowPreviewPopup = true;
    FilterSourceDefinitions = true;
    FilterDestinationUses = true;
    DefinitionValueColor = Utils.ColorFromString("#EAE4BB");
    UseValueColor = Utils.ColorFromString("#B7E5C6");
    BorderColor = Colors.Black;
    SyntaxHighlightingName = "";
    SourceMarkerSettings.Reset();
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    SourceMarkerSettings ??= new SourceDocumentMarkerSettings();
  }

  public DocumentSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<DocumentSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is DocumentSettings settings &&
           base.Equals(settings) &&
           HighlightCurrentLine == settings.HighlightCurrentLine &&
           ShowBlockFolding == settings.ShowBlockFolding &&
           HighlightSourceDefinition == settings.HighlightSourceDefinition &&
           HighlightDestinationUses == settings.HighlightDestinationUses &&
           HighlightInstructionOperands == settings.HighlightInstructionOperands &&
           ShowInfoOnHover == settings.ShowInfoOnHover &&
           ShowInfoOnHoverWithModifier == settings.ShowInfoOnHoverWithModifier &&
           ShowPreviewPopup == settings.ShowPreviewPopup &&
           FilterSourceDefinitions == settings.FilterSourceDefinitions &&
           FilterDestinationUses == settings.FilterDestinationUses &&
           SelectedValueColor.Equals(settings.SelectedValueColor) &&
           DefinitionValueColor.Equals(settings.DefinitionValueColor) &&
           UseValueColor.Equals(settings.UseValueColor) &&
           BorderColor.Equals(settings.BorderColor) &&
           SyntaxHighlightingName == settings.SyntaxHighlightingName &&
           DefaultExpressionsLevel == settings.DefaultExpressionsLevel &&
           MarkMultipleDefinitionExpressions == settings.MarkMultipleDefinitionExpressions &&
           SourceMarkerSettings.Equals(settings.SourceMarkerSettings);
  }

  public override string ToString() {
    return base.ToString() +
           $"HighlightCurrentLine: {HighlightCurrentLine}\n" +
           $"ShowBlockFolding: {ShowBlockFolding}\n" +
           $"HighlightSourceDefinition: {HighlightSourceDefinition}\n" +
           $"HighlightDestinationUses: {HighlightDestinationUses}\n" +
           $"HighlightInstructionOperands: {HighlightInstructionOperands}\n" +
           $"ShowInfoOnHover: {ShowInfoOnHover}\n" +
           $"ShowInfoOnHoverWithModifier: {ShowInfoOnHoverWithModifier}\n" +
           $"ShowPreviewPopup: {ShowPreviewPopup}\n" +
           $"FilterSourceDefinitions: {FilterSourceDefinitions}\n" +
           $"FilterDestinationUses: {FilterDestinationUses}\n" +
           $"DefinitionValueColor: {DefinitionValueColor}\n" +
           $"UseValueColor: {UseValueColor}\n" +
           $"BorderColor: {BorderColor}\n" +
           $"SyntaxHighlightingName: {SyntaxHighlightingName}\n" +
           $"DefaultExpressionsLevel: {DefaultExpressionsLevel}\n" +
           $"MarkMultipleDefinitionExpressions: {MarkMultipleDefinitionExpressions}\n" +
           $"SourceMarkerSettings: {SourceMarkerSettings}";
  }
}