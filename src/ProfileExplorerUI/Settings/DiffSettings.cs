// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.ComponentModel;
using System.Windows.Media;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class DiffSettings : SettingsBase {
  public DiffSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(true)]
  public bool IdentifyMinorDiffs { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool FilterInsignificantDiffs { get; set; }
  [ProtoMember(3)][OptionValue(true)]
  public bool ManyDiffsMarkWholeLine { get; set; }
  [ProtoMember(4)][OptionValue(60)]
  public int ManyDiffsModificationPercentage { get; set; }
  [ProtoMember(5)][OptionValue(75)]
  public int ManyDiffsInsertionPercentage { get; set; }
  [ProtoMember(6)][OptionValue("#FFF6D9")]
  public Color ModificationColor { get; set; }
  [ProtoMember(7)][OptionValue("#ff6f00")]
  public Color ModificationBorderColor { get; set; }
  [ProtoMember(8)][OptionValue("#E2F0D3")]
  public Color InsertionColor { get; set; }
  [ProtoMember(9)][OptionValue("#7FA72E")]
  public Color InsertionBorderColor { get; set; }
  [ProtoMember(10)][OptionValue("#FFE8EA")]
  public Color DeletionColor { get; set; }
  [ProtoMember(11)][OptionValue("#B33232")]
  public Color DeletionBorderColor { get; set; }
  [ProtoMember(12)][OptionValue("#E1E1E1")]
  public Color MinorModificationColor { get; set; }
  [ProtoMember(13)][OptionValue("#8F8F8F")]
  public Color MinorModificationBorderColor { get; set; }
  [ProtoMember(15)][OptionValue("#A9A9A9")]
  public Color PlaceholderBorderColor { get; set; }
  [ProtoMember(16)][OptionValue("")]
  public string ExternalDiffAppPath { get; set; }
  [ProtoMember(17)][OptionValue(DiffImplementationKind.Internal)]
  [DefaultValue(DiffImplementationKind.Internal)]
  public DiffImplementationKind DiffImplementation { get; set; }
  [ProtoMember(18)][OptionValue(true)]
  public bool FilterTempVariableNames { get; set; }
  [ProtoMember(19)][OptionValue(true)]
  public bool FilterSSADefNumbers { get; set; }
  [ProtoMember(20)][OptionValue(true)]
  public bool ShowInsertions { get; set; }
  [ProtoMember(21)][OptionValue(true)]
  public bool ShowDeletions { get; set; }
  [ProtoMember(22)][OptionValue(true)]
  public bool ShowModifications { get; set; }
  [ProtoMember(23)][OptionValue(true)]
  public bool ShowMinorModifications { get; set; }
  public bool ShowAnyChanges => ShowInsertions || ShowDeletions || ShowModifications || ShowMinorModifications;

  public override void Reset() {
    ResetAllOptions(this);
  }

  public DiffSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
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
    return AreOptionsEqual(this, obj);
  }

  [ProtoAfterDeserialization]
  private void AfterDeserialization() {
    if (!ShowAnyChanges) {
      ShowInsertions = true;
      ShowDeletions = true;
      ShowModifications = true;
      ShowMinorModifications = true;
    }
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}