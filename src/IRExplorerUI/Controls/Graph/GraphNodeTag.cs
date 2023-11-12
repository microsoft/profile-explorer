﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Windows.Media;
using IRExplorerCore.IR;

namespace IRExplorerUI;

public sealed class GraphNodeTag : ITag {
  public static readonly Color[] HeatmapColors = {
    Utils.ColorFromString("#63BE7B"),
    Utils.ColorFromString("#85C77D"),
    Utils.ColorFromString("#A8D280"),
    Utils.ColorFromString("#CCDD81"),
    Utils.ColorFromString("#EEE683"),
    Utils.ColorFromString("#FFDD83"),
    Utils.ColorFromString("#FCBF7C"),
    Utils.ColorFromString("#FCA377"),
    Utils.ColorFromString("#F58874"),
    Utils.ColorFromString("#F8696B")
  };
  public static readonly Color[] HeatmapColors2 = {
    Utils.ColorFromString("#598AC5"),
    Utils.ColorFromString("#7EA2D2"),
    Utils.ColorFromString("#A2BCDF"),
    Utils.ColorFromString("#C6D6ED"),
    Utils.ColorFromString("#EBEFF8"),
    Utils.ColorFromString("#FBECEF"),
    Utils.ColorFromString("#F9CDCC"),
    Utils.ColorFromString("#FAABAE"),
    Utils.ColorFromString("#F88A8B"),
    Utils.ColorFromString("#F8696B")
  };

  public enum LabelPlacementKind {
    Top,
    Bottom,
    Left,
    Right
  }

  public Color? BackgroundColor { get; set; }
  public Color? BorderColor { get; set; }
  public double BorderThickness { get; set; }
  public string Label { get; set; }
  public string ToolTip { get; set; }
  public LabelPlacementKind LabelPlacement { get; set; }
  public Color? LabelFontColor { get; set; }
  public string Name => "Graph Node Tag";
  public TaggedObject Owner { get; set; }

  public static GraphNodeTag MakeLabel(string label, string tooltip = null, Color? fontColor = null,
                                       LabelPlacementKind position = LabelPlacementKind.Bottom) {
    return new GraphNodeTag {
      Label = label,
      ToolTip = tooltip,
      LabelFontColor = fontColor,
      LabelPlacement = position
    };
  }

  public static GraphNodeTag MakeColor(string label, Color backColor, Color? fontColor = null,
                                       LabelPlacementKind position = LabelPlacementKind.Bottom) {
    return new GraphNodeTag {
      Label = label,
      BackgroundColor = backColor,
      LabelFontColor = fontColor,
      LabelPlacement = position
    };
  }

  public static GraphNodeTag MakeHeatMap(long value, long maxValue) {
    return new GraphNodeTag {
      BackgroundColor = GetHeatmapColor(value, maxValue)
    };
  }

  public static GraphNodeTag MakeHeatMap2(long value, long maxValue) {
    return new GraphNodeTag {
      BackgroundColor = GetHeatmapColor2(value, maxValue)
    };
  }

  public static Color GetHeatmapColor(long value, long maxValue) {
    return GetScaleColor(value, maxValue, HeatmapColors);
  }

  public static Color GetHeatmapColor2(long value, long maxValue) {
    return GetScaleColor(value, maxValue, HeatmapColors2);
  }

  public static Color GetScaleColor(long value, long maxValue, Color[] palette) {
    int index = (int)Math.Round((double)value * (palette.Length - 1) / maxValue);
    return palette[index];
  }
}
