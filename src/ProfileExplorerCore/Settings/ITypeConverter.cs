// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace ProfileExplorer.Core.Settings;

/// <summary>
/// Interface for converting string values to specific types during settings deserialization.
/// </summary>
public interface ITypeConverter {
  /// <summary>
  /// Determines if this converter can handle the specified type.
  /// </summary>
  bool CanConvert(Type targetType);
  
  /// <summary>
  /// Converts a string value to the target type.
  /// </summary>
  object ConvertFromString(string value, Type targetType);
  
  /// <summary>
  /// Converts an array of string values to the target array type.
  /// </summary>
  object ConvertFromStringArray(string[] values, Type targetType);
}
