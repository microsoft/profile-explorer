// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace IRExplorerUI {
    class ColorUtils {
        static readonly string[] PastelColors = new string[] {
            "#FE9BA1",
            "#FFABA0",
            "#FFC69E",
            "#D8BBCA",
            "#D5A2BB",
            "#EAA2B9",
            "#FFAB9F",
            "#FFBEA1",
            "#FFE0A0",
            "#DBD2CA",
            "#D9B5B9",
            "#EAB4B8",
            "#FFBEA0",
            "#FFFF9F",
            "#C8F3A9",
            "#D4F3D4",
            "#CFD3BC",
            "#AAC4C5",
            "#99C4C6",
            "#9AE2B3",
            "#DCEFAC",
            "#A4C7F0",
            "#B7C6F0",
            "#DEDCB8",
            "#B6C9EE",
            "#EAB4B8",
            "#B0ABDB",
            "#CCA9DB",
            "#ACC6C5",
            "#F1DCB8"
        };
        
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
#if false
            Random random = new Random();
            int red = random.Next(256);
            int green = random.Next(256);
            int blue = random.Next(256);

            Color mix = Colors.White;
            red = (red + mix.R) / 2;
            green = (green + mix.G) / 2;
            blue = (blue + mix.B) / 2;
            return Color.FromRgb((byte)red, (byte)green, (byte)blue);
#else
            return Utils.ColorFromString(PastelColors[new Random().Next(PastelColors.Length)]);
#endif
        }

        public static Color GeneratePastelColor(int id) {
            return Utils.ColorFromString(PastelColors[id % PastelColors.Length]);
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
}