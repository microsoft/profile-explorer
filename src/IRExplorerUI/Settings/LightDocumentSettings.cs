// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class LightDocumentSettings : SettingsBase, INotifyPropertyChanged {
        public LightDocumentSettings() {
            Reset();
        }

        [ProtoMember(1)] public string FontName { get; set; }
        [ProtoMember(2)] public double FontSize { get; set; }
        [ProtoMember(3)] public Color BackgroundColor { get; set; }
        [ProtoMember(4)] public Color TextColor { get; set; }
        [ProtoMember(5)] public Color LineNumberTextColor { get; set; }
        [ProtoMember(6)] public Color HighlightedIRElementColor { get; set; }
        [ProtoMember(7)] public Color HoveredIRElementColor { get; set; }
        [ProtoMember(8)] public Color SearchResultColor { get; set; }

        [ProtoMember(9)] public bool UseIRSyntaxHighlighting { get; set; }
        [ProtoMember(10)] public bool HighlightIRElements { get; set; }
        [ProtoMember(11)] public bool ShowLineNumbers { get; set; }
        [ProtoMember(12)] public bool WordWrap { get; set; }
        [ProtoMember(13)] public bool SyncStyleWithDocument { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public override void Reset() {
            FontName = "Consolas";
            FontSize = 12;
            BackgroundColor = Utils.ColorFromString("#FFFAFA");
            TextColor = Colors.Black;
            HighlightedIRElementColor = Utils.ColorFromString("#FFFCDC");
            HoveredIRElementColor = Utils.ColorFromString("#FFFCDC");
            LineNumberTextColor = Colors.Silver;
            SearchResultColor = Colors.Khaki;

            HighlightIRElements = true;
            ShowLineNumbers = true;
            WordWrap = false;
            SyncStyleWithDocument = true;
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
                   SyncStyleWithDocument == other.SyncStyleWithDocument;
        }
    }
}
