// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Settings;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class PreviewPopupSettings : SettingsBase {
  public PreviewPopupSettings() : this(false) { }

  public PreviewPopupSettings(bool isElementPopup) {
    IsElementPopup = isElementPopup;
    Reset();
  }

  [ProtoMember(1)][OptionValue(false)]
  public bool JumpToHottestElement { get; set; }
  [ProtoMember(2)][OptionValue(false)]
  public bool UseCompactProfilingColumns { get; set; }
  [ProtoMember(3)][OptionValue(false)]
  public bool ShowPerformanceCounterColumns { get; set; }
  [ProtoMember(4)][OptionValue(false)]
  public bool ShowPerformanceMetricColumns { get; set; }
  [ProtoMember(5)][OptionValue(false)]
  public bool UseSmallerFontSize { get; set; }
  [ProtoMember(6)][OptionValue(false)]
  public bool ShowSourcePreviewPopup { get; set; }
  [ProtoMember(7)][OptionValue(550)]
  public double PopupWidth { get; set; }
  [ProtoMember(8)][OptionValue(400)]
  public double PopupHeight { get; set; }
  [ProtoMember(9)][OptionValue(false)]
  public bool IsElementPopup { get; set; }

  public override void Reset() {
    bool isElementPopup = IsElementPopup;
    ResetAllOptions(this);

    if (isElementPopup) {
      PopupHeight = 200;
      IsElementPopup = true;
    }
    else {
      JumpToHottestElement = true;
      UseCompactProfilingColumns = true;
    }
  }

  public PreviewPopupSettings Clone() {
    byte[] serialized = UIStateSerializer.Serialize(this);
    return UIStateSerializer.Deserialize<PreviewPopupSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}