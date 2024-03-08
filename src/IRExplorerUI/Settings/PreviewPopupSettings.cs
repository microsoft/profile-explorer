// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class PreviewPopupSettings : SettingsBase {
  public PreviewPopupSettings(bool isElementPopup) {
    IsElementPopup = isElementPopup;
  }

  [ProtoMember(1)]
  public bool JumpToHottestElement { get; set; }
  [ProtoMember(2)]
  public bool UseCompactProfilingColumns { get; set; }
  [ProtoMember(3)]
  public bool ShowPerformanceCounterColumns { get; set; }
  [ProtoMember(4)]
  public bool ShowPerformanceMetricColumns { get; set; }
  [ProtoMember(5)]
  public bool UseSmallerFontSize { get; set; }
  [ProtoMember(6)]
  public bool ShowSourcePreviewPopup { get; set; }
  [ProtoMember(7)]
  public double PopupWidth { get; set; }
  [ProtoMember(8)]
  public double PopupHeight { get; set; }
  [ProtoMember(9)]
  public bool IsElementPopup { get; set; }

  private static PreviewPopupSettings Default => new PreviewPopupSettings(false) {
    PopupWidth = 600,
    PopupHeight = 400, // For ASM/source preview.
    JumpToHottestElement = true,
    UseCompactProfilingColumns = true
  };

  private static PreviewPopupSettings ElementDefault => new PreviewPopupSettings(true) {
    PopupWidth = 600,
    PopupHeight = 200
  };

  public override void Reset() {
    var settings = IsElementPopup ? ElementDefault : Default;
    PopupWidth = settings.PopupWidth;
    PopupHeight = settings.PopupHeight;
    JumpToHottestElement = settings.JumpToHottestElement ;
    UseCompactProfilingColumns = settings.UseCompactProfilingColumns ;
    ShowPerformanceCounterColumns = settings.ShowPerformanceCounterColumns ;
    ShowPerformanceMetricColumns = settings.ShowPerformanceMetricColumns ;
    UseSmallerFontSize = settings.UseSmallerFontSize ;
    ShowSourcePreviewPopup = settings.ShowSourcePreviewPopup ;
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
           ShowPerformanceMetricColumns == settings.ShowPerformanceMetricColumns &&
           UseSmallerFontSize == settings.UseSmallerFontSize &&
           ShowSourcePreviewPopup == settings.ShowSourcePreviewPopup &&
           Math.Abs(PopupWidth - settings.PopupWidth) < Double.Epsilon &&
           Math.Abs(PopupHeight - settings.PopupHeight) < Double.Epsilon;
  }

  public override string ToString() {
    return $"IsElementPopup: {IsElementPopup}\n" +
           $"JumpToHottestElement: {JumpToHottestElement}\n" +
           $"UseCompactProfilingColumns: {UseCompactProfilingColumns}\n" +
           $"ShowPerformanceCounterColumns: {ShowPerformanceCounterColumns}\n" +
           $"ShowPerformanceMetricColumns: {ShowPerformanceMetricColumns}\n" +
           $"UseSmallerFontSize: {UseSmallerFontSize}\n" +
           $"ShowSourcePreviewPopup: {ShowSourcePreviewPopup}\n" +
           $"PopupWidth: {PopupWidth}\n" +
           $"PopupHeight: {PopupHeight}";
  }
}