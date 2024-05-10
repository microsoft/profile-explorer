﻿// Copyright (c) Microsoft Corporation
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

  [ProtoMember(1), OptionValue(true)]
  public bool ShowPreviewPopup { get; set; }
  [ProtoMember(2), OptionValue(0)]
  public int PreviewPopupDuration { get; set; }
  [ProtoMember(3), OptionValue(true)]
  public bool ExpandInstances { get; set; }
  [ProtoMember(4), OptionValue(false)]
  public bool ExpandHistogram { get; set; }
  [ProtoMember(5), OptionValue(true)]
  public bool PrependModuleToFunction { get; set; }
  [ProtoMember(6), OptionValue()]
  public ProfileListViewFilter FunctionListViewFilter { get; set; }
  [ProtoMember(7), OptionValue(true)]
  public bool AlternateListRows { get; set; }
  [ProtoMember(8), OptionValue(true)]
  public bool ExpandThreads { get; set; }

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
    FunctionListViewFilter ??= new ProfileListViewFilter();
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
  public ProfileListViewFilter() {
    Reset();
  }

  public static double DefaultMinWeight = 1;
  public static int DefaultMinItems = 10;

  [ProtoMember(1), OptionValue(true)]
  public bool IsEnabled { get; set; }
  [ProtoMember(2), OptionValue(true)]
  public bool FilterByWeight { get; set; }
  [ProtoMember(3), OptionValue(true)]
  public bool SortByExclusiveTime { get; set; }
  [ProtoMember(4), OptionValue(0)]
  public int MinItems { get; set; }
  [ProtoMember(5), OptionValue(0)]
  public double MinWeight { get; set; }

  public override void Reset() {
    ResetAllOptions(this);
    MinItems = DefaultMinItems;
    MinWeight = DefaultMinWeight;
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}