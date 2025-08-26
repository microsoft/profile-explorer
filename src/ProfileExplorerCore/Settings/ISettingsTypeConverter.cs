// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace ProfileExplorer.Core.Settings;

/// <summary>
/// Interface for custom type converters used in settings serialization/deserialization.
/// Allows registration of type-specific conversion logic for complex types.
/// </summary>
public interface ISettingsTypeConverter {
  /// <summary>
  /// Gets the target type this converter handles.
  /// </summary>
  Type TargetType { get; }

  /// <summary>
  /// Converts a string value to the target type.
  /// </summary>
  /// <param name="stringValue">The string representation of the value.</param>
  /// <returns>The converted value of the target type.</returns>
  object ConvertFromString(string stringValue);

  /// <summary>
  /// Converts an array of string values to an array of the target type.
  /// </summary>
  /// <param name="stringArray">The array of string representations.</param>
  /// <returns>An array of converted values of the target type.</returns>
  Array ConvertFromStringArray(string[] stringArray);
}
