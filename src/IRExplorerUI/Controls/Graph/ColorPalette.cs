// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Windows.Media;
using ProtoBuf;

namespace IRExplorerUI;

[ProtoContract(SkipConstructor = true)]
public class ColorPalette {
  public ColorPalette(string name = "", string description = "") {
    Name = name;
    Description = description;
    Colors = new List<Color>();
  }

  public ColorPalette(IEnumerable<Color> colors, string name = "", string description = "") {
    Name = name;
    Description = description;
    Colors = new List<Color>(colors);
  }

  public static ColorPalette Profile =>
    new ColorPalette(new[] {
      Utils.ColorFromString("#FFFCF4D6"),
      Utils.ColorFromString("#FFFCF2D6"),
      Utils.ColorFromString("#FFFCF0D6"),
      Utils.ColorFromString("#FFFCEED6"),
      Utils.ColorFromString("#FFFCECD6"),
      Utils.ColorFromString("#FFFCEAD6"),
      Utils.ColorFromString("#FFFCE8D6"),
      Utils.ColorFromString("#FFFCE6D6"),
      Utils.ColorFromString("#FFFCE4D6"),
      Utils.ColorFromString("#FFFCE2D6"),
      Utils.ColorFromString("#FFFCE0D6"),
      Utils.ColorFromString("#FFFCDED6"),
      Utils.ColorFromString("#FFFCDCD6"),
      Utils.ColorFromString("#FFFCDAD7"),
      Utils.ColorFromString("#FFFCD9D7"),
      Utils.ColorFromString("#FFFCD7D7")
    });
  public static ColorPalette ProfileManaged =>
    new ColorPalette(new[] {
      Utils.ColorFromString("#FFC9DBF3"),
      Utils.ColorFromString("#FFCCDAF2"),
      Utils.ColorFromString("#FFD1DAF0"),
      Utils.ColorFromString("#FFD4DAEE"),
      Utils.ColorFromString("#FFD9DAEE"),
      Utils.ColorFromString("#FFDCDAEC"),
      Utils.ColorFromString("#FFDFDBEB"),
      Utils.ColorFromString("#FFE3DBEA"),
      Utils.ColorFromString("#FFE6DBE9"),
      Utils.ColorFromString("#FFE9DBE8"),
      Utils.ColorFromString("#FFECDCE8"),
      Utils.ColorFromString("#FFEEDCE8"),
      Utils.ColorFromString("#FFF1DDE7"),
      Utils.ColorFromString("#FFF4DEE7"),
      Utils.ColorFromString("#FFF6DEE7"),
      Utils.ColorFromString("#FFF7E0E7")
    });
  public static ColorPalette ProfileKernel =>
    new ColorPalette(new[] {
      Utils.ColorFromString("#FFCFFAFB"),
      Utils.ColorFromString("#FFCFF7FB"),
      Utils.ColorFromString("#FFD0F4FB"),
      Utils.ColorFromString("#FFD0F1FB"),
      Utils.ColorFromString("#FFD0EFFB"),
      Utils.ColorFromString("#FFD0ECFB"),
      Utils.ColorFromString("#FFD0EAFB"),
      Utils.ColorFromString("#FFD0E7FB"),
      Utils.ColorFromString("#FFD1E4FB"),
      Utils.ColorFromString("#FFD1E2FB"),
      Utils.ColorFromString("#FFD1E0FB"),
      Utils.ColorFromString("#FFD1DDFB"),
      Utils.ColorFromString("#FFD1DAFB"),
      Utils.ColorFromString("#FFD1D8FB"),
      Utils.ColorFromString("#FFD2D5FB"),
      Utils.ColorFromString("#FFD2D3FB")
    });
  public static ColorPalette DarkHue => MakeHue(0.9f, 0.2f, 10);
  public static ColorPalette LightHue => MakeHue(0.9f, 0.5f, 10);

  [ProtoMember(1)]
  public string Name { get; set; }
  [ProtoMember(2)]
  public string Description { get; set; }
  [ProtoMember(3)]
  public List<Color> Colors { get; set; }

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
    return PickScaleColor(value, maxValue).AsBrush();
  }

  public Brush PickBrushForPercentage(double weightPercentage, bool reverse = false) {
    return PickColorForPercentage(weightPercentage, reverse).AsBrush();
  }

  public Brush PickBrush(int colorIndex) {
    colorIndex = Math.Clamp(colorIndex, 0, Colors.Count - 1);
    return Colors[colorIndex].AsBrush();
  }
}