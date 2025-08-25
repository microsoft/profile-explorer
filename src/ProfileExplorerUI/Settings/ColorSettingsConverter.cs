// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Windows.Media;
using ProfileExplorerCore2.Settings;

namespace ProfileExplorerUI.Settings;

/// <summary>
/// Type converter for WPF Color types, enabling settings serialization/deserialization
/// of Color values from string representations.
/// </summary>
public class ColorSettingsConverter : ISettingsTypeConverter {
  public Type TargetType => typeof(Color);

  public object ConvertFromString(string stringValue) {
    try {
      return (Color)ColorConverter.ConvertFromString(stringValue);
    }
    catch (Exception) {
      return Colors.Transparent;
    }
  }

  public Array ConvertFromStringArray(string[] stringArray) {
    var colors = new Color[stringArray.Length];
    
    for (int i = 0; i < stringArray.Length; i++) {
      colors[i] = (Color)ConvertFromString(stringArray[i]);
    }
    
    return colors;
  }
}
