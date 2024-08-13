// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace ProfileExplorer.UI;

[ProtoContract(SkipConstructor = true)]
public class ColorPalette {
  public ColorPalette(string name = "", string description = "") {
    Name = name;
    Description = description;
    Colors = new List<Color>();
    Brushes = new List<Brush>();
  }

  public ColorPalette(IEnumerable<Color> colors, string name = "", string description = "") {
    Name = name;
    Description = description;
    Colors = new List<Color>(colors);
    Brushes = new List<Brush>();

    foreach (var color in Colors) {
      Brushes.Add(color.AsBrush());
    }
  }

  public ColorPalette(IEnumerable<string> colors, string name = "", string description = "") {
    Name = name;
    Description = description;
    Colors = new List<Color>();
    Brushes = new List<Brush>();

    foreach (string color in colors) {
      Colors.Add(Utils.ColorFromString(color));
    }

    foreach (var color in Colors) {
      Brushes.Add(color.AsBrush());
    }
  }

  public static List<ColorPalette> BuiltinPalettes {
    get {
      var list = new List<ColorPalette> {
        Profile,
        ProfileManaged,
        ProfileKernel,
        LightPastels,
        Pastels
      };

      for (int i = 0; i < ColorUtils.LightPastelColors.Length; i++) {
        var color = Utils.ColorFromString(ColorUtils.LightPastelColors[i]);
        list.Add(new ColorPalette(new List<Color> {
                                    color
                                  }, $"LightPastelColor{i}"));
      }

      return list;
    }
  }

  public static List<ColorPalette> GradientBuiltinPalettes =>
    new() {
      LightPastels,
      Pastels,
      LightPastels2,
      LightPastels3,
      LightPastels4,
      Pastels2,
      Profile,
      ProfileManaged,
      ProfileKernel
    };
  public static List<ColorPalette>[] BuiltinPaletteSets = new List<ColorPalette>[] {
    BuiltinPalettes,
    GradientBuiltinPalettes
  };

  public static ColorPalette GetPalette(string name) {
    foreach (var set in BuiltinPaletteSets) {
      foreach (var palette in set) {
        if (palette.Name == name) {
          return palette;
        }
      }
    }

    return Profile;
  }

  public static ColorPalette Profile =>
    new(new[] {
      "#FFF4F1E8",
      "#FFFCF2D6",
      "#FFFCEED6",
      "#FFFCEAD6",
      "#FFFCE6D6",
      "#FFFCE2D6",
      "#FFFCDED6",
      "#FFFCDAD7",
      "#FFFCD7D7"
    }, "Profile");
  public static ColorPalette ProfileManaged =>
    new(new[] {
      "#FFCCDAF2",
      "#FFD4DAEE",
      "#FFDCDAEC",
      "#FFE3DBEA",
      "#FFE9DBE8",
      "#FFEEDCE8",
      "#FFF4DEE7",
      "#FFF7E0E7"
    }, "ProfileManaged");
  public static ColorPalette ProfileKernel =>
    new(new[] {
      "#FFCFF7FB",
      "#FFD0F1FB",
      "#FFD0ECFB",
      "#FFD0E7FB",
      "#FFD1E2FB",
      "#FFD1DDFB",
      "#FFD1D8FB",
      "#FFD2D3FB"
    }, "ProfileKernel");
  public static ColorPalette Pastels => new(ColorUtils.PastelColors, "Pastels");
  public static ColorPalette Pastels2 =>
    new(new[] {
      "#E2E2DF", "#D2D2CF", "#E2CFC4", "#F7D9C4",
      "#FAEDCB", "#C9E4DE", "#C6DEF1", "#DBCDF0",
      "#F2C6DE", "#F9C6C9"
    }, "Pastels2");
  public static ColorPalette LightPastels => new(ColorUtils.LightPastelColors, "LightPastels");
  public static ColorPalette LightPastels2 =>
    new(new[] {
      "#E8DCE6", "#FFEDE0", "#FCDEE0", "#FAD2E1",
      "#D3EAE3", "#BEE1E6", "#EDE9DC", "#DFE7FD"
    }, "LightPastels2");
  public static ColorPalette LightPastels3 =>
    new(new[] {
      "#FFF1E6", "#FDE2E4", "#FAD2E1",
      "#C5DEDD", "#DBE7E4", "#F0EFEB", "#BCD4E6", "#99C1DE"
    }, "LightPastels3");
  public static ColorPalette LightPastels4 =>
    new(new[] {
      "#F0D7DF", "#F8EAEC", "#F7DDD9",
      "#F7E6DA", "#E3E9DD", "#C4DBD9", "#D4E5E3",
      "#C8C7D6"
    }, "LightPastels4");
  public static ColorPalette DarkHue => MakeHue(0.9f, 0.2f, 10);
  public static ColorPalette LightHue => MakeHue(0.9f, 0.5f, 10);
  [ProtoMember(1)]
  public string Name { get; set; }
  [ProtoMember(2)]
  public string Description { get; set; }
  [ProtoMember(3)]
  public List<Color> Colors { get; set; }
  public List<Brush> Brushes { get; set; }
  public int Count => Colors.Count;
  public Color this[int index] => Colors[index];

  public static ColorPalette MakeHue(float saturation, float light, int lightSteps) {
    var colors = new List<Color>();
    float rangeStep = 3.0f / lightSteps;
    float hue = 0;

    for (int i = 0; i < lightSteps; i++) {
      colors.Add(ColorUtils.HSLToRGB(hue, saturation, light));
      hue += rangeStep;
    }

    return new ColorPalette(colors);
  }

  public static ColorPalette MakeScale(float hue, float saturation,
                                       float minLight, float maxLight, int lightSteps) {
    float rangeStep = (maxLight - minLight) / lightSteps;
    var colors = new List<Color>();

    for (float light = minLight; light <= maxLight; light += rangeStep) {
      colors.Add(ColorUtils.HSLToRGB(hue, saturation, light));
    }

    return new ColorPalette(colors);
  }

  public Color PickScaleColor(long value, long maxValue) {
    return PickColor((int)Math.Floor((double)value * Colors.Count / maxValue));
  }

  public Color PickColorForPercentage(double weightPercentage, bool reverse = false) {
    int colorIndex = (int)Math.Floor(Colors.Count * weightPercentage);
    return PickColor(colorIndex, reverse);
  }

  public Color PickColor(int colorIndex, bool reverse = false) {
    if (reverse) {
      colorIndex = Colors.Count - colorIndex - 1;
    }

    colorIndex = Math.Clamp(colorIndex, 0, Colors.Count - 1);
    return Colors[colorIndex];
  }

  public Brush PickScaleBrush(long value, long maxValue) {
    return PickBrush((int)Math.Floor((double)value * Colors.Count / maxValue));
  }

  public Brush PickBrushForPercentage(double weightPercentage, bool reverse = false) {
    return PickColorForPercentage(weightPercentage, reverse).AsBrush();
  }

  public Brush PickBrush(int colorIndex, bool reverse = false) {
    if (reverse) {
      colorIndex = Colors.Count - colorIndex - 1;
    }

    colorIndex = Math.Clamp(colorIndex, 0, Colors.Count - 1);
    return Brushes[colorIndex];
  }

  public Brush PickBrush(string name, bool reverse = false) {
    int hash = name.GetStableHashCode();
    int index = Math.Abs(hash % Count);
    return PickBrush(index);
  }
}