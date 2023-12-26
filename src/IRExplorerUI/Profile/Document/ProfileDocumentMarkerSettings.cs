// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System.Windows;
using System.Windows.Media;

namespace IRExplorerUI.Profile;

public class ProfileDocumentMarkerSettings {
  private static ProfileDocumentMarkerSettings defaultInstance_;
  private static ColorPalette defaultBackColorPalette_ = ColorPalette.Profile;

  static ProfileDocumentMarkerSettings() {
    defaultInstance_ = new ProfileDocumentMarkerSettings {
      ColumnSettings = App.Settings.ColumnSettings,
      VirtualColumnPosition = 350,
      ElementWeightCutoff = 0.003, // 0.3%
      LineWeightCutoff = 0.005, // 0.5%,
      TopOrderCutoff = 10,
      IconBarWeightCutoff = 0.03,
      MaxPercentageBarWidth = 50,
      DisplayIcons = true,
      RemoveEmptyColumns = true,
      DisplayPercentageBar = true,
      ColumnTextColor = Brushes.Black,
      ElementOverlayTextColor = Brushes.DimGray,
      HotElementOverlayTextColor = Brushes.DarkRed,
      ElementOverlayBackColor = Brushes.Transparent,
      HotElementOverlayBackColor = Brushes.AntiqueWhite,
      BlockOverlayTextColor = Brushes.DarkBlue,
      HotBlockOverlayTextColor = Brushes.DarkRed,
      BlockOverlayBackColor = Brushes.AliceBlue,
      BlockOverlayBorderColor = Brushes.DimGray,
      BlockOverlayBorderThickness = 1,
      HotBlockOverlayBackColor = Brushes.AntiqueWhite,
      InlineeOverlayTextColor = Brushes.Green,
      InlineeOverlayBackColor = Brushes.Transparent,
      PercentageBarBackColor = Utils.ColorFromString("#Aa4343").AsBrush()
    };
  }

  public enum ValueUnitKind {
    Nanosecond,
    Millisecond,
    Second,
    Percent,
    Value
  }

  public static ProfileDocumentMarkerSettings Default => defaultInstance_;
  public OptionalColumnSettings ColumnSettings { get; set; }
  public double VirtualColumnPosition { get; set; }
  public double ElementWeightCutoff { get; set; }
  public double LineWeightCutoff { get; set; }
  public int TopOrderCutoff { get; set; }
  public double IconBarWeightCutoff { get; set; }
  public Brush ColumnTextColor { get; set; }
  public Brush ElementOverlayTextColor { get; set; }
  public Brush HotElementOverlayTextColor { get; set; }
  public Brush InlineeOverlayTextColor { get; set; }
  public Brush BlockOverlayTextColor { get; set; }
  public Brush HotBlockOverlayTextColor { get; set; }
  public Brush InlineeOverlayBackColor { get; set; }
  public Brush ElementOverlayBackColor { get; set; }
  public Brush HotElementOverlayBackColor { get; set; }
  public Brush BlockOverlayBackColor { get; set; }
  public Brush HotBlockOverlayBackColor { get; set; }
  public Brush BlockOverlayBorderColor { get; set; }
  public double BlockOverlayBorderThickness { get; set; }
  public Brush PercentageBarBackColor { get; set; }
  public int MaxPercentageBarWidth { get; set; }
  public bool DisplayPercentageBar { get; set; }
  public bool DisplayIcons { get; set; }
  public bool RemoveEmptyColumns { get; set; }
  public ValueUnitKind ValueUnit { get; set; }

  public Color PickBackColor(OptionalColumn column, int colorIndex, double percentage) {
    if (!ShouldUseBackColor(column)) {
      return Colors.Transparent;
    }

    //? TODO: ShouldUsePalette, ColorPalette in Appearance
    return column.Style.PickColorForPercentage
      ? PickBackColorForPercentage(column, percentage)
      : PickBackColorForOrder(column, colorIndex, percentage, InvertColorPalette(column));
  }

  public Color PickBackColorForPercentage(double percentage) {
    return PickBackColorForPercentage(null, percentage);
  }

  public Color PickBackColorForPercentage(OptionalColumn column, double percentage) {
    if (percentage < ElementWeightCutoff) {
      return Colors.Transparent;
    }

    var palette = PickColorPalette(column);
    return palette.PickColorForPercentage(percentage, InvertColorPalette(column));
  }

  public Color PickBackColorForOrder(OptionalColumn column, int order, double percentage, bool inverted) {
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
      ColorBrushes.GetBrush(column.Style.TextColor) : ColumnTextColor;
  }

  public FontWeight PickTextWeight(OptionalColumn column, int order, double percentage) {
    if (column.Style.PickColorForPercentage) {
      return percentage switch {
        >= 0.9 => FontWeights.Bold,
        >= 0.75 => FontWeights.Medium,
        _ => FontWeights.Normal
      };
    }

    return order switch {
      0 => FontWeights.ExtraBold,
      1 => FontWeights.Bold,
      _ => IsSignificantValue(order, percentage)
        ? FontWeights.Medium
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

    //? TODO: Option
    // if (column.Style.PickColorForPercentage) {
    //   return PickIconForPercentage(percentage);
    // }

    return PickIconForOrder(order, percentage);
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

  public IconDrawing PickIconForPercentage(double percentage) {
    return percentage switch {
      >= 0.9 => IconDrawing.FromIconResource("HotFlameIcon1"),
      >= 0.75 => IconDrawing.FromIconResource("HotFlameIcon2"),
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
      PercentageBarBackColor;
  }

  public bool ShouldShowPercentageBar(OptionalColumn column) {
    return DisplayPercentageBar &&
           (column.Style.ShowPercentageBar ||
            column.IsMainColumn && column.Style.ShowMainColumnPercentageBar);
  }

  public bool ShouldShowIcon(OptionalColumn column) {
    return DisplayIcons && (column.Style.ShowIcon ||
                            column.IsMainColumn && column.Style.ShowMainColumnIcon);
  }

  public bool ShouldUseBackColor(OptionalColumn column) {
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
}
