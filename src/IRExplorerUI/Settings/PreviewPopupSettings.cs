// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class PreviewPopupSettings : SettingsBase {
  public PreviewPopupSettings() {
    Reset();
  }

  [ProtoMember(1)]
  public bool JumpToHottestElement { get; set; }
  [ProtoMember(2)]
  public bool UseCompactProfilingColumns { get; set; }
  [ProtoMember(3)]
  public bool ShowPerformanceCounterColumns { get; set; }
  [ProtoMember(4)]
  public bool UseSmallerFontSize { get; set; }
  [ProtoMember(5)]
  public bool ShowSourcePreviewPopup { get; set; }
  [ProtoMember(6)]
  public double PopupWidth { get; set; }
  [ProtoMember(7)]
  public double PopupHeight { get; set; }
  
  public override void Reset() {
    JumpToHottestElement = true;
    UseCompactProfilingColumns = true;
    PopupWidth = 600;
    PopupHeight = 400;
  }

  public PreviewPopupSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<PreviewPopupSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is PreviewPopupSettings settings &&
           JumpToHottestElement == settings.JumpToHottestElement &&
           UseCompactProfilingColumns == settings.UseCompactProfilingColumns &&
           ShowPerformanceCounterColumns == settings.ShowPerformanceCounterColumns &&
           UseSmallerFontSize == settings.UseSmallerFontSize &&
           ShowSourcePreviewPopup == settings.ShowSourcePreviewPopup &&
           Math.Abs(PopupWidth - settings.PopupWidth) < Double.Epsilon &&
           Math.Abs(PopupHeight - settings.PopupHeight) < Double.Epsilon;
  }
}