using System.Windows.Media;
using ProtoBuf;

namespace IRExplorer {
    [ProtoContract(SkipConstructor = true)]
    public class DiffSettings : SettingsBase {
        public DiffSettings() {
            Reset();
        }

        [ProtoMember(1)] public bool IdentifyMinorDiffs { get; set; }
        [ProtoMember(2)] public bool FilterInsignificantDiffs { get; set; }
        [ProtoMember(3)] public bool ManyDiffsMarkWholeLine { get; set; }
        [ProtoMember(4)] public int ManyDiffsModificationPercentage { get; set; }
        [ProtoMember(5)] public int ManyDiffsInsertionPercentage { get; set; }

        [ProtoMember(6)] public Color ModificationColor { get; set; }
        [ProtoMember(7)] public Color ModificationBorderColor { get; set; }
        [ProtoMember(8)] public Color InsertionColor { get; set; }
        [ProtoMember(9)] public Color InsertionBorderColor { get; set; }
        [ProtoMember(10)] public Color DeletionColor { get; set; }
        [ProtoMember(11)] public Color DeletionBorderColor { get; set; }
        [ProtoMember(12)] public Color MinorModificationColor { get; set; }
        [ProtoMember(13)] public Color MinorModificationBorderColor { get; set; }
        [ProtoMember(15)] public Color PlaceholderBorderColor { get; set; }

        public override void Reset() {
            IdentifyMinorDiffs = true;
            FilterInsignificantDiffs = true;
            ManyDiffsMarkWholeLine = true;
            ManyDiffsModificationPercentage = 60;
            ManyDiffsInsertionPercentage = 75;

            DeletionBorderColor = Utils.ColorFromString("#B33232");
            InsertionBorderColor = Utils.ColorFromString("#7FA72E");
            ModificationBorderColor = Utils.ColorFromString("#ff6f00");
            MinorModificationBorderColor = Utils.ColorFromString("#8F8F8F");
            PlaceholderBorderColor = Colors.DarkGray;
            InsertionColor = Utils.ColorFromString("#c5e1a5");
            DeletionColor = Utils.ColorFromString("#FFD6D9");
            ModificationColor = Utils.ColorFromString("#FFF6D9");
            MinorModificationColor = Utils.ColorFromString("#E1E1E1");
        }

        public override SettingsBase Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<DiffSettings>(serialized);
        }

        public bool HasDiffHandlingChanges(DiffSettings other) {
            return other.IdentifyMinorDiffs != IdentifyMinorDiffs ||
                other.FilterInsignificantDiffs != FilterInsignificantDiffs ||
                other.ManyDiffsMarkWholeLine != ManyDiffsMarkWholeLine ||
                other.ManyDiffsInsertionPercentage != ManyDiffsInsertionPercentage ||
                other.ManyDiffsModificationPercentage != ManyDiffsModificationPercentage;
        }

        public override bool Equals(object obj) {
            return obj is DiffSettings settings &&
                   IdentifyMinorDiffs == settings.IdentifyMinorDiffs &&
                   FilterInsignificantDiffs == settings.FilterInsignificantDiffs &&
                   ManyDiffsMarkWholeLine == settings.ManyDiffsMarkWholeLine &&
                   ManyDiffsModificationPercentage == settings.ManyDiffsModificationPercentage &&
                   ManyDiffsInsertionPercentage == settings.ManyDiffsInsertionPercentage &&
                   ModificationColor.Equals(settings.ModificationColor) &&
                   ModificationBorderColor.Equals(settings.ModificationBorderColor) &&
                   InsertionColor.Equals(settings.InsertionColor) &&
                   InsertionBorderColor.Equals(settings.InsertionBorderColor) &&
                   DeletionColor.Equals(settings.DeletionColor) &&
                   DeletionBorderColor.Equals(settings.DeletionBorderColor) &&
                   MinorModificationColor.Equals(settings.MinorModificationColor) &&
                   MinorModificationBorderColor.Equals(settings.MinorModificationBorderColor) &&
                   PlaceholderBorderColor.Equals(settings.PlaceholderBorderColor);
        }
    }
}
