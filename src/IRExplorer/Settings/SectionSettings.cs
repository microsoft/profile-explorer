using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorer {
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

        public override void Reset() {
            ColorizeSectionNames = true;
            ShowSectionSeparators = true;
            UseNameIndentation = true;
            IndentationAmount = 4;
            MarkAnnotatedSections = true;
            MarkNoDiffSectionGroups = false;
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
                   NewSectionColor.Equals(settings.NewSectionColor) &&
                   MissingSectionColor.Equals(settings.MissingSectionColor) &&
                   ChangedSectionColor.Equals(settings.ChangedSectionColor);
        }
    }
}
