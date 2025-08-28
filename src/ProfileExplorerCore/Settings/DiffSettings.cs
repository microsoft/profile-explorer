// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.ComponentModel;
using ProfileExplorer.Core.Diff;
using ProfileExplorer.Core.Session;
using ProtoBuf;

namespace ProfileExplorer.Core.Settings;

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
  [ProtoMember(6)][OptionValue(DiffImplementationKind.Internal)]
  [DefaultValue(DiffImplementationKind.Internal)]
  public DiffImplementationKind DiffImplementation { get; set; }
  [ProtoMember(7)][OptionValue(true)]
  public bool FilterTempVariableNames { get; set; }
  [ProtoMember(8)][OptionValue(true)]
  public bool FilterSSADefNumbers { get; set; }
  [ProtoMember(9)][OptionValue(true)]
  public bool ShowInsertions { get; set; }
  [ProtoMember(10)][OptionValue(true)]
  public bool ShowDeletions { get; set; }
  [ProtoMember(11)][OptionValue(true)]
  public bool ShowModifications { get; set; }
  [ProtoMember(12)][OptionValue(true)]
  public bool ShowMinorModifications { get; set; }

  [ProtoMember(13)]
  [OptionValue("")]
  public string ExternalDiffAppPath { get; set; }
  public bool ShowAnyChanges => ShowInsertions || ShowDeletions || ShowModifications || ShowMinorModifications;

  public override void Reset() {
    ResetAllOptions(this);
  }

  public virtual DiffSettings Clone() {
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