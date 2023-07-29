// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IRExplorerUI {
    public class StringInterningConverter : JsonConverter<string> {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) {
            return string.Intern(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) {
            writer.WriteStringValue(value);
        }
    }

    public class JsonUtils {
        public static JsonSerializerOptions GetJsonOptions() {
            var options = new JsonSerializerOptions {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                IgnoreReadOnlyProperties = true,

            };

            options.Converters.Add(new StringInterningConverter());
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return options;
        }

        public static bool SerializeToFile<T>(T data, string path, JsonSerializerOptions options = null) {
            try {
                string result = JsonSerializer.Serialize(data, options ?? GetJsonOptions());
                File.WriteAllText(path, result);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save JSON file: {ex.Message}");
                return false;
            }
        }

        public static string Serialize<T>(T data, JsonSerializerOptions options = null) {
            try {
                return JsonSerializer.Serialize(data, options ?? GetJsonOptions());
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save JSON: {ex.Message}");
                return null;
            }
        }

        public static byte[] SerializeToBytes<T>(T data, JsonSerializerOptions options = null) {
            var text = Serialize(data, options);
            return text != null ? Encoding.UTF8.GetBytes(text) : null;
        }

        public static bool DeserializeFromFile<T>(string path, out T data, JsonSerializerOptions options = null) where T : class {
            try {
                string text = File.ReadAllText(path);
                data = JsonSerializer.Deserialize<T>(text, options ?? GetJsonOptions());
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load JSON file: {ex.Message}");
                data = default;
                return false;
            }
        }

        public static bool Deserialize<T>(string text, out T data, JsonSerializerOptions options = null) where T : class {
            try {
                data = JsonSerializer.Deserialize<T>(text, options ?? GetJsonOptions());
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save JSON: {ex.Message}");
                data = default;
                return false;
            }
        }

        public static bool DeserializeFromBytes<T>(byte[] textData, out T data, JsonSerializerOptions options = null) where T : class {
            return Deserialize(Encoding.UTF8.GetString(textData), out data, options);
        }
    }
}