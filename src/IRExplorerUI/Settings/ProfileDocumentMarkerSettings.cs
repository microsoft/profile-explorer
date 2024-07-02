// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
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
  [ProtoMember(1)][OptionValue(true)]
  public bool MarkElements { get; set; }
  [ProtoMember(2)][OptionValue(true)]
  public bool MarkBlocks { get; set; }
  [ProtoMember(3)][OptionValue(true)]
  public bool MarkBlocksInFlowGraph { get; set; }
  [ProtoMember(4)][OptionValue(true)]
  public bool MarkCallTargets { get; set; }
  [ProtoMember(5)][OptionValue(true)]
  public bool JumpToHottestElement { get; set; }
  [ProtoMember(7)][OptionValue(0.01)] // 1%
  public double ElementWeightCutoff { get; set; }
  [ProtoMember(8)][OptionValue(10)]
  public int TopOrderCutoff { get; set; }
  [ProtoMember(9)][OptionValue(0.01)] // 1%
  public double IconBarWeightCutoff { get; set; }
  [ProtoMember(10)][OptionValue("#000000")]
  public Color ColumnTextColor { get; set; }
  [ProtoMember(11)][OptionValue("#00008B")]
  public Color BlockOverlayTextColor { get; set; }
  [ProtoMember(12)][OptionValue("#8B0000")]
  public Color HotBlockOverlayTextColor { get; set; }
  [ProtoMember(14)][OptionValue("#696969")]
  public Color BlockOverlayBorderColor { get; set; }
  [ProtoMember(15)][OptionValue(1)]
  public double BlockOverlayBorderThickness { get; set; }
  [ProtoMember(16)][OptionValue("#AA4343")]
  public Color PercentageBarBackColor { get; set; }
  [ProtoMember(17)][OptionValue(50)]
  public int MaxPercentageBarWidth { get; set; }
  [ProtoMember(18)][OptionValue(true)]
  public bool DisplayPercentageBar { get; set; }
  [ProtoMember(19)][OptionValue(true)]
  public bool DisplayIcons { get; set; }
  [ProtoMember(20)][OptionValue(ValueUnitKind.Millisecond)]
  public ValueUnitKind ValueUnit { get; set; }
  [ProtoMember(21)][OptionValue(true)]
  public bool AppendValueUnitSuffix { get; set; }
  [ProtoMember(22)][OptionValue(2)]
  public int ValueUnitDecimals { get; set; }
  [ProtoMember(23)][OptionValue("#FAEBD7")]
  public Color PerformanceMetricBackColor { get; set; }
  [ProtoMember(24)][OptionValue("#F5F5F5")]
  public Color PerformanceCounterBackColor { get; set; }
  public static int DefaultMaxPercentageBarWidth = 50;
  public static double DefaultElementWeightCutoff = 0.01; // 1%;
  private static IconDrawing[] orderIcons_;

  public override void Reset() {
    ResetAllOptions(this);
  }

  public string FormatWeightValue(TimeSpan weight) {
    string suffix = "";

    if (AppendValueUnitSuffix) {
      suffix = $" {ValueUnitSuffix}";
    }

    return ValueUnit switch {
      ValueUnitKind.Millisecond => weight.AsMillisecondsString(ValueUnitDecimals, suffix),
      ValueUnitKind.Microsecond => weight.AsMicrosecondString(ValueUnitDecimals, suffix),
      ValueUnitKind.Nanosecond  => weight.AsNanosecondsString(ValueUnitDecimals, suffix),
      ValueUnitKind.Second      => weight.AsSecondsString(ValueUnitDecimals, suffix),
      _                         => weight.Ticks.ToString()
    };
  }

  public string ValueUnitSuffix => ValueUnit switch {
    ValueUnitKind.Millisecond => "ms",
    ValueUnitKind.Microsecond => "µs",
    ValueUnitKind.Nanosecond  => "ns",
    ValueUnitKind.Second      => "s",
    _                         => ""
  };

  public Brush PickBackColor(OptionalColumn column, int order, double percentage) {
    if (!ShouldUseBackColor(column)) {
      return ColorBrushes.Transparent;
    }

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
      return ColorBrushes.Transparent;
    }

    var palette = PickColorPalette(column);
    return palette.PickBrushForPercentage(percentage, InvertColorPalette(column));
  }

  private Brush PickBackColorForOrder(OptionalColumn column, int order, double percentage, bool inverted) {
    if (!IsSignificantValue(order, percentage)) {
      return ColorBrushes.Transparent;
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

    return ColorBrushes.Transparent;
  }

  public bool IsSignificantValue(int order, double percentage) {
    return order < TopOrderCutoff && percentage >= IconBarWeightCutoff;
  }

  public bool IsVisibleValue(int order, double percentage) {
    return order < TopOrderCutoff && percentage > double.Epsilon ||
           percentage >= ElementWeightCutoff;
  }

  public bool IsVisibleValue(double percentage, double scale = 1.0) {
    return percentage >= ElementWeightCutoff * scale;
  }

  public Brush PickBackColorForOrder(int order, double percentage, bool inverted) {
    return PickBackColorForOrder(null, order, percentage, inverted);
  }

  public (Brush, FontWeight) PickBlockOverlayStyle(int order, double percentage) {
    return IsSignificantValue(order, percentage)
      ? (HotBlockOverlayTextColor.AsBrush(), FontWeights.Bold)
      : (BlockOverlayTextColor.AsBrush(), FontWeights.Normal);
  }

  public (Brush, FontWeight) PickBlockOverlayStyle(double percentage) {
    return PickBlockOverlayStyle(int.MaxValue, percentage);
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
        _      => FontWeights.Normal
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
      >= 0.9  => FontWeights.Bold,
      >= 0.75 => FontWeights.Medium,
      _       => FontWeights.Normal
    };
  }

  public Brush PickBrushForPercentage(double weightPercentage) {
    return PickBackColorForPercentage(null, weightPercentage);
  }

  public IconDrawing PickIcon(OptionalColumn column, int order, double percentage) {
    if (!ShouldShowIcon(column)) {
      return IconDrawing.Empty;
    }

    return column.Style.PickColorForPercentage &&
           !ShouldOverrideIconPercentage(order, percentage)
      ? PickIconForPercentage(percentage)
      : PickIconForOrder(order, percentage);
  }

  static ProfileDocumentMarkerSettings() {
    // Preload icons used to mark hot elements.
    orderIcons_ = [
      IconDrawing.FromIconResource("HotFlameIcon1"),
      IconDrawing.FromIconResource("HotFlameIcon2"),
      IconDrawing.FromIconResource("HotFlameIcon3"),
      IconDrawing.FromIconResource("HotFlameIconTransparent")
    ];
  }

  public IconDrawing PickIconForOrder(int order, double percentage) {
    return order switch {
      0 => orderIcons_[0],
      1 => orderIcons_[1],
      // Even if instr is the n-th hottest one, don't use an icon
      // if the percentage is small.
      _ => IsSignificantValue(order, percentage) ? orderIcons_[2] : orderIcons_[3]
    };
  }

  public IconDrawing PickIconForPercentage(double percentage) {
    return percentage switch {
      >= 0.9 => orderIcons_[0],
      >= 0.7 => orderIcons_[1],
      >= 0.5 => orderIcons_[2],
      _      => orderIcons_[3]
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
            column.IsMainColumn &&
            column.Style.ShowPercentageBar == OptionalColumnStyle.PartVisibility.IfActiveColumn);
  }

  private bool ShouldShowIcon(OptionalColumn column) {
    return DisplayIcons && (column.Style.ShowIcon == OptionalColumnStyle.PartVisibility.Always ||
                            column.IsMainColumn &&
                            column.Style.ShowIcon == OptionalColumnStyle.PartVisibility.IfActiveColumn);
  }

  private bool ShouldUseBackColor(OptionalColumn column) {
    return column.Style.UseBackColor == OptionalColumnStyle.PartVisibility.Always ||
           column.IsMainColumn &&
           column.Style.UseBackColor == OptionalColumnStyle.PartVisibility.IfActiveColumn;
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
    return AreOptionsEqual(this, obj);
  }

  public override string ToString() {
    return PrintOptions(this);
  }
}