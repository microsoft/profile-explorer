// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class DocumentColors {
        public DocumentColors() {
            switch (App.Theme.Kind) {
                case ApplicationThemeKind.Dark: {
                    //? TODO: Set default dark theme colors
                    BackgroundColor = Utils.ColorFromString("#FFFAFA");
                    AlternateBackgroundColor = Utils.ColorFromString("#f5f5f5");
                    TextColor = Colors.Black;
                    BlockSeparatorColor = Colors.Silver;
                    MarginBackgroundColor = Colors.Gainsboro;
                    LineNumberTextColor = Colors.Silver;
                    SearchResultColor = Colors.Khaki;
                    SelectedValueColor = Utils.ColorFromString("#C5DEEA");
                    DefinitionValueColor = Utils.ColorFromString("#F2E67C");
                    UseValueColor = Utils.ColorFromString("#95DBAD");
                    BorderColor = Colors.Black;
                    DefinitionMarkerColor = Utils.ColorFromString("#ffc5a3");
                    break;
                }
                default: {
                    BackgroundColor = Utils.ColorFromString("#FFFAFA");
                    AlternateBackgroundColor = Utils.ColorFromString("#f5f5f5");
                    TextColor = Colors.Black;
                    BlockSeparatorColor = Colors.Silver;
                    MarginBackgroundColor = Colors.Gainsboro;
                    LineNumberTextColor = Colors.Silver;
                    SearchResultColor = Colors.Khaki;
                    SelectedValueColor = Utils.ColorFromString("#C5DEEA");
                    DefinitionValueColor = Utils.ColorFromString("#F2E67C");
                    UseValueColor = Utils.ColorFromString("#95DBAD");
                    BorderColor = Colors.Black;
                    DefinitionMarkerColor = Utils.ColorFromString("#ffc5a3");
                    break;
                }
            }
        }

        [ProtoMember(1)] public Color BackgroundColor { get; set; }
        [ProtoMember(2)] public Color AlternateBackgroundColor { get; set; }
        [ProtoMember(3)] public Color MarginBackgroundColor { get; set; }
        [ProtoMember(4)] public Color TextColor { get; set; }
        [ProtoMember(5)] public Color BlockSeparatorColor { get; set; }
        [ProtoMember(6)] public Color SelectedValueColor { get; set; }
        [ProtoMember(7)] public Color DefinitionValueColor { get; set; }
        [ProtoMember(8)] public Color UseValueColor { get; set; }
        [ProtoMember(9)] public Color BorderColor { get; set; }
        [ProtoMember(10)] public Color LineNumberTextColor { get; set; }
        [ProtoMember(11)] public Color SearchResultColor { get; set; }
        [ProtoMember(12)] public Color DefinitionMarkerColor { get; set; }

        public override bool Equals(object obj) {
            return obj is DocumentColors settings &&
                   BackgroundColor.Equals(settings.BackgroundColor) &&
                   AlternateBackgroundColor.Equals(settings.AlternateBackgroundColor) &&
                   MarginBackgroundColor.Equals(settings.MarginBackgroundColor) &&
                   TextColor.Equals(settings.TextColor) &&
                   BlockSeparatorColor.Equals(settings.BlockSeparatorColor) &&
                   SelectedValueColor.Equals(settings.SelectedValueColor) &&
                   DefinitionValueColor.Equals(settings.DefinitionValueColor) &&
                   UseValueColor.Equals(settings.UseValueColor) &&
                   BorderColor.Equals(settings.BorderColor) &&
                   LineNumberTextColor.Equals(settings.LineNumberTextColor) &&
                   SearchResultColor.Equals(settings.SearchResultColor) &&
                   DefinitionMarkerColor.Equals(settings.DefinitionMarkerColor);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class DocumentSettings : SettingsBase, INotifyPropertyChanged {
        static readonly Guid Id = new Guid("F83ECF43-E4F5-4662-B6E6-C8A11EC5A437");
        private ThemeColorSet theme_;
        
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

        public Color BackgroundColor {
            get => currentThemeColors_.BackgroundColor;
            set => currentThemeColors_.BackgroundColor = value;
        }

        public Color AlternateBackgroundColor {
            get => currentThemeColors_.AlternateBackgroundColor;
            set => currentThemeColors_.AlternateBackgroundColor = value;
        }

        public Color MarginBackgroundColor {
            get => currentThemeColors_.MarginBackgroundColor;
            set => currentThemeColors_.MarginBackgroundColor = value;
        }

        public Color TextColor {
            get => currentThemeColors_.TextColor;
            set => currentThemeColors_.TextColor = value;
        }

        public Color BlockSeparatorColor {
            get => currentThemeColors_.BlockSeparatorColor;
            set => currentThemeColors_.BlockSeparatorColor = value;
        }

        public Color SelectedValueColor {
            get => currentThemeColors_.SelectedValueColor;
            set => currentThemeColors_.SelectedValueColor = value;
        }

        public Color DefinitionValueColor {
            get => currentThemeColors_.DefinitionValueColor;
            set => currentThemeColors_.DefinitionValueColor = value;
        }

        public Color UseValueColor {
            get => currentThemeColors_.UseValueColor;
            set => currentThemeColors_.UseValueColor = value;
        }

        public Color BorderColor {
            get => currentThemeColors_.BorderColor;
            set => currentThemeColors_.BorderColor = value;
        }
        public Color LineNumberTextColor {
            get => currentThemeColors_.LineNumberTextColor;
            set => currentThemeColors_.LineNumberTextColor = value;
        }
        public Color SearchResultColor {
            get => currentThemeColors_.SearchResultColor;
            set => currentThemeColors_.SearchResultColor = value;
        }

        public Color DefinitionMarkerColor {
            get => currentThemeColors_.DefinitionMarkerColor;
            set => currentThemeColors_.DefinitionMarkerColor = value;
        }

        [ProtoMember(13)] public string SyntaxHighlightingName { get; set; }
        [ProtoMember(14)] public int DefaultExpressionsLevel { get; set; }
        [ProtoMember(15)] public bool MarkMultipleDefinitionExpressions { get; set; }
        [ProtoMember(16)] public bool FilterSourceDefinitions { get; set; }
        [ProtoMember(17)] public bool FilterDestinationUses { get; set; }

        [ProtoMember(18)]
        private Dictionary<ApplicationThemeKind, DocumentColors> themeColors_;
        private DocumentColors currentThemeColors_;

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
                   SyntaxHighlightingName == settings.SyntaxHighlightingName &&
                   Utils.AreEqual(themeColors_, settings.themeColors_);
        }
    }
}
