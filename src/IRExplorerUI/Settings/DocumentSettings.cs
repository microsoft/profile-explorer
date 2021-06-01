// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class DocumentSettings : SettingsBase, INotifyPropertyChanged {
        public DocumentSettings() {
            Reset();
        }

        [ProtoMember(1)] public bool ShowBlockSeparatorLine { get; set; }

        [ProtoMember(2)] public string FontName { get; set; }

        [ProtoMember(3)] public double FontSize { get; set; }

        [ProtoMember(4)] public bool HighlightCurrentLine { get; set; }

        [ProtoMember(5)] public bool ShowBlockFolding { get; set; }

        [ProtoMember(6)] public bool HighlightSourceDefinition { get; set; }

        [ProtoMember(7)] public bool HighlightDestinationUses { get; set; }

        [ProtoMember(8)] public bool HighlightInstructionOperands { get; set; }

        [ProtoMember(9)] public bool ShowInfoOnHover { get; set; }

        [ProtoMember(10)] public bool ShowInfoOnHoverWithModifier { get; set; }

        [ProtoMember(11)] public bool ShowPreviewPopup { get; set; }

        [ProtoMember(12)] public bool ShowPreviewPopupWithModifier { get; set; }

        [ProtoMember(13)] public Color BackgroundColor { get; set; }

        [ProtoMember(14)] public Color AlternateBackgroundColor { get; set; }

        [ProtoMember(15)] public Color MarginBackgroundColor { get; set; }

        [ProtoMember(16)] public Color TextColor { get; set; }

        [ProtoMember(17)] public Color BlockSeparatorColor { get; set; }

        [ProtoMember(18)] public Color SelectedValueColor { get; set; }

        [ProtoMember(19)] public Color DefinitionValueColor { get; set; }

        [ProtoMember(20)] public Color UseValueColor { get; set; }

        [ProtoMember(21)] public Color BorderColor { get; set; }

        [ProtoMember(23)] public string SyntaxHighlightingName { get; set; }

        [ProtoMember(24)] public int DefaultExpressionsLevel { get; set; }
        [ProtoMember(25)] public bool MarkMultipleDefinitionExpressions { get; set; }
        [ProtoMember(26)] public bool FilterSourceDefinitions { get; set; }
        [ProtoMember(27)] public bool FilterDestinationUses { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public override void Reset() {
            ShowBlockSeparatorLine = true;
            FontName = "Consolas";
            FontSize = 12;
            HighlightCurrentLine = true;
            ShowBlockFolding = true;
            HighlightSourceDefinition = true;
            HighlightDestinationUses = true;
            HighlightInstructionOperands = true;
            ShowInfoOnHover = true;
            ShowInfoOnHoverWithModifier = true;
            ShowPreviewPopup = true;
            ShowPreviewPopupWithModifier = false;
            FilterSourceDefinitions = true;
            FilterDestinationUses = true;
            BackgroundColor = Utils.ColorFromString("#FFFAFA");
            AlternateBackgroundColor = Utils.ColorFromString("#f5f5f5");
            TextColor = Colors.Black;
            BlockSeparatorColor = Colors.Silver;
            MarginBackgroundColor = Colors.Gainsboro;
            SelectedValueColor = Utils.ColorFromString("#C5DEEA");
            DefinitionValueColor = Utils.ColorFromString("#EDE4A6");
            UseValueColor = Utils.ColorFromString("#B7E5C6");
            BorderColor = Colors.Black;
            SyntaxHighlightingName = "";
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<DocumentSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is DocumentSettings settings &&
                   ShowBlockSeparatorLine == settings.ShowBlockSeparatorLine &&
                   FontName == settings.FontName &&
                   Math.Abs(FontSize - settings.FontSize) < double.Epsilon &&
                   HighlightCurrentLine == settings.HighlightCurrentLine &&
                   ShowBlockFolding == settings.ShowBlockFolding &&
                   HighlightSourceDefinition == settings.HighlightSourceDefinition &&
                   HighlightDestinationUses == settings.HighlightDestinationUses &&
                   HighlightInstructionOperands == settings.HighlightInstructionOperands &&
                   ShowInfoOnHover == settings.ShowInfoOnHover &&
                   ShowInfoOnHoverWithModifier == settings.ShowInfoOnHoverWithModifier &&
                   ShowPreviewPopup == settings.ShowPreviewPopup &&
                   ShowPreviewPopupWithModifier == settings.ShowPreviewPopupWithModifier &&
                   FilterSourceDefinitions == settings.FilterSourceDefinitions &&
                   FilterDestinationUses == settings.FilterDestinationUses &&
                   BackgroundColor.Equals(settings.BackgroundColor) &&
                   AlternateBackgroundColor.Equals(settings.AlternateBackgroundColor) &&
                   MarginBackgroundColor.Equals(settings.MarginBackgroundColor) &&
                   TextColor.Equals(settings.TextColor) &&
                   BlockSeparatorColor.Equals(settings.BlockSeparatorColor) &&
                   SelectedValueColor.Equals(settings.SelectedValueColor) &&
                   DefinitionValueColor.Equals(settings.DefinitionValueColor) &&
                   UseValueColor.Equals(settings.UseValueColor) &&
                   BorderColor.Equals(settings.BorderColor) &&
                   SyntaxHighlightingName == settings.SyntaxHighlightingName;
        }
    }
}
