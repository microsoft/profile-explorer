// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Windows.Media;

namespace Client {
    class ColorUtils {
        public static Color IncreaseSaturation(Color color, float saturationAdjustment = 2f) {
            rgbToHsl(color, out float h, out float s, out float l);
            s = Math.Clamp(s * saturationAdjustment, 0, 1);
            l = Math.Clamp(l * 0.5f, 0, 1);
            return hslToRgb(h, s, l);
        }

        public static Color AdjustLight(Color color, float lightAdjustment) {
            rgbToHsl(color, out float h, out float s, out float l);
            l = Math.Clamp(l * lightAdjustment, 0, 1);
            return hslToRgb(h, s, l);
        }

        public static void rgbToHsl(Color color, out float h, out float s, out float l) {
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

        public static Color hslToRgb(float h, float s, float l) {
            float r, g, b;

            if (Math.Abs(s) < double.Epsilon) {
                r = g = b = l; // achromatic
            }
            else {
                float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
                float p = 2 * l - q;
                r = hueToRgb(p, q, h + 1f / 3f);
                g = hueToRgb(p, q, h);
                b = hueToRgb(p, q, h - 1f / 3f);
            }

            return Color.FromRgb(to255(r), to255(g), to255(b));
        }

        public static byte to255(float v) {
            return (byte) Math.Min(255, 256 * v);
        }

        public static float hueToRgb(float p, float q, float t) {
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
