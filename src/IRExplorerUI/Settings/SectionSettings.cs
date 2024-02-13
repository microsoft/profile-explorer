// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class SectionSettings : SettingsBase {
  public SectionSettings() {
    Reset();
  }

  public static readonly int DefaultCallStackPopupDuration = (int)HoverPreview.ExtraLongHoverDuration.TotalMilliseconds;

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
  [ProtoMember(19)] public bool DemangleNoReturnType { get; set; }
  [ProtoMember(20)] public bool DemangleNoSpecialKeywords { get; set; }
  [ProtoMember(21)] public bool ComputeStatistics { get; set; }
  [ProtoMember(22)] public bool IncludeCallGraphStatistics { get; set; }
  [ProtoMember(23)] public bool SyncSourceFile { get; set; }
  [ProtoMember(24)] public bool SyncSelection { get; set; }
  [ProtoMember(25)] public bool ShowCallStackPopup { get; set; }
  [ProtoMember(26)] public int CallStackPopupDuration { get; set; }
  [ProtoMember(28)] public bool ShowPerformanceCounterColumns { get; set; }
  [ProtoMember(29)] public bool ShowPerformanceMetricColumns { get; set; }
  [ProtoMember(30)] public bool AppendTimeToTotalColumn { get; set; }
  [ProtoMember(31)] public bool AppendTimeToSelfColumn { get; set; }
  [ProtoMember(32)] public bool ShowModulePanel { get; set; }
  [ProtoMember(33)] public bool AlternateListRows { get; set; }

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
           AppendTimeToSelfColumn != other.AppendTimeToSelfColumn;
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
    ShowDemangledNames = true;
    DemangleNoSpecialKeywords = true;
    DemangleNoReturnType = true;
    SyncSelection = true;
    ShowCallStackPopup = true;
    ShowModulePanel = true;
    ShowPerformanceCounterColumns = true;
    ShowPerformanceMetricColumns = true;
    AppendTimeToSelfColumn = true;
    AppendTimeToTotalColumn = true;
    AlternateListRows = true;
    CallStackPopupDuration = DefaultCallStackPopupDuration;
    NewSectionColor = Utils.ColorFromString("#007200");
    MissingSectionColor = Utils.ColorFromString("#BB0025");
    ChangedSectionColor = Utils.ColorFromString("#DE8000");
  }

  public SectionSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
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
           DemangleOnlyNames == settings.DemangleOnlyNames &&
           DemangleNoReturnType == settings.DemangleNoReturnType &&
           DemangleNoSpecialKeywords == settings.DemangleNoSpecialKeywords &&
           ComputeStatistics == settings.ComputeStatistics &&
           ShowCallStackPopup == settings.ShowCallStackPopup &&
           CallStackPopupDuration == settings.CallStackPopupDuration &&
           SyncSourceFile == settings.SyncSourceFile &&
           SyncSelection == settings.SyncSelection &&
           ShowPerformanceCounterColumns == settings.ShowPerformanceCounterColumns &&
           ShowPerformanceMetricColumns == settings.ShowPerformanceMetricColumns &&
           AppendTimeToTotalColumn == settings.AppendTimeToTotalColumn &&
           AppendTimeToSelfColumn == settings.AppendTimeToSelfColumn &&
           ShowModulePanel == settings.ShowModulePanel &&
           AlternateListRows == settings.AlternateListRows;
  }
}