// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace ProfileExplorer.Core.Settings;

/// <summary>
/// Registry for type converters used during settings deserialization.
/// </summary>
public static class TypeConverterRegistry {
  private static readonly List<ITypeConverter> _converters = new();
  
  /// <summary>
  /// Registers a type converter.
  /// </summary>
  public static void Register(ITypeConverter converter) {
    _converters.Add(converter);
  }
  
  /// <summary>
  /// Attempts to convert a string value using registered converters.
  /// </summary>
  public static bool TryConvertFromString(string value, Type targetType, out object result) {
    result = null;
    
    foreach (var converter in _converters) {
      if (converter.CanConvert(targetType)) {
        try {
          result = converter.ConvertFromString(value, targetType);
          return true;
        }
        catch {
          // Continue trying other converters
        }
      }
    }
    
    return false;
  }
  
  /// <summary>
  /// Attempts to convert a string array using registered converters.
  /// </summary>
  public static bool TryConvertFromStringArray(string[] values, Type targetType, out object result) {
    result = null;
    
    foreach (var converter in _converters) {
      if (converter.CanConvert(targetType)) {
        try {
          result = converter.ConvertFromStringArray(values, targetType);
          return true;
        }
        catch {
          // Continue trying other converters
        }
      }
    }
    
    return false;
  }
}
