// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows.Media;
using IRExplorerUI.Diff;
using ProtoBuf;

namespace IRExplorerUI {
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
        [ProtoMember(16)] public string ExternalDiffAppPath { get; set; }
        [ProtoMember(17), DefaultValue(DiffImplementationKind.Internal)]
        public DiffImplementationKind DiffImplementation { get; set; }

        [ProtoMember(18)] public bool FilterTempVariableNames { get; set; }
        [ProtoMember(19)] public bool FilterSSADefNumbers { get; set; }
        [ProtoMember(20)] public bool ShowInsertions { get; set; }
        [ProtoMember(21)] public bool ShowDeletions { get; set; }
        [ProtoMember(22)] public bool ShowModifications { get; set; }
        [ProtoMember(23)] public bool ShowMinorModifications { get; set; }

        public bool ShowAnyChanges => ShowInsertions || ShowDeletions || ShowModifications || ShowMinorModifications;

        public override void Reset() {
            IdentifyMinorDiffs = true;
            FilterInsignificantDiffs = true;
            FilterTempVariableNames = true;
            FilterSSADefNumbers = true;
            ManyDiffsMarkWholeLine = true;
            ManyDiffsModificationPercentage = 60;
            ManyDiffsInsertionPercentage = 75;
            DiffImplementation = DiffImplementationKind.Internal;
            ShowInsertions = true;
            ShowDeletions = true;
            ShowModifications = true;
            ShowMinorModifications = true;

            DeletionBorderColor = Utils.ColorFromString("#B33232");
            InsertionBorderColor = Utils.ColorFromString("#7FA72E");
            ModificationBorderColor = Utils.ColorFromString("#ff6f00");
            MinorModificationBorderColor = Utils.ColorFromString("#8F8F8F");
            PlaceholderBorderColor = Colors.DarkGray;
            InsertionColor = Utils.ColorFromString("#E2F0D3");
            DeletionColor = Utils.ColorFromString("#FFE8EA");
            ModificationColor = Utils.ColorFromString("#FFF6D9");
            MinorModificationColor = Utils.ColorFromString("#E1E1E1");
        }

        [ProtoAfterDeserialization]
        private void AfterDeserialization() {
            if(!ShowAnyChanges) {
                ShowInsertions = true;
                ShowDeletions = true;
                ShowModifications = true;
                ShowMinorModifications = true;
            }
        }

        public DiffSettings Clone() {
            var serialized = StateSerializer.Serialize(this);
            return StateSerializer.Deserialize<DiffSettings>(serialized);
        }

        public bool HasDiffHandlingChanges(DiffSettings other) {
            return other.IdentifyMinorDiffs != IdentifyMinorDiffs ||
                   other.FilterInsignificantDiffs != FilterInsignificantDiffs ||
                   other.FilterTempVariableNames != FilterTempVariableNames ||
                   other.FilterSSADefNumbers != FilterSSADefNumbers ||
                   other.ManyDiffsMarkWholeLine != ManyDiffsMarkWholeLine ||
                   other.ManyDiffsInsertionPercentage != ManyDiffsInsertionPercentage ||
                   other.ManyDiffsModificationPercentage != ManyDiffsModificationPercentage ||
                   other.DiffImplementation != DiffImplementation ||
                   other.ShowInsertions != ShowInsertions ||
                   other.ShowDeletions != ShowDeletions ||
                   other.ShowModifications != ShowModifications ||
                   other.ShowMinorModifications != ShowMinorModifications;
        }

        public override bool Equals(object obj) {
            return obj is DiffSettings settings &&
                   IdentifyMinorDiffs == settings.IdentifyMinorDiffs &&
                   FilterInsignificantDiffs == settings.FilterInsignificantDiffs &&
                   FilterTempVariableNames == settings.FilterTempVariableNames &&
                   FilterSSADefNumbers == settings.FilterSSADefNumbers &&
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
                   PlaceholderBorderColor.Equals(settings.PlaceholderBorderColor) &&
                   ExternalDiffAppPath == settings.ExternalDiffAppPath &&
                   DiffImplementation == settings.DiffImplementation &&
                   ShowInsertions == settings.ShowInsertions &&
                   ShowDeletions == settings.ShowDeletions &&
                   ShowModifications == settings.ShowModifications &&
                   ShowMinorModifications == settings.ShowMinorModifications;
        }
    }
}
