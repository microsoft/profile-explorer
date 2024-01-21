// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
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
    Millisecond,
    Second,
    Percent,
    Value
  }

  [ProtoMember(1)] public bool MarkElements { get; set; }
  [ProtoMember(2)] public bool MarkBlocks { get; set; }
  [ProtoMember(3)] public bool MarkBlocksInFlowGraph { get; set; }
  [ProtoMember(4)] public bool MarkCallTargets { get; set; }
  [ProtoMember(5)] public bool JumpToHottestElement { get; set; }
  [ProtoMember(6)] public double VirtualColumnPosition { get; set; }
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
  [ProtoMember(20)]  public bool RemoveEmptyColumns { get; set; }
  [ProtoMember(21)]  public bool ShowPerformanceCounterColumns { get; set; }
  [ProtoMember(22)]  public bool ShowPerformanceMetricColumns { get; set; }
  [ProtoMember(23)]  public ValueUnitKind ValueUnit { get; set; }

  public override void Reset() {
    MarkElements = true;
    MarkBlocks = true;
    MarkBlocksInFlowGraph = true;
    MarkCallTargets = true;
    ValueUnit = ValueUnitKind.Millisecond;
    VirtualColumnPosition = 350;
    ElementWeightCutoff = 0.003; // 0.3%
    TopOrderCutoff = 10;
    IconBarWeightCutoff = 0.03; // 3%
    MaxPercentageBarWidth = 50;
    DisplayIcons = true;
    RemoveEmptyColumns = true;
    DisplayPercentageBar = true;
    ShowPerformanceCounterColumns = true;
    ShowPerformanceMetricColumns = true;
    ColumnTextColor = Colors.Black;
    BlockOverlayTextColor = Colors.DarkBlue;
    HotBlockOverlayTextColor = Colors.DarkRed;
    BlockOverlayBorderColor = Colors.DimGray;
    BlockOverlayBorderThickness = 1;
    PercentageBarBackColor = Utils.ColorFromString("#Aa4343");
  }

  public Color PickBackColor(OptionalColumn column, int order, double percentage) {
    if (!ShouldUseBackColor(column)) {
      return Colors.Transparent;
    }

    //? TODO: ShouldUsePalette, ColorPalette in Appearance
    return column.Style.PickColorForPercentage &&
           !ShouldOverridePercentage(order, percentage)
      ? PickBackColorForPercentage(column, percentage)
      : PickBackColorForOrder(column, order, percentage, !InvertColorPalette(column));
  }

  public Color PickBackColorForPercentage(double percentage) {
    return PickBackColorForPercentage(null, percentage);
  }

  private Color PickBackColorForPercentage(OptionalColumn column, double percentage) {
    if (percentage < ElementWeightCutoff) {
      return Colors.Transparent;
    }

    var palette = PickColorPalette(column);
    return palette.PickColorForPercentage(percentage, InvertColorPalette(column));
  }

  private Color PickBackColorForOrder(OptionalColumn column, int order, double percentage, bool inverted) {
    if (!IsSignificantValue(order, percentage)) {
      return Colors.Transparent;
    }

    var palette = PickColorPalette(column);
    return palette.PickColor(order, inverted);
  }

  public bool IsSignificantValue(int order, double percentage) {
    return order < TopOrderCutoff && percentage >= IconBarWeightCutoff;
  }

  public Color PickBackColorForOrder(int order, double percentage, bool inverted) {
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
    return PickBackColorForPercentage(null, weightPercentage).AsBrush();
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

  private IconDrawing PickIconForPercentage(double percentage) {
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
           (column.Style.ShowPercentageBar ||
            column.IsMainColumn && column.Style.ShowMainColumnPercentageBar);
  }

  private bool ShouldShowIcon(OptionalColumn column) {
    return DisplayIcons && (column.Style.ShowIcon ||
                            column.IsMainColumn && column.Style.ShowMainColumnIcon);
  }

  private bool ShouldUseBackColor(OptionalColumn column) {
    return column.Style.UseBackColor ||
           column.IsMainColumn && column.Style.UseMainColumnBackColor;
  }

  public Color PickColorForPercentage(double percentage) {
    return PickBackColorForPercentage(null, percentage);
  }

  private ColorPalette PickColorPalette(OptionalColumn column) {
    return column?.Style?.BackColorPalette ?? defaultBackColorPalette_;
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

  protected bool Equals(ProfileDocumentMarkerSettings other) {
    return MarkElements == other.MarkElements &&
           MarkBlocks == other.MarkBlocks &&
           MarkBlocksInFlowGraph == other.MarkBlocksInFlowGraph &&
           MarkCallTargets == other.MarkCallTargets &&
           JumpToHottestElement == other.JumpToHottestElement &&
           VirtualColumnPosition.Equals(other.VirtualColumnPosition) &&
           ElementWeightCutoff.Equals(other.ElementWeightCutoff) &&
           TopOrderCutoff == other.TopOrderCutoff && 
           IconBarWeightCutoff.Equals(other.IconBarWeightCutoff) &&
           Equals(ColumnTextColor, other.ColumnTextColor) &&
           Equals(BlockOverlayTextColor, other.BlockOverlayTextColor) &&
           Equals(HotBlockOverlayTextColor, other.HotBlockOverlayTextColor) &&
           Equals(BlockOverlayBorderColor, other.BlockOverlayBorderColor) &&
           BlockOverlayBorderThickness.Equals(other.BlockOverlayBorderThickness) &&
           Equals(PercentageBarBackColor, other.PercentageBarBackColor) &&
           MaxPercentageBarWidth == other.MaxPercentageBarWidth &&
           DisplayPercentageBar == other.DisplayPercentageBar &&
           DisplayIcons == other.DisplayIcons &&
           RemoveEmptyColumns == other.RemoveEmptyColumns &&
           ShowPerformanceCounterColumns == other.ShowPerformanceCounterColumns &&
           ShowPerformanceMetricColumns == other.ShowPerformanceMetricColumns &&
           ValueUnit == other.ValueUnit;
  }
}