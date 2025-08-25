// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows.Media;
using ProfileExplorerCore2.Settings;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class CallTreeNodeSettings : SettingsBase {
  public static readonly int DefaultPreviewPopupDuration = (int)HoverPreview.ExtraLongHoverDurationMs;

  public CallTreeNodeSettings() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(true)]
  public bool ShowPreviewPopup { get; set; }
  [ProtoMember(2)][OptionValue(0)]
  public int PreviewPopupDuration { get; set; }
  [ProtoMember(3)][OptionValue(true)]
  public bool ExpandInstances { get; set; }
  [ProtoMember(4)][OptionValue(false)]
  public bool ExpandHistogram { get; set; }
  [ProtoMember(5)][OptionValue(true)]
  public bool PrependModuleToFunction { get; set; }
  [ProtoMember(6)][OptionValue()]
  public ProfileListViewFilter FunctionListViewFilter { get; set; }
  [ProtoMember(7)][OptionValue(true)]
  public bool AlternateListRows { get; set; }
  [ProtoMember(8)][OptionValue(true)]
  public bool ExpandThreads { get; set; }
  [ProtoMember(9)][OptionValue()]
  public ColumnSettings StackListColumns { get; set; }
  [ProtoMember(10)][OptionValue()]
  public ColumnSettings FunctionListColumns { get; set; }
  [ProtoMember(11)][OptionValue()]
  public ColumnSettings ModuleListColumns { get; set; }
  [ProtoMember(12)][OptionValue()]
  public ColumnSettings ModuleFunctionListColumns { get; set; }
  [ProtoMember(13)][OptionValue()]
  public ColumnSettings CategoryListColumns { get; set; }
  [ProtoMember(14)][OptionValue()]
  public ColumnSettings CategoryFunctionListColumns { get; set; }
  [ProtoMember(15)][OptionValue()]
  public ColumnSettings InstanceListColumns { get; set; }
  [ProtoMember(16)][OptionValue("#F2EA9D")]
  public Color HistogramBarColor { get; set; }
  [ProtoMember(17)][OptionValue("#B22222")]
  public Color HistogramAverageColor { get; set; }
  [ProtoMember(18)][OptionValue("#00008B")]
  public Color HistogramMedianColor { get; set; }
  [ProtoMember(19)][OptionValue("#008000")]
  public Color HistogramCurrentColor { get; set; }

  public override void Reset() {
    InitializeReferenceMembers();
    ResetAllOptions(this);
    PreviewPopupDuration = DefaultPreviewPopupDuration;
  }

  public CallTreeNodeSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<CallTreeNodeSettings>(serialized);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    InitializeReferenceOptions(this);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}

[ProtoContract(SkipConstructor = true)]
public class ProfileListViewFilter : SettingsBase {
  public static double DefaultMinWeight = 1;
  public static int DefaultMinItems = 10;

  public ProfileListViewFilter() {
    Reset();
  }

  [ProtoMember(1)][OptionValue(true)]
  public bool IsEnabled { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool FilterByWeight { get; set; }
  [ProtoMember(3)][OptionValue(true)]
  public bool SortByExclusiveTime { get; set; }
  [ProtoMember(4)][OptionValue(10)]
  public int MinItems { get; set; }
  [ProtoMember(5)][OptionValue(1)]
  public double MinWeight { get; set; }
  [ProtoMember(6)][OptionValue(0.01)]
  public double MinPercentage { get; set; }

  public override void Reset() {
    ResetAllOptions(this);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}