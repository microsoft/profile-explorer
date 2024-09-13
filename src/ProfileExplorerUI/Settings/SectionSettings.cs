// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows.Media;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class SectionSettings : SettingsBase {
  public static readonly int DefaultCallStackPopupDuration = (int)HoverPreview.ExtraLongHoverDuration.TotalMilliseconds;

  public SectionSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(true)]
  public bool ColorizeSectionNames { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool MarkAnnotatedSections { get; set; }
  [ProtoMember(3)][OptionValue(false)]
  public bool MarkNoDiffSectionGroups { get; set; }
  [ProtoMember(4)][OptionValue(true)]
  public bool ShowSectionSeparators { get; set; }
  [ProtoMember(5)][OptionValue(true)]
  public bool UseNameIndentation { get; set; }
  [ProtoMember(6)][OptionValue(4)]
  public int IndentationAmount { get; set; }
  [ProtoMember(10)][OptionValue("#007200")]
  public Color NewSectionColor { get; set; }
  [ProtoMember(11)][OptionValue("#BB0025")]
  public Color MissingSectionColor { get; set; }
  [ProtoMember(12)][OptionValue("#DE8000")]
  public Color ChangedSectionColor { get; set; }
  [ProtoMember(13)][OptionValue(false)]
  public bool FunctionSearchCaseSensitive { get; set; }
  [ProtoMember(14)][OptionValue(false)]
  public bool SectionSearchCaseSensitive { get; set; }
  [ProtoMember(15)][OptionValue(true)]
  public bool MarkSectionsIdenticalToPrevious { get; set; }
  [ProtoMember(16)][OptionValue(true)]
  public bool LowerIdenticalToPreviousOpacity { get; set; }
  [ProtoMember(17)][OptionValue(true)]
  public bool ShowDemangledNames { get; set; }
  [ProtoMember(18)][OptionValue(true)]
  public bool DemangleOnlyNames { get; set; }
  [ProtoMember(19)][OptionValue(true)]
  public bool DemangleNoReturnType { get; set; }
  [ProtoMember(20)][OptionValue(true)]
  public bool DemangleNoSpecialKeywords { get; set; }
  [ProtoMember(21)][OptionValue(false)]
  public bool ComputeStatistics { get; set; }
  [ProtoMember(22)][OptionValue(false)]
  public bool IncludeCallGraphStatistics { get; set; }
  [ProtoMember(23)][OptionValue(false)]
  public bool SyncSourceFile { get; set; }
  [ProtoMember(24)][OptionValue(true)]
  public bool SyncSelection { get; set; }
  [ProtoMember(25)][OptionValue(true)]
  public bool ShowCallStackPopup { get; set; }
  [ProtoMember(26)][OptionValue(0)]
  public int CallStackPopupDuration { get; set; }
  [ProtoMember(28)][OptionValue(true)]
  public bool ShowPerformanceCounterColumns { get; set; }
  [ProtoMember(29)][OptionValue(true)]
  public bool ShowPerformanceMetricColumns { get; set; }
  [ProtoMember(30)][OptionValue(true)]
  public bool AppendTimeToTotalColumn { get; set; }
  [ProtoMember(31)][OptionValue(true)]
  public bool AppendTimeToSelfColumn { get; set; }
  [ProtoMember(32)][OptionValue(true)]
  public bool ShowModulePanel { get; set; }
  [ProtoMember(33)][OptionValue(true)]
  public bool AlternateListRows { get; set; }
  [ProtoMember(34)][OptionValue(false)]
  public bool ShowMangleNamesColumn { get; set; }
  [ProtoMember(35)][OptionValue()]
  public ColumnSettings FunctionListColumns { get; set; }
  [ProtoMember(36)][OptionValue()]
  public ColumnSettings SectionListColumns { get; set; }
  [ProtoMember(37)][OptionValue()]
  public ColumnSettings ModuleListColumns { get; set; }

  public FunctionNameDemanglingOptions DemanglingOptions {
    get {
      var options = FunctionNameDemanglingOptions.Default;

      if (DemangleOnlyNames) {
        options |= FunctionNameDemanglingOptions.OnlyName;
      }

      if (DemangleNoReturnType) {
        options |= FunctionNameDemanglingOptions.NoReturnType;
      }

      if (DemangleNoSpecialKeywords) {
        options |= FunctionNameDemanglingOptions.NoSpecialKeywords;
      }

      return options;
    }
  }

  public bool HasFunctionListChanges(SectionSettings other) {
    return ShowDemangledNames != other.ShowDemangledNames ||
           DemangleOnlyNames != other.DemangleOnlyNames ||
           DemangleNoReturnType != other.DemangleNoReturnType ||
           DemangleNoSpecialKeywords != other.DemangleNoSpecialKeywords ||
           ShowPerformanceCounterColumns != other.ShowPerformanceCounterColumns ||
           ShowPerformanceMetricColumns != other.ShowPerformanceMetricColumns ||
           AppendTimeToTotalColumn != other.AppendTimeToTotalColumn ||
           AppendTimeToSelfColumn != other.AppendTimeToSelfColumn ||
           ShowMangleNamesColumn != other.ShowMangleNamesColumn;
  }

  public override void Reset() {
    ResetAllOptions(this);
    CallStackPopupDuration = DefaultCallStackPopupDuration;
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public SectionSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<SectionSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}