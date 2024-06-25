// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Collections.Generic;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class RemarkSettings : SettingsBase {
  public RemarkSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(false)]
  public bool ShowRemarks { get; set; }
  [ProtoMember(2)][OptionValue(false)]
  public bool ShowPreviousSections { get; set; }
  [ProtoMember(3)][OptionValue(true)]
  public bool StopAtSectionBoundaries { get; set; }
  [ProtoMember(4)][OptionValue(false)]
  public int SectionHistoryDepth { get; set; }
  [ProtoMember(5)][OptionValue(true)]
  public bool ShowPreviousOptimizationRemarks { get; set; }
  [ProtoMember(6)][OptionValue(true)]
  public bool ShowActionButtonOnHover { get; set; }
  [ProtoMember(7)][OptionValue(false)]
  public bool ShowActionButtonWithModifier { get; set; }
  [ProtoMember(8)][OptionValue(true)]
  public bool ShowMarginRemarks { get; set; }
  [ProtoMember(9)][OptionValue(true)]
  public bool ShowDocumentRemarks { get; set; }
  [ProtoMember(10)][OptionValue(true)]
  public bool UseRemarkBackground { get; set; }
  [ProtoMember(11)][OptionValue(true)]
  public bool UseTransparentRemarkBackground { get; set; }
  [ProtoMember(12)][OptionValue(25)]
  public int RemarkBackgroundOpacity { get; set; }
  [ProtoMember(13)][OptionValue(true)]
  public bool Default { get; set; }
  [ProtoMember(14)][OptionValue(true)]
  public bool Verbose { get; set; }
  [ProtoMember(15)][OptionValue(false)]
  public bool Trace { get; set; }
  [ProtoMember(16)][OptionValue(true)]
  public bool Analysis { get; set; }
  [ProtoMember(17)][OptionValue(true)]
  public bool Optimization { get; set; }
  [ProtoMember(18)][OptionValue()]
  public Dictionary<string, bool> CategoryFilter { get; set; }
  [ProtoMember(19)][OptionValue(false)]
  public bool ShowPreviousAnalysisRemarks { get; set; }
  public string SearchedText { get; set; }
  public bool HasCategoryFilters => CategoryFilter != null && CategoryFilter.Count > 0;

  public override void Reset() {
    ResetAllOptions(this);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public RemarkSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<RemarkSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}
