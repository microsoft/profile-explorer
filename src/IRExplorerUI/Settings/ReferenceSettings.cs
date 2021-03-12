// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class ReferenceColors {
        public ReferenceColors() {
            switch (App.Theme.Kind) {
                case ApplicationThemeKind.Dark: {
                    //? TODO: Set default dark theme colors
                    StoreTextColor = Utils.ColorFromString("#006700");
                    LoadTextColor = Utils.ColorFromString("#4E0088");
                    AddressTextColor = Utils.ColorFromString("#BE0000");
                    SSATextColor = Utils.ColorFromString("#C4C4C4");
                    break;
                }
                default: {
                    StoreTextColor = Utils.ColorFromString("#FFFAFA");
                    LoadTextColor = Utils.ColorFromString("#FFFAFA");
                    AddressTextColor = Utils.ColorFromString("#FFFAFA");
                    SSATextColor = Utils.ColorFromString("#252525");
                    break;
                }
            }
        }

        [ProtoMember(1)] public Color StoreTextColor { get; set; }
        [ProtoMember(2)] public Color LoadTextColor { get; set; }
        [ProtoMember(3)] public Color AddressTextColor { get; set; }
        [ProtoMember(4)] public Color SSATextColor { get; set; }
        
        public override bool Equals(object obj) {
            return obj is ReferenceColors other &&
                   StoreTextColor.Equals(other.StoreTextColor) &&
                   LoadTextColor.Equals(other.LoadTextColor) &&
                   AddressTextColor.Equals(other.AddressTextColor) &&
                   SSATextColor.Equals(other.SSATextColor);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class ReferenceSettings : SettingsBase, INotifyPropertyChanged {
        static readonly Guid Id = new Guid("9DF53096-9446-4174-B3AD-AD7D1632B3CE");
        private ThemeColorSet theme_;
        
        public ReferenceSettings() {
            Reset();
        }

        public Color StoreTextColor {
            get => currentThemeColors_.StoreTextColor;
            set => currentThemeColors_.StoreTextColor = value;
        }
        public Color LoadTextColor {
            get => currentThemeColors_.LoadTextColor;
            set => currentThemeColors_.LoadTextColor = value;
        }
        public Color AddressTextColor {
            get => currentThemeColors_.AddressTextColor;
            set => currentThemeColors_.AddressTextColor = value;
        }
        public Color SSATextColor {
            get => currentThemeColors_.SSATextColor;
            set => currentThemeColors_.SSATextColor = value;
        }

        [ProtoMember(1)]
        private Dictionary<ApplicationThemeKind, ReferenceColors> themeColors_;
        private ReferenceColors currentThemeColors_;

        [ProtoMember(2)] public bool ShowPreviewPopup { get; set; }
        [ProtoMember(3)] public bool ShowOnlySSA { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public override void Reset() {
            ShowPreviewPopup = true;
            ShowOnlySSA = true;
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<ReferenceSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is ReferenceSettings other &&
                   ShowPreviewPopup == other.ShowPreviewPopup &&
                   ShowOnlySSA == other.ShowOnlySSA &&
                   Utils.AreEqual(themeColors_, other.themeColors_);
        }
    }
}
