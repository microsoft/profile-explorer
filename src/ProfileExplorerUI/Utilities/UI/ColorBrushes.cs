// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media;

namespace ProfileExplorer.UI;

public static class ColorBrushes {
  private static readonly ThreadLocal<Dictionary<Color, SolidColorBrush>> brushes_ =
    new(() => new Dictionary<Color, SolidColorBrush>());

  // Cache transparent brush, since using it from Brushes
  // does a dictionary lookup.
  public static readonly Brush Transparent = Brushes.Transparent;

  public static SolidColorBrush GetBrush(string colorName) {
    return GetBrush(Utils.ColorFromString(colorName));
  }

  public static SolidColorBrush GetBrush(Color color) {
    if (brushes_.Value.TryGetValue(color, out var brush)) {
      return brush;
    }

    brush = new SolidColorBrush(color);
    brush.Freeze();
    brushes_.Value.Add(color, brush);
    return brush;
  }

  public static SolidColorBrush GetTransparentBrush(Color baseColor, byte alpha) {
    return GetBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
  }

  public static SolidColorBrush GetTransparentBrush(Color baseColor, double opacity) {
    byte alpha = Math.Clamp((byte)(opacity * 255), (byte)0, (byte)255);
    return GetBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
  }

  public static SolidColorBrush GetTransparentBrush(string baseColor, byte alpha) {
    return GetTransparentBrush(Utils.ColorFromString(baseColor), alpha);
  }

  public static SolidColorBrush GetTransparentBrush(string baseColor, double opacity) {
    return GetTransparentBrush(Utils.ColorFromString(baseColor), opacity);
  }
}