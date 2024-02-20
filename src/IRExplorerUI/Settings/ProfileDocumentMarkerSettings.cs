// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class ProfileDocumentMarkerSettings : SettingsBase {
  private static ColorPalette defaultBackColorPalette_ = ColorPalette.Profile;

  public ProfileDocumentMarkerSettings() {
    Reset();
  }

  public enum ValueUnitKind {
    Nanosecond,
    Microsecond,
    Millisecond,
    Second,
    Percent,
    Value,
    Default
  }

  //? TODO: Counter shortening list
  // private static readonly (string, string)[] PerfCounterNameReplacements = {
  //   ("Instruction", "Instr"),
  //   ("Misprediction", "Mispred")
  // };

  [ProtoMember(1)] public bool MarkElements { get; set; }
  [ProtoMember(2)] public bool MarkBlocks { get; set; }
  [ProtoMember(3)] public bool MarkBlocksInFlowGraph { get; set; }
  [ProtoMember(4)] public bool MarkCallTargets { get; set; }
  [ProtoMember(5)] public bool JumpToHottestElement { get; set; }
  [ProtoMember(7)]  public double ElementWeightCutoff { get; set; }
  [ProtoMember(8)]  public int TopOrderCutoff { get; set; }
  [ProtoMember(9)]  public double IconBarWeightCutoff { get; set; }
  [ProtoMember(10)]  public Color ColumnTextColor { get; set; }
  [ProtoMember(11)]  public Color BlockOverlayTextColor { get; set; }
  [ProtoMember(12)]  public Color HotBlockOverlayTextColor { get; set; }
  [ProtoMember(14)]  public Color BlockOverlayBorderColor { get; set; }
  [ProtoMember(15)]  public double BlockOverlayBorderThickness { get; set; }
  [ProtoMember(16)]  public Color PercentageBarBackColor { get; set; }
  [ProtoMember(17)]  public int MaxPercentageBarWidth { get; set; }
  [ProtoMember(18)]  public bool DisplayPercentageBar { get; set; }
  [ProtoMember(19)]  public bool DisplayIcons { get; set; }
  [ProtoMember(20)]  public ValueUnitKind ValueUnit { get; set; }
  [ProtoMember(21)] public bool AppendValueUnitSuffix { get; set; }
  [ProtoMember(22)] public int ValueUnitDecimals { get; set; }
  [ProtoMember(23)]  public Color PerformanceMetricBackColor { get; set; }
  [ProtoMember(24)]  public Color PerformanceCounterBackColor { get; set; }

  public static int DefaultMaxPercentageBarWidth = 50;
  public static double DefaultElementWeightCutoff = 0.009; // 0.9%;

  public override void Reset() {
    MarkElements = true;
    MarkBlocks = true;
    MarkBlocksInFlowGraph = true;
    MarkCallTargets = true;
    ValueUnit = ValueUnitKind.Millisecond;
    ValueUnitDecimals = 2;
    AppendValueUnitSuffix = true;
    ElementWeightCutoff = DefaultElementWeightCutoff;
    TopOrderCutoff = 10;
    IconBarWeightCutoff = DefaultElementWeightCutoff;
    MaxPercentageBarWidth = DefaultMaxPercentageBarWidth;
    DisplayIcons = true;
    DisplayPercentageBar = true;
    ColumnTextColor = Colors.Black;
    BlockOverlayTextColor = Colors.DarkBlue;
    HotBlockOverlayTextColor = Colors.DarkRed;
    BlockOverlayBorderColor = Colors.DimGray;
    BlockOverlayBorderThickness = 1;
    PercentageBarBackColor = Utils.ColorFromString("#Aa4343");
    PerformanceMetricBackColor = Colors.AntiqueWhite;
    PerformanceCounterBackColor = Colors.WhiteSmoke;
  }

  public string FormatWeightValue(OptionalColumn column, TimeSpan weight) {
    string suffix = "";
    if (AppendValueUnitSuffix) {
      suffix = ValueUnit switch {
        ValueUnitKind.Millisecond => " ms",
        ValueUnitKind.Microsecond => " µs",
        ValueUnitKind.Nanosecond => " ns",
        ValueUnitKind.Second => " s",
        _ => ""
      };
    }

    return ValueUnit switch {
      ValueUnitKind.Millisecond => weight.AsMillisecondsString(ValueUnitDecimals, suffix),
      ValueUnitKind.Microsecond => weight.AsMicrosecondString(ValueUnitDecimals, suffix),
      ValueUnitKind.Nanosecond => weight.AsNanosecondsString(ValueUnitDecimals, suffix),
      ValueUnitKind.Second => weight.AsSecondsString(ValueUnitDecimals, suffix),
      _ => weight.Ticks.ToString()
    };
  }

  public Brush PickBackColor(OptionalColumn column, int order, double percentage) {
    if (!ShouldUseBackColor(column)) {
      return Brushes.Transparent;
    }

    //? TODO: ShouldUsePalette, ColorPalette in Appearance
    return column.Style.PickColorForPercentage &&
           !ShouldOverridePercentage(order, percentage)
      ? PickBackColorForPercentage(column, percentage)
      : PickBackColorForOrder(column, order, percentage, !InvertColorPalette(column));
  }

  public Brush PickBackColorForPercentage(double percentage) {
    return PickBackColorForPercentage(null, percentage);
  }

  private Brush PickBackColorForPercentage(OptionalColumn column, double percentage) {
    if (percentage < ElementWeightCutoff) {
      return Brushes.Transparent;
    }

    var palette = PickColorPalette(column);
    return palette.PickBrushForPercentage(percentage, InvertColorPalette(column));
  }

  private Brush PickBackColorForOrder(OptionalColumn column, int order, double percentage, bool inverted) {
    if (!IsSignificantValue(order, percentage)) {
      return Brushes.Transparent;
    }

    var palette = PickColorPalette(column);
    return palette.PickBrush(order, inverted);
  }

  public Brush PickDefaultBackColor(OptionalColumn column) {
    if (!column.Style.BackgroundColor.IsTransparent()) {
      return column.Style.BackgroundColor.AsBrush();
    }

    if (column.PerformanceCounter != null) {
      return column.IsPerformanceCounter ?
        PerformanceCounterBackColor.AsBrush() :
        PerformanceMetricBackColor.AsBrush();
    }

    return Brushes.Transparent;
  }

  public bool IsSignificantValue(int order, double percentage) {
    return order < TopOrderCutoff && percentage >= IconBarWeightCutoff;
  }

  public bool IsVisibleValue(int order, double percentage) {
    return order < TopOrderCutoff || percentage >= ElementWeightCutoff;
  }

  public Brush PickBackColorForOrder(int order, double percentage, bool inverted) {
    return PickBackColorForOrder(null, order, percentage, inverted);
  }

  public Brush PickTextColor(OptionalColumn column, int order, double percentage) {
    return !column.Style.TextColor.IsTransparent() ?
      ColorBrushes.GetBrush(column.Style.TextColor) : ColumnTextColor.AsBrush();
  }

  public FontWeight PickTextWeight(OptionalColumn column, int order, double percentage) {
    if (column.Style.PickColorForPercentage &&
        !ShouldOverridePercentage(order, percentage)) {
      return percentage switch {
        >= 0.9 => FontWeights.Bold,
        >= 0.7 => FontWeights.Medium,
        >= 0.5 => FontWeights.SemiBold,
        _ => FontWeights.Normal
      };
    }

    return order switch {
      0 => FontWeights.Bold,
      1 => FontWeights.Medium,
      _ => IsSignificantValue(order, percentage)
        ? FontWeights.SemiBold
        : FontWeights.Normal
    };
  }

  public FontWeight PickTextWeight(double percentage) {
    return percentage switch {
      >= 0.9 => FontWeights.Bold,
      >= 0.75 => FontWeights.Medium,
      _ => FontWeights.Normal
    };
  }

  public Brush PickBrushForPercentage(double weightPercentage) {
    return PickBackColorForPercentage(null, weightPercentage);
  }

  //? TODO: Cache IconDrawing between calls
  public IconDrawing PickIcon(OptionalColumn column, int order, double percentage) {
    if (!ShouldShowIcon(column)) {
      return IconDrawing.Empty;
    }

    return column.Style.PickColorForPercentage &&
           !ShouldOverrideIconPercentage(order, percentage)
      ? PickIconForPercentage(percentage)
      : PickIconForOrder(order, percentage);
  }

  //? TODO:
  //? private void PreloadIcons() {
  //? }

  public IconDrawing PickIconForOrder(int order, double percentage) {
    return order switch {
      0 => IconDrawing.FromIconResource("HotFlameIcon1"),
      1 => IconDrawing.FromIconResource("HotFlameIcon2"),
      // Even if instr is the n-th hottest one, don't use an icon
      // if the percentage is small.
      _ => IsSignificantValue(order, percentage) ?
        IconDrawing.FromIconResource("HotFlameIcon3") :
        IconDrawing.FromIconResource("HotFlameIconTransparent")
    };
  }

  public IconDrawing PickIconForPercentage(double percentage) {
    return percentage switch {
      >= 0.9 => IconDrawing.FromIconResource("HotFlameIcon1"),
      >= 0.7 => IconDrawing.FromIconResource("HotFlameIcon2"),
      >= 0.5 => IconDrawing.FromIconResource("HotFlameIcon3"),
      _ => IconDrawing.FromIconResource("HotFlameIconTransparent")
    };
  }

  public bool ShowPercentageBar(OptionalColumn column, int order, double percentage) {
    if (!ShouldShowPercentageBar(column)) {
      return false;
    }

    // Don't use a bar if it ends up only a few pixels.
    return percentage >= IconBarWeightCutoff;
  }

  public bool ShowPercentageBar(double percentage) {
    if (!DisplayPercentageBar) {
      return false;
    }

    // Don't use a bar if it ends up only a few pixels.
    return percentage >= IconBarWeightCutoff;
  }

  public Brush PickPercentageBarColor(OptionalColumn column) {
    return !column.Style.PercentageBarBackColor.IsTransparent() ?
      ColorBrushes.GetBrush(column.Style.PercentageBarBackColor) :
      PercentageBarBackColor.AsBrush();
  }

  private bool ShouldShowPercentageBar(OptionalColumn column) {
    return DisplayPercentageBar &&
           (column.Style.ShowPercentageBar == OptionalColumnStyle.PartVisibility.Always ||
            (column.IsMainColumn &&
             column.Style.ShowPercentageBar == OptionalColumnStyle.PartVisibility.IfActiveColumn));
  }

  private bool ShouldShowIcon(OptionalColumn column) {
    return DisplayIcons && (column.Style.ShowIcon == OptionalColumnStyle.PartVisibility.Always ||
                            (column.IsMainColumn &&
                            column.Style.ShowIcon == OptionalColumnStyle.PartVisibility.IfActiveColumn));
  }

  private bool ShouldUseBackColor(OptionalColumn column) {
    return column.Style.UseBackColor == OptionalColumnStyle.PartVisibility.Always ||
           (column.IsMainColumn &&
            column.Style.UseBackColor == OptionalColumnStyle.PartVisibility.IfActiveColumn);
  }

  public Brush PickColorForPercentage(double percentage) {
    return PickBackColorForPercentage(null, percentage);
  }

  private ColorPalette PickColorPalette(OptionalColumn column) {
    return !string.IsNullOrEmpty(column?.Style?.BackColorPalette) ?
      ColorPalette.GetPalette(column.Style.BackColorPalette) :
      defaultBackColorPalette_;
  }

  private bool InvertColorPalette(OptionalColumn column) {
    return column != null && column.Style.InvertColorPalette;
  }

  private bool ShouldOverridePercentage(int order, double percentage) {
    // Hottest elements still handled by order.
    return order == 0 ||
           order < 3 && percentage > 0.2; // 20%
  }

  private bool ShouldOverrideIconPercentage(int order, double percentage) {
    // Hottest elements still handled by order.
    return order == 0 ||
           order < 3 && percentage > 0.05; // 5%
  }

  public ProfileDocumentMarkerSettings Clone() {
    byte[] serialized = StateSerializer.Serialize(this);
    return StateSerializer.Deserialize<ProfileDocumentMarkerSettings>(serialized);
  }

  public override bool Equals(object obj) {
    return obj is ProfileDocumentMarkerSettings other &&
           MarkElements == other.MarkElements &&
           MarkBlocks == other.MarkBlocks &&
           MarkBlocksInFlowGraph == other.MarkBlocksInFlowGraph &&
           MarkCallTargets == other.MarkCallTargets &&
           JumpToHottestElement == other.JumpToHottestElement &&
           ElementWeightCutoff.Equals(other.ElementWeightCutoff) &&
           TopOrderCutoff == other.TopOrderCutoff &&
           IconBarWeightCutoff.Equals(other.IconBarWeightCutoff) &&
           ColumnTextColor == other.ColumnTextColor &&
           BlockOverlayTextColor == other.BlockOverlayTextColor &&
           HotBlockOverlayTextColor == other.HotBlockOverlayTextColor &&
           BlockOverlayBorderColor == other.BlockOverlayBorderColor &&
           BlockOverlayBorderThickness.Equals(other.BlockOverlayBorderThickness) &&
           PercentageBarBackColor == other.PercentageBarBackColor &&
           MaxPercentageBarWidth == other.MaxPercentageBarWidth &&
           DisplayPercentageBar == other.DisplayPercentageBar &&
           DisplayIcons == other.DisplayIcons &&
           ValueUnit == other.ValueUnit &&
           AppendValueUnitSuffix == other.AppendValueUnitSuffix &&
           ValueUnitDecimals == other.ValueUnitDecimals &&
           PerformanceMetricBackColor == other.PerformanceMetricBackColor &&
           PerformanceCounterBackColor == other.PerformanceCounterBackColor;
  }

  public override string ToString() {
    return $"MarkElements: {MarkElements}\n" +
           $"MarkBlocks: {MarkBlocks}\n" +
           $"MarkBlocksInFlowGraph: {MarkBlocksInFlowGraph}\n" +
           $"MarkCallTargets: {MarkCallTargets}\n" +
           $"JumpToHottestElement: {JumpToHottestElement}\n" +
           $"ElementWeightCutoff: {ElementWeightCutoff}\n" +
           $"TopOrderCutoff: {TopOrderCutoff}\n" +
           $"IconBarWeightCutoff: {IconBarWeightCutoff}\n" +
           $"ColumnTextColor: {ColumnTextColor}\n" +
           $"BlockOverlayTextColor: {BlockOverlayTextColor}\n" +
           $"HotBlockOverlayTextColor: {HotBlockOverlayTextColor}\n" +
           $"BlockOverlayBorderColor: {BlockOverlayBorderColor}\n" +
           $"BlockOverlayBorderThickness: {BlockOverlayBorderThickness}\n" +
           $"PercentageBarBackColor: {PercentageBarBackColor}\n" +
           $"MaxPercentageBarWidth: {MaxPercentageBarWidth}\n" +
           $"DisplayPercentageBar: {DisplayPercentageBar}\n" +
           $"DisplayIcons: {DisplayIcons}\n" +
           $"ValueUnit: {ValueUnit}\n" +
           $"AppendValueUnitSuffix: {AppendValueUnitSuffix}\n" +
           $"ValueUnitDecimals: {ValueUnitDecimals}\n" +
           $"PerformanceMetricBackColor: {PerformanceMetricBackColor}\n" +
           $"PerformanceCounterBackColor: {PerformanceCounterBackColor}";
  }
}