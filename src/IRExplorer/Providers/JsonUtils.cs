using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace IRExplorer {
    public class JsonColorConverter : JsonConverter<Color> {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert,
                                   JsonSerializerOptions options) {
            return Utils.ColorFromString(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) {
            writer.WriteStringValue(Utils.ColorToString(value));
        }
    }

    public class JsonUtils {
        public static JsonSerializerOptions GetJsonOptions() {
            var options = new JsonSerializerOptions();
            options.WriteIndented = true;
            options.PropertyNameCaseInsensitive = true;
            options.Converters.Add(new JsonColorConverter());
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return options;
        }

        public static bool Serialize<T>(T data, string path) {
            try {
                var options = GetJsonOptions();
                string result = JsonSerializer.Serialize(data, options);
                File.WriteAllText(path, result);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save JSON file: {ex.Message}");
                return false;
            }
        }

        public static bool Deserialize<T>(string path, out T data) where T : class {
            try {
                var options = GetJsonOptions();
                string text = File.ReadAllText(path);
                data = JsonSerializer.Deserialize<T>(text, options);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save JSON file: {ex.Message}");
                data = default;
                return false;
            }
        }
    }
}
