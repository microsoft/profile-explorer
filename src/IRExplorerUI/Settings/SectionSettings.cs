using System;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI {
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

        [ProtoMember(10)] public Color NewSectionColor { get; set; }
        [ProtoMember(11)] public Color MissingSectionColor { get; set; }
        [ProtoMember(12)] public Color ChangedSectionColor { get; set; }

        [ProtoMember(13)] public bool FunctionSearchCaseSensitive { get; set; }
        [ProtoMember(14)] public bool SectionSearchCaseSensitive { get; set; }

        [ProtoMember(15)] public bool MarkSectionsIdenticalToPrevious { get; set; }
        [ProtoMember(16)] public bool LowerIdenticalToPreviousOpacity { get; set; }
        [ProtoMember(17)] public bool ShowDemangledNames { get; set; }
        [ProtoMember(18)] public bool DemangleOnlyNames { get; set; }

        public FunctionNameDemanglingOptions DemanglingOptions {
            get {
                var options = FunctionNameDemanglingOptions.Default;

                if (DemangleOnlyNames) {
                    options |= FunctionNameDemanglingOptions.OnlyName;
                }

                return options;
            }
        }

        public override void Reset() {
            ColorizeSectionNames = true;
            ShowSectionSeparators = true;
            UseNameIndentation = true;
            IndentationAmount = 4;
            MarkAnnotatedSections = true;
            MarkNoDiffSectionGroups = false;
            FunctionSearchCaseSensitive = false;
            SectionSearchCaseSensitive = false;
            MarkSectionsIdenticalToPrevious = true;
            LowerIdenticalToPreviousOpacity = true;
            NewSectionColor = Utils.ColorFromString("#007200");
            MissingSectionColor = Utils.ColorFromString("#BB0025");
            ChangedSectionColor = Utils.ColorFromString("#DE8000");
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
                   LowerIdenticalToPreviousOpacity == settings.LowerIdenticalToPreviousOpacity &&
                   MarkSectionsIdenticalToPrevious == settings.MarkSectionsIdenticalToPrevious &&
                   NewSectionColor.Equals(settings.NewSectionColor) &&
                   MissingSectionColor.Equals(settings.MissingSectionColor) &&
                   ChangedSectionColor.Equals(settings.ChangedSectionColor) &&
                   ShowDemangledNames == settings.ShowDemangledNames &&
                   DemangleOnlyNames == settings.DemangleOnlyNames;
        }
    }
}
