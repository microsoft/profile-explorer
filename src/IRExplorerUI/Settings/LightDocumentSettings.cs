// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class LightDocumentColors {
        public LightDocumentColors() {
            switch (App.Theme.Kind) {
                case ApplicationThemeKind.Dark: {
                    //? TODO: Set default dark theme colors
                    BackgroundColor = Utils.ColorFromString("#FFFAFA");
                    TextColor = Colors.Black;
                    HighlightedIRElementColor = Utils.ColorFromString("#FFFCDC");
                    HoveredIRElementColor = Utils.ColorFromString("#FFFCDC");
                    LineNumberTextColor = Colors.Silver;
                    SearchResultColor = Colors.Khaki;
                    break;
                }
                default: {
                    BackgroundColor = Utils.ColorFromString("#FFFAFA");
                    TextColor = Colors.Black;
                    HighlightedIRElementColor = Utils.ColorFromString("#FFFCDC");
                    HoveredIRElementColor = Utils.ColorFromString("#FFFCDC");
                    LineNumberTextColor = Colors.Silver;
                    SearchResultColor = Colors.Khaki;
                    break;
                }
            }
        }

        [ProtoMember(1)] public Color BackgroundColor { get; set; }
        [ProtoMember(2)] public Color TextColor { get; set; }
        [ProtoMember(3)] public Color LineNumberTextColor { get; set; }
        [ProtoMember(4)] public Color HighlightedIRElementColor { get; set; }
        [ProtoMember(5)] public Color HoveredIRElementColor { get; set; }
        [ProtoMember(6)] public Color SearchResultColor { get; set; }

        public override bool Equals(object obj) {
            return obj is LightDocumentColors other &&
                   BackgroundColor.Equals(other.BackgroundColor) &&
                   TextColor.Equals(other.TextColor) &&
                   LineNumberTextColor.Equals(other.LineNumberTextColor) &&
                   HighlightedIRElementColor.Equals(other.HighlightedIRElementColor) &&
                   HoveredIRElementColor.Equals(other.HoveredIRElementColor) &&
                   SearchResultColor.Equals(other.SearchResultColor);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class LightDocumentSettings : SettingsBase, INotifyPropertyChanged {
        public LightDocumentSettings() {
            Reset();
        }

        [ProtoMember(1)] public string FontName { get; set; }
        [ProtoMember(2)] public double FontSize { get; set; }
        [ProtoMember(3)] public bool UseIRSyntaxHighlighting { get; set; }
        [ProtoMember(4)] public bool HighlightIRElements { get; set; }
        [ProtoMember(5)] public bool ShowLineNumbers { get; set; }
        [ProtoMember(6)] public bool WordWrap { get; set; }
        [ProtoMember(7)] public bool SyncStyleWithDocument { get; set; }

        public Color BackgroundColor {
            get => currentThemeColors_.BackgroundColor;
            set => currentThemeColors_.BackgroundColor = value;
        }
        public Color TextColor {
            get => currentThemeColors_.TextColor;
            set => currentThemeColors_.TextColor = value;
        }
        public Color LineNumberTextColor {
            get => currentThemeColors_.LineNumberTextColor;
            set => currentThemeColors_.LineNumberTextColor = value;
        }
        public Color HighlightedIRElementColor {
            get => currentThemeColors_.HighlightedIRElementColor;
            set => currentThemeColors_.HighlightedIRElementColor = value;
        }
        public Color HoveredIRElementColor {
            get => currentThemeColors_.HoveredIRElementColor;
            set => currentThemeColors_.HoveredIRElementColor = value;
        }
        public Color SearchResultColor {
            get => currentThemeColors_.SearchResultColor;
            set => currentThemeColors_.SearchResultColor = value;
        }

        [ProtoMember(8)]
        private Dictionary<ApplicationThemeKind, LightDocumentColors> themeColors_;
        private LightDocumentColors currentThemeColors_;

        public event PropertyChangedEventHandler PropertyChanged;

        public override void Reset() {
            LoadThemeSettings();
            FontName = "Consolas";
            FontSize = 12;
            HighlightIRElements = true;
            ShowLineNumbers = true;
            WordWrap = false;
            SyncStyleWithDocument = true;
        }

        [ProtoAfterDeserialization]
        public void LoadThemeSettings() {
            themeColors_ ??= new Dictionary<ApplicationThemeKind, LightDocumentColors>();

            if (!themeColors_.TryGetValue(App.Theme.Kind, out var colors)) {
                colors = new LightDocumentColors();
                themeColors_[App.Theme.Kind] = colors;
            }

            currentThemeColors_ = colors;
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<LightDocumentSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is LightDocumentSettings other &&
                   FontName == other.FontName &&
                   FontSize.Equals(other.FontSize) &&
                   BackgroundColor.Equals(other.BackgroundColor) &&
                   TextColor.Equals(other.TextColor) &&
                   LineNumberTextColor.Equals(other.LineNumberTextColor) &&
                   HighlightedIRElementColor.Equals(other.HighlightedIRElementColor) &&
                   HoveredIRElementColor.Equals(other.HoveredIRElementColor) &&
                   SearchResultColor.Equals(other.SearchResultColor) &&
                   UseIRSyntaxHighlighting == other.UseIRSyntaxHighlighting &&
                   HighlightIRElements == other.HighlightIRElements &&
                   ShowLineNumbers == other.ShowLineNumbers &&
                   WordWrap == other.WordWrap &&
                   SyncStyleWithDocument == other.SyncStyleWithDocument &&
                   Utils.AreEqual(themeColors_, other.themeColors_);
        }
    }
}
