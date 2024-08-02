// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using IRExplorerCore.Utilities;
using IRExplorerUI.Document;
using IRExplorerUI.Profile;

namespace IRExplorerUI.Utilities;

public class FontFamilyConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    try {
      return new FontFamily((string)value);
    }
    catch (Exception) {
      return null;
    }
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return ((FontFamily)value)?.Source;
  }
}

public class ColorPaletteConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    try {
      return ColorPalette.GetPalette((string)value);
    }
    catch (Exception) {
      return null;
    }
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return ((ColorPalette)value)?.Name;
  }
}

public class ListToStringConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    var sb = new StringBuilder();
    bool first = true;

    if (value is List<string> list) {
      foreach (string line in list) {
        if (!first) {
          sb.AppendLine();
        }
        else {
          first = false;
        }

        sb.Append(line);
      }
    }

    return sb.ToString();
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    var list = new List<string>();

    if (value is string text) {
      string[] lines = text.SplitLinesRemoveEmpty();
      list.AddRange(lines);
    }

    return list;
  }
}

public class ListPairToStringConverter : IValueConverter {
  private static char[] SPLIT_CHARS = {'=', ':'};

  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    var sb = new StringBuilder();
    bool first = true;

    if (value is List<(string Variable, string Value)> dict) {
      foreach (var pair in dict) {
        if (!first) {
          sb.AppendLine();
        }
        else {
          first = false;
        }

        sb.Append($"{pair.Variable}={pair.Value}");
      }
    }

    return sb.ToString();
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    var dict = new List<(string Variable, string Value)>();

    if (value is string text) {
      string[] lines = text.SplitLinesRemoveEmpty();

      foreach (string line in lines) {
        int splitIndex = line.IndexOfAny(SPLIT_CHARS);

        if (splitIndex > 0) {
          string namePart = line.Substring(0, splitIndex).Trim();
          string valuePart = "";

          if (splitIndex + 1 < line.Length) {
            valuePart = line.Substring(splitIndex + 1).Trim();
          }

          if (!string.IsNullOrEmpty(namePart)) {
            dict.Add((namePart, valuePart));
          }
        }
      }
    }

    return dict;
  }
}

public class DictionaryToStringConverter : IValueConverter {
  private static char[] SPLIT_CHARS = {'=', ':'};

  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    var sb = new StringBuilder();
    bool first = true;

    if (value is Dictionary<string, string> dict) {
      foreach (var pair in dict) {
        if (!first) {
          sb.AppendLine();
        }
        else {
          first = false;
        }

        sb.Append($"{pair.Key}={pair.Value}");
      }
    }

    return sb.ToString();
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    var dict = new Dictionary<string, string>();

    if (value is string text) {
      string[] lines = text.SplitLinesRemoveEmpty();

      foreach (string line in lines) {
        int splitIndex = line.IndexOfAny(SPLIT_CHARS);

        if (splitIndex > 0) {
          string namePart = line.Substring(0, splitIndex).Trim();
          string valuePart = "";

          if (splitIndex + 1 < line.Length) {
            valuePart = line.Substring(splitIndex + 1).Trim();
          }

          if (!string.IsNullOrEmpty(namePart)) {
            dict[namePart] = valuePart;
          }
        }
      }
    }

    return dict;
  }
}

public class PerformanceCounterListConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    var sb = new StringBuilder();
    bool first = true;

    if (value is List<PerformanceCounterConfig> list) {
      foreach (var line in list) {
        if (!first) {
          sb.AppendLine();
        }
        else {
          first = false;
        }

        sb.Append($"{line.Name}, Id {line.Id}");
      }
    }

    return sb.ToString();
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}

public class ProfileCallTreeNodeKindConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (value is ProfileCallTreeNodeKind kind) {
      if (parameter is bool flag and true) {
        return kind switch {
          ProfileCallTreeNodeKind.NativeUser   => "U",
          ProfileCallTreeNodeKind.NativeKernel => "K",
          ProfileCallTreeNodeKind.Managed      => "M",
          _                                    => ""
        };
      }

      return kind switch {
        ProfileCallTreeNodeKind.NativeUser   => "User mode execution context",
        ProfileCallTreeNodeKind.NativeKernel => "Kernel mode execution context",
        ProfileCallTreeNodeKind.Managed      => "Managed (.NET) execution context",
        _                                    => ""
      };
    }

    return null;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}

class BooleanConverter<T> : IValueConverter {
  public BooleanConverter(T trueValue, T falseValue) {
    True = trueValue;
    False = falseValue;
  }

  public T True { get; set; }
  public T False { get; set; }

  public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    return value is bool && (bool)value ? True : False;
  }

  public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return value is T && EqualityComparer<T>.Default.Equals((T)value, True);
  }
}

class InvertedBooleanConverter : BooleanConverter<bool> {
  public InvertedBooleanConverter() :
    base(false, true) {
  }
}

class BoolToVisibilityConverter : BooleanConverter<Visibility> {
  public BoolToVisibilityConverter() :
    base(Visibility.Visible, Visibility.Collapsed) {
  }
}

class EnumBooleanConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    return value != null && value.Equals(parameter);
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return value != null && (bool)value ? parameter : Binding.DoNothing;
  }
}

class BoolToParameterConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (value != null && (bool)value) {
      return new SolidColorBrush(Utils.ColorFromString((string)parameter));
    }

    return null;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}

class DoubleScalingConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (value == null) {
      return 0.0;
    }

    double doubleValue = (double)value;

    if (doubleValue == 0.0 || Math.Abs(doubleValue) < double.Epsilon) {
      return 0.0;
    }

    double maxValue = double.Parse((string)parameter);
    return doubleValue * maxValue;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    if (value == null) {
      return 0.0;
    }

    double doubleValue = (double)value;
    double maxValue = double.Parse((string)parameter);

    if (maxValue == 0) {
      return 0.0;
    }

    return doubleValue / maxValue;
  }
}

// This version allows the parameter itself to be bound to another value,
// such as the width of some UI element., which is not possible with ConverterParameter.
// https://stackoverflow.com/a/15309844
class DoubleScalingBoundConverter : IMultiValueConverter {
  public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
    if (values.Length < 2 || !(values[0] is double && values[1] is double)) {
      return null;
    }

    double doubleValue = (double)values[0];

    if (doubleValue == 0.0 || Math.Abs(doubleValue) < double.Epsilon) {
      return 0.0;
    }

    double maxValue = (double)values[1];
    return Math.Min(doubleValue * maxValue, maxValue);
  }

  public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
    return null;
  }
}

class DoubleDiffScalingBoundConverter : IMultiValueConverter {
  public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
    if (values.Length < 3 ||
        !(values[0] is double && values[1] is double && values[2] is double)) {
      return null;
    }

    double doubleValue1 = (double)values[0];
    double doubleValue2 = (double)values[1];
    double diffValue = doubleValue2 - doubleValue1;

    if (diffValue == 0.0 || Math.Abs(diffValue) < double.Epsilon) {
      return 0.0;
    }

    double maxValue = (double)values[2];
    return Math.Min(diffValue * maxValue, maxValue);
  }

  public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
    return null;
  }
}

class TreeViewLineConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    var item = (TreeViewItem)value;
    var ic = ItemsControl.ItemsControlFromItemContainer(item);
    return ic.ItemContainerGenerator.IndexFromContainer(item) == ic.Items.Count - 1;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    throw new Exception("The method or operation is not implemented.");
  }
}

class ColorBrushOpacityConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    var brush = value as SolidColorBrush;
    double opacity = double.Parse((string)parameter);

    if (brush == null) {
      return ColorBrushes.Transparent;
    }

    return ColorBrushes.GetTransparentBrush(brush.Color, opacity);
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    throw new Exception("The method or operation is not implemented.");
  }
}

class MillisecondTimeConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (value is TimeSpan timeValue) {
      return timeValue.AsMillisecondsString();
    }

    if (value is long longValue) {
      return TimeSpan.FromTicks(longValue).AsMillisecondsString();
    }

    if (value is double doubleValue) {
      return TimeSpan.FromTicks((long)doubleValue).AsMillisecondsString();
    }

    return value;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}

class SecondTimeConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (value is TimeSpan timeValue) {
      return timeValue.AsSecondsString();
    }

    if (value is long longValue) {
      return TimeSpan.FromTicks(longValue).AsSecondsString();
    }

    if (value is double doubleValue) {
      return TimeSpan.FromTicks((long)doubleValue).AsSecondsString();
    }

    return value;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}

class PercentageConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (value is double doubleValue) {
      return doubleValue.AsPercentageString();
    }

    return value;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}

class ExclusivePercentageConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (value is double doubleValue) {
      return $"{doubleValue.AsPercentageString()} self";
    }

    return value;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}

class RoundedPercentageConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (value is double doubleValue) {
      return doubleValue.AsPercentageString(0);
    }

    return value;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}


class AlternateRowConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    return (bool)value ? 2 : 1;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}

public class FunctionNameConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (string.IsNullOrEmpty(value as string)) {
      return null;
    }

    return ((string)value).TrimToLength(80);
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}

public class LongFunctionNameConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (string.IsNullOrEmpty(value as string)) {
      return null;
    }

    return DocumentUtils.FormatLongFunctionName((string)value);
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    return null;
  }
}

public class StringFormatConverter : IValueConverter {
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
    if (parameter is string formatString) {
      return string.Format(formatString, value);
    }

    return null;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
    throw new NotImplementedException();
  }
}