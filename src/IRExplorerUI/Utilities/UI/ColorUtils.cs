// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace IRExplorerUI;

class ColorUtils {
  public static readonly string[] PastelColors = {
    "#f4a3a3",
    "#fbc29c",
    "#dabdce",
    "#b6c9e1",
    "#ebacc2",
    "#ebacd6",
    "#fcb89b",
    "#dbbcbe",
    "#fbd397",
    "#fcfc9b",
    "#cbeea3",
    "#abe6b1",
    "#d3d5c2",
    "#b9d9d7",
    "#b1d0dc",
    "#b1e6c2",
    "#daedaa",
    "#acbeeb",
    "#dcdab6",
    "#b5b5ed",
    "#d3b8df",
    "#ceb9de",
    "#eed3af"
  };
  public static readonly string[] LightPastelColors = {
    "#fed7d7",
    "#fee6d6",
    "#f0e4eb",
    "#e1e9f3",
    "#f7dde6",
    "#f7ddee",
    "#fee2d6",
    "#f1e4e5",
    "#fee2d6",
    "#fefed6",
    "#e8f9db",
    "#dff6df",
    "#edeee6",
    "#e4f0f1",
    "#e6eeee",
    "#dff5e6",
    "#f0f8dc",
    "#dde4f7",
    "#f1f0e3",
    "#dde6f7",
    "#e4e2f2",
    "#ede2f2",
    "#f8eddc"
  };

  private static Color[] cachedLightPastelColors_;
  private static Color[] cachedPastelColors_;
  private static Brush[] cachedPastelBrushes_;
  private static Brush[] cachedLightPastelBrushes_;

  static ColorUtils() {
    cachedLightPastelColors_ = new Color[LightPastelColors.Length];
    cachedPastelColors_ = new Color[PastelColors.Length];
    cachedLightPastelBrushes_ = new Brush[LightPastelColors.Length];
    cachedPastelBrushes_ = new Brush[PastelColors.Length];

    for (int i = 0; i < LightPastelColors.Length; i++) {
      cachedLightPastelColors_[i] = Utils.ColorFromString(LightPastelColors[i]);
    }

    for (int i = 0; i < PastelColors.Length; i++) {
      cachedPastelColors_[i] = Utils.ColorFromString(PastelColors[i]);
    }

    for (int i = 0; i < LightPastelColors.Length; i++) {
      cachedLightPastelBrushes_[i] = ColorBrushes.GetBrush(cachedLightPastelColors_[i]);
    }

    for (int i = 0; i < PastelColors.Length; i++) {
      cachedPastelBrushes_[i] = ColorBrushes.GetBrush(cachedPastelColors_[i]);
    }
  }

  public static Color AdjustSaturation(Color color, float saturationAdjustment = 2f) {
    RGBToHSL(color, out float h, out float s, out float l);
    s = Math.Clamp(s * saturationAdjustment, 0, 1);
    l = Math.Clamp(l * 0.5f, 0, 1);
    return HSLToRGB(h, s, l);
  }

  public static Color AdjustLight(Color color, float lightAdjustment) {
    RGBToHSL(color, out float h, out float s, out float l);
    l = Math.Clamp(l * lightAdjustment, 0, 1);
    return HSLToRGB(h, s, l);
  }

  public static List<Color> MakeColorPalette(float hue, float saturation,
                                             float minLight, float maxLight, int lightSteps) {
    float rangeStep = (maxLight - minLight) / lightSteps;
    var colors = new List<Color>();

    for (float light = minLight; light <= maxLight; light += rangeStep) {
      colors.Add(HSLToRGB(hue, saturation, light));
    }

    return colors;
  }

  public static Color GenerateRandomPastelColor() {
    return cachedPastelColors_[new Random().Next(PastelColors.Length)];
  }

  public static Color GeneratePastelColor(uint id) {
    return cachedPastelColors_[id % PastelColors.Length];
  }

  public static Color GenerateRandomLightPastelColor() {
    return cachedLightPastelColors_[new Random().Next(LightPastelColors.Length)];
  }

  public static Color GenerateLightPastelColor(uint id) {
    return cachedLightPastelColors_[id % LightPastelColors.Length];
  }

  public static Brush GenerateRandomPastelBrush() {
    return cachedPastelBrushes_[new Random().Next(PastelColors.Length)];
  }

  public static Brush GeneratePastelBrush(uint id) {
    return cachedPastelBrushes_[id % PastelColors.Length];
  }

  public static Brush GenerateRandomLightPastelBrush() {
    return cachedLightPastelBrushes_[new Random().Next(LightPastelColors.Length)];
  }

  public static Brush GenerateLightPastelBrush(uint id) {
    return cachedLightPastelBrushes_[id % LightPastelColors.Length];
  }

  public static Color HSLToRGB(float h, float s, float l) {
    float r, g, b;

    if (Math.Abs(s) < double.Epsilon) {
      r = g = b = l; // achromatic
    }
    else {
      float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
      float p = 2 * l - q;
      r = HueToRGB(p, q, h + 1f / 3f);
      g = HueToRGB(p, q, h);
      b = HueToRGB(p, q, h - 1f / 3f);
    }

    return Color.FromRgb(To255(r), To255(g), To255(b));
  }

  private static void RGBToHSL(Color color, out float h, out float s, out float l) {
    float r = color.R / 255f;
    float g = color.G / 255f;
    float b = color.B / 255f;
    float max = r > g && r > b ? r : g > b ? g : b;
    float min = r < g && r < b ? r : g < b ? g : b;
    l = (max + min) / 2.0f;

    if (Math.Abs(max - min) < double.Epsilon) {
      h = s = 0.0f;
    }
    else {
      float d = max - min;
      s = l > 0.5f ? d / (2.0f - max - min) : d / (max + min);

      if (r > g && r > b) {
        h = (g - b) / d + (g < b ? 6.0f : 0.0f);
      }
      else if (g > b) {
        h = (b - r) / d + 2.0f;
      }
      else {
        h = (r - g) / d + 4.0f;
      }

      h /= 6.0f;
    }
  }

  private static byte To255(float v) {
    return (byte)Math.Min(255, 256 * v);
  }

  private static float HueToRGB(float p, float q, float t) {
    if (t < 0f) {
      t += 1f;
    }

    if (t > 1f) {
      t -= 1f;
    }

    if (t < 1f / 6f) {
      return p + (q - p) * 6f * t;
    }

    if (t < 1f / 2f) {
      return q;
    }

    if (t < 2f / 3f) {
      return p + (q - p) * (2f / 3f - t) * 6f;
    }

    return p;
  }
}
