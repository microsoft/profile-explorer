// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media;

namespace ProfileExplorer.UI;

public static class ColorPens {
  private static readonly double BoldPenThickness = 1.25;
  private static ThreadLocal<Dictionary<Tuple<Color, double>, Pen>> pens_ =
    new(() => {
      return new Dictionary<Tuple<Color, double>, Pen>();
    });
  private static ThreadLocal<Dictionary<Tuple<Color, double, DashStyle>, Pen>> dashedPens_ =
    new(() => {
      return new Dictionary<Tuple<Color, double, DashStyle>, Pen>();
    });

  public static Pen GetPen(Color color, double thickness = 1.0) {
    var pair = new Tuple<Color, double>(color, thickness);

    if (pens_.Value.TryGetValue(pair, out var pen)) {
      return pen;
    }

    pen = CreatePen(color, thickness);
    pens_.Value.Add(pair, pen);
    return pen;
  }

  public static Pen GetPen(Brush brush, double thickness = 1.0) {
    if (brush is SolidColorBrush colorBrush) {
      return GetPen(colorBrush.Color, thickness);
    }

    return null;
  }

  public static Pen GetPen(string color, double thickness = 1.0) {
    return GetPen(Utils.ColorFromString(color), thickness);
  }

  public static Pen GetBoldPen(Color color) {
    return GetPen(color, BoldPenThickness);
  }

  public static Pen GetBoldPen(Pen templatePen) {
    var brush = (SolidColorBrush)templatePen.Brush;
    return GetPen(brush.Color, BoldPenThickness);
  }

  public static Pen GetBoldPen(string color) {
    return GetPen(Utils.ColorFromString(color), BoldPenThickness);
  }

  public static Pen GetDashedPen(Color color, DashStyle dashStyle, double thickness = 1.0) {
    var pair = new Tuple<Color, double, DashStyle>(color, thickness, dashStyle);

    if (dashedPens_.Value.TryGetValue(pair, out var pen)) {
      return pen;
    }

    pen = CreateDashedPen(color, dashStyle, thickness);
    dashedPens_.Value.Add(pair, pen);
    return pen;
  }

  public static Pen GetTransparentPen(Color baseColor, byte alpha, double thickness = 1.0) {
    return GetPen(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B), thickness);
  }

  private static Pen CreatePen(Color color, double thickness) {
    Pen pen;
    var brush = ColorBrushes.GetBrush(color);
    pen = new Pen(brush, thickness);

    if (brush.CanFreeze) {
      brush.Freeze();
    }

    if (pen.CanFreeze) {
      pen.Freeze();
    }

    return pen;
  }

  private static Pen CreateDashedPen(Color color, DashStyle dashStyle, double thickness) {
    Pen pen;
    var brush = ColorBrushes.GetBrush(color);

    pen = new Pen(brush, thickness) {
      DashStyle = dashStyle
    };

    if (brush.CanFreeze) {
      brush.Freeze();
    }

    if (pen.CanFreeze) {
      pen.Freeze();
    }

    return pen;
  }
}