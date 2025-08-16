// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows.Media;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.Settings;

/// <summary>
/// Type converter for WPF Color and Color[] types.
/// </summary>
public class ColorTypeConverter : ITypeConverter {
  public bool CanConvert(Type targetType) {
    return targetType == typeof(Color) || targetType == typeof(Color[]);
  }

  public object ConvertFromString(string value, Type targetType) {
    if (targetType == typeof(Color)) {
      return Utils.ColorFromString(value);
    }
    
    throw new InvalidOperationException($"Cannot convert string to {targetType}");
  }

  public object ConvertFromStringArray(string[] values, Type targetType) {
    if (targetType == typeof(Color[])) {
      var colors = new Color[values.Length];
      for (int i = 0; i < values.Length; i++) {
        colors[i] = Utils.ColorFromString(values[i]);
      }
      return colors;
    }
    
    throw new InvalidOperationException($"Cannot convert string array to {targetType}");
  }
}
