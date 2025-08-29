// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.UI;

public class JsonColorConverter : JsonConverter<Color> {
  public override Color Read(ref Utf8JsonReader reader, Type typeToConvert,
                             JsonSerializerOptions options) {
    return Utils.ColorFromString(reader.GetString());
  }

  public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) {
    writer.WriteStringValue(Utils.ColorToString(value));
  }
}

public static class UIJsonUtils {
  private static readonly JsonColorConverter colorConverter = new JsonColorConverter();
  private static bool isInitialized = false;

  /// <summary>
  /// Initializes the UI-specific JSON converters by registering them with the Core JsonUtils.
  /// This method is safe to call multiple times.
  /// </summary>
  public static void Initialize() {
    if (!isInitialized) {
      JsonUtils.RegisterConverter(colorConverter);
      isInitialized = true;
    }
  }

  /// <summary>
  /// Cleans up UI-specific JSON converters by unregistering them from the Core JsonUtils.
  /// </summary>
  public static void Cleanup() {
    if (isInitialized) {
      JsonUtils.UnregisterConverter(colorConverter);
      isInitialized = false;
    }
  }

  // All JSON operations now delegate to the Core JsonUtils
  public static bool SerializeToFile<T>(T data, string path) => JsonUtils.SerializeToFile(data, path);
  public static string Serialize<T>(T data) => JsonUtils.Serialize(data);
  public static byte[] SerializeToBytes<T>(T data) => JsonUtils.SerializeToBytes(data);
  public static bool DeserializeFromFile<T>(string path, out T data) where T : class => JsonUtils.DeserializeFromFile(path, out data);
  public static bool Deserialize<T>(string text, out T data) where T : class => JsonUtils.Deserialize(text, out data);
  public static bool DeserializeFromBytes<T>(byte[] textData, out T data) where T : class => JsonUtils.DeserializeFromBytes(textData, out data);
}