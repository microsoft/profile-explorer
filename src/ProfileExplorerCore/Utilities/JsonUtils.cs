// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfileExplorer.Core.Utilities;

public class StringInterningConverter : JsonConverter<string> {
  public override string Read(ref Utf8JsonReader reader, Type typeToConvert,
                              JsonSerializerOptions options) {
    return string.Intern(reader.GetString());
  }

  public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) {
    writer.WriteStringValue(value);
  }
}

public static class JsonUtils {
  private static readonly List<JsonConverter> customConverters = new List<JsonConverter>();

  /// <summary>
  /// Registers a custom JSON converter that will be added to all JsonSerializerOptions.
  /// </summary>
  /// <param name="converter">The custom converter to register</param>
  public static void RegisterConverter(JsonConverter converter) {
    if (converter != null && !customConverters.Contains(converter)) {
      customConverters.Add(converter);
    }
  }

  /// <summary>
  /// Unregisters a custom JSON converter.
  /// </summary>
  /// <param name="converter">The converter to unregister</param>
  public static void UnregisterConverter(JsonConverter converter) {
    customConverters.Remove(converter);
  }

  /// <summary>
  /// Clears all registered custom converters.
  /// </summary>
  public static void ClearCustomConverters() {
    customConverters.Clear();
  }

  public static JsonSerializerOptions GetJsonOptions() {
    var options = new JsonSerializerOptions {
      WriteIndented = true,
      PropertyNameCaseInsensitive = true,
      IgnoreReadOnlyProperties = true
    };

    // Add built-in converters
    options.Converters.Add(new StringInterningConverter());
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    
    // Add any registered custom converters
    foreach (var converter in customConverters) {
      options.Converters.Add(converter);
    }
    
    return options;
  }

  public static bool SerializeToFile<T>(T data, string path) {
    try {
      var options = GetJsonOptions();
      using var stream = File.OpenWrite(path);
      JsonSerializer.Serialize(stream, data, options);
      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to save JSON file: {ex.Message}");
      return false;
    }
  }

  public static string Serialize<T>(T data) {
    try {
      var options = GetJsonOptions();
      return JsonSerializer.Serialize(data, options);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to save JSON: {ex.Message}");
      return null;
    }
  }

  public static byte[] SerializeToBytes<T>(T data) {
    string text = Serialize(data);
    return text != null ? Encoding.UTF8.GetBytes(text) : null;
  }

  public static bool DeserializeFromFile<T>(string path, out T data) where T : class {
    try {
      var options = GetJsonOptions();
      using var stream = File.OpenRead(path);
      data = JsonSerializer.Deserialize<T>(stream, options);
      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load JSON file: {ex.Message}");
      data = default(T);
      return false;
    }
  }

  public static bool Deserialize<T>(string text, out T data) where T : class {
    try {
      var options = GetJsonOptions();
      data = JsonSerializer.Deserialize<T>(text, options);
      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to load JSON: {ex.Message}");
      data = default(T);
      return false;
    }
  }

  public static bool DeserializeFromBytes<T>(byte[] textData, out T data) where T : class {
    return Deserialize(Encoding.UTF8.GetString(textData), out data);
  }
}