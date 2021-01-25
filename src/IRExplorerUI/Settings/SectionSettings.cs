using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
    [ProtoContract(SkipConstructor = true)]
    public class SectionColors {
        public SectionColors() {
            switch (App.Theme.Kind) {
                case ApplicationThemeKind.Dark: {
                    //? TODO: Set default dark theme colors
                    NewSectionColor = Utils.ColorFromString("#007200");
                    MissingSectionColor = Utils.ColorFromString("#BB0025");
                    ChangedSectionColor = Utils.ColorFromString("#DE8000");
                    break;
                }
                default: {
                    NewSectionColor = Utils.ColorFromString("#007200");
                    MissingSectionColor = Utils.ColorFromString("#BB0025");
                    ChangedSectionColor = Utils.ColorFromString("#DE8000");
                    break;
                }
            }
        }

        [ProtoMember(1)] public Color NewSectionColor { get; set; }
        [ProtoMember(2)] public Color MissingSectionColor { get; set; }
        [ProtoMember(3)] public Color ChangedSectionColor { get; set; }

        public override bool Equals(object obj) {
            return obj is SectionColors other &&
                   NewSectionColor.Equals(other.NewSectionColor) &&
                   MissingSectionColor.Equals(other.MissingSectionColor) &&
                   ChangedSectionColor.Equals(other.ChangedSectionColor);
        }
    }

    [ProtoContract(SkipConstructor = true)]
    public class SectionSettings : SettingsBase {
        public SectionSettings() {
            Reset();
        }

        [ProtoMember(1)] public bool ColorizeSectionNames { get; set; }
        [ProtoMember(2)] public bool MarkAnnotatedSections { get; set; }
        [ProtoMember(3)] public bool MarkNoDiffSectionGroups { get; set; }
        [ProtoMember(4)] public bool ShowSectionSeparators { get; set; }
        [ProtoMember(5)] public bool UseNameIndentation { get; set; }
        [ProtoMember(6)] public int IndentationAmount { get; set; }

        public Color NewSectionColor {
            get => currentThemeColors_.NewSectionColor;
            set => currentThemeColors_.NewSectionColor = value;
        }

        public Color MissingSectionColor {
            get => currentThemeColors_.MissingSectionColor;
            set => currentThemeColors_.MissingSectionColor = value;
        }
        
        public Color ChangedSectionColor {
            get => currentThemeColors_.ChangedSectionColor;
            set => currentThemeColors_.ChangedSectionColor = value;
        }

        [ProtoMember(7)] public bool FunctionSearchCaseSensitive { get; set; }
        [ProtoMember(8)] public bool SectionSearchCaseSensitive { get; set; }

        [ProtoMember(9)]
        private Dictionary<ApplicationThemeKind, SectionColors> themeColors_;
        private SectionColors currentThemeColors_;

        public override void Reset() {
            LoadThemeSettings();
            ColorizeSectionNames = true;
            ShowSectionSeparators = true;
            UseNameIndentation = true;
            IndentationAmount = 4;
            MarkAnnotatedSections = true;
            MarkNoDiffSectionGroups = false;
            FunctionSearchCaseSensitive = false;
            SectionSearchCaseSensitive = false;
        }

        [ProtoAfterDeserialization]
        public void LoadThemeSettings() {
            themeColors_ ??= new Dictionary<ApplicationThemeKind, SectionColors>();

            if (!themeColors_.TryGetValue(App.Theme.Kind, out var colors)) {
                colors = new SectionColors();
                themeColors_[App.Theme.Kind] = colors;
            }

            currentThemeColors_ = colors;
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<SectionSettings>(serialized);
        }

        public override bool Equals(object obj) {
            return obj is SectionSettings settings &&
                   ColorizeSectionNames == settings.ColorizeSectionNames &&
                   MarkAnnotatedSections == settings.MarkAnnotatedSections &&
                   MarkNoDiffSectionGroups == settings.MarkNoDiffSectionGroups &&
                   ShowSectionSeparators == settings.ShowSectionSeparators &&
                   UseNameIndentation == settings.UseNameIndentation &&
                   IndentationAmount == settings.IndentationAmount &&
                   FunctionSearchCaseSensitive == settings.FunctionSearchCaseSensitive &&
                   SectionSearchCaseSensitive == settings.SectionSearchCaseSensitive &&
                   Utils.AreEqual(themeColors_, settings.themeColors_);
        }
    }
}
