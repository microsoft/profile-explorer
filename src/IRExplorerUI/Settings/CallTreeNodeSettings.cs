// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using IRExplorerUI.Profile;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class CallTreeNodeSettings : SettingsBase {
  public static readonly int DefaultPreviewPopupDuration = (int)HoverPreview.LongHoverDuration.TotalMilliseconds;

  public CallTreeNodeSettings() {
    Reset();
  }

  [ProtoMember(1)]
  public bool ShowPreviewPopup { get; set; }
  [ProtoMember(2)]
  public int PreviewPopupDuration { get; set; }
  [ProtoMember(3)]
  public bool ExpandInstances { get; set; }
  [ProtoMember(4)]
  public bool ExpandHistogram { get; set; }
  [ProtoMember(5)]
  public bool PrependModuleToFunction { get; set; }
  [ProtoMember(6)]
  public ProfileListViewFilter FunctionListViewFilter { get; set; }
  [ProtoMember(7)]
  public bool AlternateListRows { get; set; }
  [ProtoMember(8)]
  public bool ExpandThreads { get; set; }

  public override void Reset() {
    InitializeReferenceMembers();
    ShowPreviewPopup = true;
    ExpandInstances = true;
    ExpandThreads = true;
    PrependModuleToFunction = true;
    AlternateListRows = true;
    PreviewPopupDuration = DefaultPreviewPopupDuration;
    FunctionListViewFilter.Reset();
  }

  public CallTreeNodeSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<CallTreeNodeSettings>(serialized);
  }

  [ProtoAfterDeserialization]
  private void InitializeReferenceMembers() {
    FunctionListViewFilter ??= new ProfileListViewFilter();
  }

  public override bool Equals(object obj) {
    return obj is CallTreeNodeSettings settings &&
           ShowPreviewPopup == settings.ShowPreviewPopup &&
           PreviewPopupDuration == settings.PreviewPopupDuration &&
           ExpandInstances == settings.ExpandInstances &&
           ExpandHistogram == settings.ExpandHistogram &&
           ExpandThreads == settings.ExpandThreads &&
           PrependModuleToFunction == settings.PrependModuleToFunction &&
           AlternateListRows == settings.AlternateListRows &&
           FunctionListViewFilter.Equals(settings.FunctionListViewFilter);
  }
}

[ProtoContract(SkipConstructor = true)]
public class ProfileListViewFilter : SettingsBase {
  public ProfileListViewFilter() {
    Reset();
  }

  public static double DefaultMinWeight = 1;
  public static int DefaultMinItems = 10;

  [ProtoMember(1)]
  public bool IsEnabled { get; set; }
  [ProtoMember(2)]
  public bool FilterByWeight { get; set; }
  [ProtoMember(3)]
  public bool SortByExclusiveTime { get; set; }
  [ProtoMember(4)]
  public int MinItems { get; set; }
  [ProtoMember(5)]
  public double MinWeight { get; set; }

  public override void Reset() {
    IsEnabled = true;
    FilterByWeight = true;
    SortByExclusiveTime = true;
    MinItems = DefaultMinItems;
    MinWeight = DefaultMinWeight;
  }

  public override bool Equals(object obj) {
    return obj is ProfileListViewFilter other &&
           IsEnabled == other.IsEnabled &&
           FilterByWeight == other.FilterByWeight &&
           SortByExclusiveTime == other.SortByExclusiveTime &&
           MinItems == other.MinItems &&
           Math.Abs(MinWeight - other.MinWeight) < double.Epsilon;
  }

  public override string ToString() {
    return $"IsEnabled: {IsEnabled}\n" +
           $"FilterByWeight: {FilterByWeight}\n" +
           $"SortByExclusiveTime: {SortByExclusiveTime}\n" +
           $"MinItems: {MinItems}\n" +
           $"MinWeight: {MinWeight}";
  }
}