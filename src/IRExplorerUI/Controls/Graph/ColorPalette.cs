using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace IRExplorerUI {
    public class ColorPalette {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<Color> Colors { get; set; }
        public int Count => Colors.Count;
        public Color this[int index] => Colors[index];

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

        public static ColorPalette HeatMap =>
            new ColorPalette(new Color[] {
                Utils.ColorFromString("#598AC5"),
                Utils.ColorFromString("#7EA2D2"),
                Utils.ColorFromString("#A2BCDF"), 
                Utils.ColorFromString("#C6D6ED"),
                Utils.ColorFromString("#EBEFF8"), 
                Utils.ColorFromString("#FBECEF"), 
                Utils.ColorFromString("#F9CDCC"), 
                Utils.ColorFromString("#FAABAE"), 
                Utils.ColorFromString("#F88A8B"), 
                Utils.ColorFromString("#F8696B"),
            });

        public static ColorPalette HeatMap2 =>
            new ColorPalette(new Color[] { 
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
            });

        public static ColorPalette Profile =>
            new ColorPalette(new Color[] {
                Utils.ColorFromString("#FFFEE1"),
                Utils.ColorFromString("#FFFEE1"),
                Utils.ColorFromString("#FFFAE1"),
                Utils.ColorFromString("#FFF9E1"),
                Utils.ColorFromString("#FFEFD9"),
                Utils.ColorFromString("#FFE9D9"),
                Utils.ColorFromString("#FFE9D9"),
                Utils.ColorFromString("#FFE3D9"),
                Utils.ColorFromString("#FFDED9"),
                Utils.ColorFromString("#FFD1D1")
            });

        public static ColorPalette DarkHue => MakeHue(0.9f,0.2f, 10);
        public static ColorPalette LightHue => MakeHue(0.9f, 0.5f, 10);

        public static ColorPalette MakeHue(float saturation, float light, int lightSteps) {
            var colors = new List<Color>();
            float rangeStep = 3.0f / lightSteps;
            float hue = 0;

            for (int i = 0; i < lightSteps; i++) {
                colors.Add(ColorUtils.hslToRgb(hue, saturation, light));
                hue += rangeStep;

            }

            return new ColorPalette(colors);
        }

        public static ColorPalette MakeScale(float hue, float saturation,
            float minLight, float maxLight, int lightSteps) {
            float rangeStep = (maxLight - minLight) / lightSteps;
            var colors = new List<Color>();

            for (float light = minLight; light <= maxLight; light += rangeStep) {
                colors.Add(ColorUtils.hslToRgb(hue, saturation, light));

            }

            return new ColorPalette(colors);
        }

        public Color PickScaleColor(long value, long maxValue) {
            return PickColor((int)Math.Floor(((double)value * Colors.Count) / (double)maxValue));
        }

        public Color PickColorForPercentage(double weightPercentage, bool reverse = false) {
            int colorIndex = (int)Math.Floor(Colors.Count * (weightPercentage));
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
}