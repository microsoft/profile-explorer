// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProfileExplorer.Core;
using ProfileExplorer.UI.Profile;

namespace ProfileExplorer.UI;

static class ExtensionMethods {
  private static readonly char[] NewLineChars = {'\r', '\n'};

  public static string RemoveChars(this string value, params char[] charList) {
    if (string.IsNullOrEmpty(value)) {
      return value;
    }

    var sb = new StringBuilder(value.Length);

    foreach (char c in value) {
      if (Array.IndexOf(charList, c) == -1) {
        sb.Append(c);
      }
    }

    return sb.ToString();
  }

  public static string RemoveNewLines(this string value) {
    return value.RemoveChars(NewLineChars);
  }

  public static string TrimToLength(this string text, int maxLength) {
    if (text.Length <= maxLength) {
      return text;
    }

    return $"{text.Substring(0, maxLength)}..";
  }

  public static List<int> AllIndexesOf(this string text, string value) {
    if (string.IsNullOrEmpty(value)) {
      return new List<int>();
    }

    var offsetList = new List<int>(32);
    int offset = text.IndexOf(value, StringComparison.InvariantCulture);

    while (offset != -1 && offset < text.Length) {
      offsetList.Add(offset);
      offset += value.Length;
      offset = text.IndexOf(value, offset, StringComparison.InvariantCulture);
    }

    return offsetList;
  }

  public static bool IsValidRGBColor(this RGBColor color) {
    return color.R != 0 || color.G != 0 || color.B != 0;
  }

  public static Color ToColor(this RGBColor color) {
    return Color.FromRgb((byte)color.R, (byte)color.G, (byte)color.B);
  }

  public static bool IsTransparent(this Color color) {
    return color.A == 0;
  }

  public static bool IsTransparent(this SolidColorBrush brush) {
    return brush.Color.A == 0;
  }

  public static bool IsTransparent(this Brush brush) {
    return brush is SolidColorBrush {Color.A: 0};
  }

  public static SolidColorBrush AsBrush(this Color color) {
    return ColorBrushes.GetBrush(color);
  }

  public static SolidColorBrush AsBrush(this Color color, double opacity) {
    return ColorBrushes.GetTransparentBrush(color, opacity);
  }

  public static SolidColorBrush AsBrush(this Color color, byte alpha) {
    return ColorBrushes.GetTransparentBrush(color, alpha);
  }

  public static Pen AsPen(this Color color, double thickness = 1.0) {
    return ColorPens.GetPen(color, thickness);
  }

  public static Pen AsBoldPen(this Color color) {
    return ColorPens.GetBoldPen(color);
  }

  // Cache percentage and time value to string conversion result
  // to reduce GC pressure when rendering.
  private record PercentageString(double value, int digits, bool trim, string suffix);
  private record TimeString(TimeSpan value, int digits, string suffix);
  private static ConcurrentDictionary<PercentageString, string> percentageStringCache_ = new();
  private static ConcurrentDictionary<TimeString, string> nanosecondsTimeStringCache_ = new();
  private static ConcurrentDictionary<TimeString, string> microsecondTimeStringCache_ = new();
  private static ConcurrentDictionary<TimeString, string> millisecondsTimeStringCache_ = new();
  private static ConcurrentDictionary<TimeString, string> secondsTimeStringCache_ = new();

  public static string AsTrimmedPercentageString(this double value, int digits = 2, string suffix = "%") {
    return AsPercentageString(value, digits, true, suffix);
  }

  public static string AsPercentageString(this double value, int digits = 2,
                                          bool trim = false, string suffix = "%") {
    var entry = new PercentageString(value, digits, trim, suffix);

    if (percentageStringCache_.TryGetValue(entry, out string percentageString)) {
      return percentageString;
    }

    value = Math.Round(value * 100, digits);

    if (value == 0 && trim) {
      percentageStringCache_.TryAdd(entry, "");
      return "";
    }

    percentageString = digits switch {
      1 => $"{value:0.0}{suffix}",
      2 => $"{value:0.00}{suffix}",
      _ => string.Format("{0:0." + new string('0', digits) + "}", value) + suffix
    };

    percentageStringCache_.TryAdd(entry, percentageString);
    return percentageString;
  }

  public static string AsNanosecondsString(this TimeSpan value, int digits = 2,
                                           string suffix = " ns") {
    var entry = new TimeString(value, digits, suffix);

    if (nanosecondsTimeStringCache_.TryGetValue(entry, out string timeString)) {
      return timeString;
    }

    double roundedValue = value.TotalNanoseconds.TruncateToDigits(digits);
    timeString = string.Format("{0:N" + Math.Abs(digits) + "}", roundedValue) + suffix;
    nanosecondsTimeStringCache_.TryAdd(entry, timeString);
    return timeString;
  }

  public static string AsMicrosecondString(this TimeSpan value, int digits = 2,
                                           string suffix = " µs") {
    var entry = new TimeString(value, digits, suffix);

    if (microsecondTimeStringCache_.TryGetValue(entry, out string timeString)) {
      return timeString;
    }

    double roundedValue = value.TotalMicroseconds.TruncateToDigits(digits);
    timeString = string.Format("{0:N" + Math.Abs(digits) + "}", roundedValue) + suffix;
    microsecondTimeStringCache_.TryAdd(entry, timeString);
    return timeString;
  }

  public static string AsMillisecondsString(this TimeSpan value, int digits = 2,
                                            string suffix = " ms") {
    var entry = new TimeString(value, digits, suffix);

    if (millisecondsTimeStringCache_.TryGetValue(entry, out string timeString)) {
      return timeString;
    }

    double roundedValue = value.TotalMilliseconds.TruncateToDigits(digits);
    timeString = string.Format("{0:N" + Math.Abs(digits) + "}", roundedValue) + suffix;
    millisecondsTimeStringCache_.TryAdd(entry, timeString);
    return timeString;
  }

  public static string AsSecondsString(this TimeSpan value, int digits = 2,
                                       string suffix = " s") {
    var entry = new TimeString(value, digits, suffix);

    if (secondsTimeStringCache_.TryGetValue(entry, out string timeString)) {
      return timeString;
    }

    double roundedValue = value.TotalSeconds.TruncateToDigits(digits);
    timeString = string.Format("{0:N" + Math.Abs(digits) + "}", roundedValue) + suffix;
    secondsTimeStringCache_.TryAdd(entry, timeString);
    return timeString;
  }

  public static string AsTimeString(this TimeSpan value, int digits = 2) {
    return AsTimeString(value, value, digits);
  }

  public static string AsTimeString(this TimeSpan value, TimeSpan totalValue, int digits = 2) {
    if (value.Ticks == 0) {
      return "0";
    }

    if (totalValue.TotalMinutes >= 60) {
      return value.ToString("h\\:mm\\:ss");
    }

    if (totalValue.TotalMinutes >= 10) {
      return value.ToString("mm\\:ss");
    }

    if (totalValue.TotalSeconds >= 60) {
      return $"{value.Minutes}:{value.Seconds:D2}";
    }

    if (totalValue.TotalSeconds >= 10) {
      return value.ToString("ss");
    }

    if (totalValue.TotalSeconds >= 1) {
      return $"{value.Seconds}";
    }

    double roundedValue = value.TotalMilliseconds.TruncateToDigits(digits);
    return string.Format("{0:N" + Math.Abs(digits) + "}", roundedValue) + " ms";
  }

  public static string AsTimeStringWithMilliseconds(this TimeSpan value, int digits = 2) {
    return AsTimeStringWithMilliseconds(value, value, digits);
  }

  public static string AsTimeStringWithMilliseconds(this TimeSpan value, TimeSpan totalValue, int digits = 2) {
    if (value.Ticks == 0) {
      return "0";
    }

    if (totalValue.TotalMinutes >= 60) {
      return value.ToString("h\\:mm\\:ss\\.fff");
    }

    if (totalValue.TotalMinutes >= 10) {
      return value.ToString("mm\\:ss\\.fff");
    }

    if (totalValue.TotalSeconds >= 60) {
      return $"{value.Minutes}:{value:ss\\.fff}";
    }

    if (totalValue.TotalSeconds >= 10) {
      return value.ToString("ss\\.fff");
    }

    if (totalValue.TotalSeconds >= 1) {
      return $"{value.Seconds}{value:\\.fff}";
    }

    double roundedValue = value.TotalMilliseconds.TruncateToDigits(digits);
    return string.Format("{0:N" + Math.Abs(digits) + "}", roundedValue) + " ms";
  }

  public static double TruncateToDigits(this double value, int digits) {
    double factor = Math.Pow(10, digits);
    value *= factor;
    value = Math.Truncate(value);
    return value / factor;
  }

  public static Point AdjustForMouseCursor(this Point value) {
    return new Point(value.X + SystemParameters.CursorWidth / 2,
                     value.Y + SystemParameters.CursorHeight / 2);
  }

  public static ItemContainer GetObjectAtPoint<ItemContainer>(this ItemsControl control, Point p)
    where ItemContainer : DependencyObject {
    // ItemContainer - can be ListViewItem, or TreeViewItem and so on(depends on control)
    return control.GetContainerAtPoint<ItemContainer>(p);
  }

  public static string FormatFunctionName(this ProfileCallTreeNode node, FunctionNameFormatter nameFormatter,
                                          int maxLength = int.MaxValue) {
    return FormatName(node.FunctionName, nameFormatter, maxLength);
  }

  public static string FormatModuleName(this ProfileCallTreeNode node, FunctionNameFormatter nameFormatter,
                                        int maxLength = int.MaxValue) {
    return FormatName(node.ModuleName, nameFormatter, maxLength);
  }

  public static string FormatFunctionName(this IRTextFunction func, ISession session, int maxLength = int.MaxValue) {
    return FormatName(func.Name, session.CompilerInfo.NameProvider.FormatFunctionName, maxLength);
  }

  public static string FormatFunctionName(this IRTextSection section, ISession session, int maxLength = int.MaxValue) {
    return FormatName(section.ParentFunction.Name, session.CompilerInfo.NameProvider.FormatFunctionName, maxLength);
  }

  public static string FormatFunctionName(this string name, ISession session, int maxLength = int.MaxValue) {
    return FormatName(name, session.CompilerInfo.NameProvider.FormatFunctionName, maxLength);
  }

  public static string FormatFunctionName(this ProfileCallTreeNode node, ISession session,
                                          int maxLength = int.MaxValue) {
    return FormatName(node.FunctionName, session.CompilerInfo.NameProvider.FormatFunctionName, maxLength);
  }

  private static ItemContainer GetContainerAtPoint<ItemContainer>(this ItemsControl control, Point p)
    where ItemContainer : DependencyObject {
    var result = VisualTreeHelper.HitTest(control, p);
    var obj = result?.VisualHit;

    if (obj == null) {
      return null;
    }

    while (VisualTreeHelper.GetParent(obj) != null && !(obj is ItemContainer)) {
      obj = VisualTreeHelper.GetParent(obj);
    }

    // Will return null if not found
    return obj as ItemContainer;
  }

  private static string FormatName(string name, FunctionNameFormatter nameFormatter, int maxLength) {
    if (string.IsNullOrEmpty(name)) {
      return name;
    }

    name = nameFormatter != null ? nameFormatter(name) : name;

    if (name.Length > maxLength) {
      if (maxLength > 3) {
        name = $"{name.Substring(0, maxLength - 3)}...";
      }
      else {
        name = name.Substring(0, maxLength);
      }
    }

    return name;
  }

  public static int GetStableHashCode(this string str) {
    unchecked {
      int hash1 = 5381;
      int hash2 = hash1;

      for (int i = 0; i < str.Length && str[i] != '\0'; i += 2) {
        hash1 = (hash1 << 5) + hash1 ^ str[i];
        if (i == str.Length - 1 || str[i + 1] == '\0')
          break;
        hash2 = (hash2 << 5) + hash2 ^ str[i + 1];
      }

      return hash1 + hash2 * 1566083941;
    }
  }
}