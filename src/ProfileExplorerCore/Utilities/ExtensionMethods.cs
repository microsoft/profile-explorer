// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ProfileExplorer.Core.Utilities;

public static class ExtensionMethods {
  private static readonly string[] NewLineStrings = {"\r\n", "\r", "\n"};

  public static string Indent(this string value, int spaces) {
    string whitespace = new(' ', spaces);
    string valueNoCr = value.Replace("\r\n", "\n", StringComparison.Ordinal);
    return valueNoCr.Replace("\n", $"{Environment.NewLine}{whitespace}", StringComparison.Ordinal);
  }

  public static V GetOrAddValue<K, V>(this Dictionary<K, V> dict, K key) where V : new() {
    ref var currentValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);

    if (!exists) {
      currentValue = new V();
    }

    return currentValue;
  }

  public static V GetOrAddValue<K, V>(this Dictionary<K, V> dict, K key, V defaultValue) where V : new() {
    ref var currentValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);

    if (!exists) {
      currentValue = defaultValue;
    }

    return currentValue;
  }

  public static V GetOrAddValue<K, V>(this Dictionary<K, V> dict, K key, Func<V> newValueFunc) where V : class {
    ref var currentValue = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);

    if (!exists) {
      currentValue = newValueFunc();
    }

    return currentValue;
  }

  public static V GetValueOrNull<K, V>(this Dictionary<K, V> dict, K key) where V : class {
    if (dict.TryGetValue(key, out var currentValue)) {
      return currentValue;
    }

    return null;
  }

  public static V GetValueOr<K, V>(this Dictionary<K, V> dict, K key, V defaultValue) {
    if (dict.TryGetValue(key, out var currentValue)) {
      return currentValue;
    }

    return defaultValue;
  }

  public static string[] NewLineSeparators(this string value) {
    return NewLineStrings;
  }

  public static string[] SplitLines(this string value) {
    return value.Split(NewLineStrings, StringSplitOptions.None);
  }

  public static string[] SplitLinesRemoveEmpty(this string value) {
    return value.Split(NewLineStrings, StringSplitOptions.RemoveEmptyEntries);
  }

  public static int CountLines(this string value) {
    if (string.IsNullOrEmpty(value)) {
      return 0;
    }
    int lines = 0;
    int len = value.Length;
    for (int i = 0; i < len; i++) {
      char c = value[i];
      if (c == '\r') {
        lines++;
        // Treat CRLF as a single newline.
        if (i + 1 < len && value[i + 1] == '\n') {
          i++;
        }
      }
      else if (c == '\n') {
        lines++;
      }
    }

    // If the last character wasn't a newline, account for the final line.
    if (len > 0) {
      char last = value[len - 1];
      if (last != '\n' && last != '\r') {
        lines++;
      }
    }

    return lines;
  }
}