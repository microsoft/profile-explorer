using System;
using System.Collections.Generic;
using System.Text;

namespace IRExplorerCore.Utilities {
    public static class ExtensionMethods {
        public static string Indent(this String value, int spaces) {
            var whitespace = new string(' ', spaces);
            var valueNoCr = value.Replace("\r\n", "\n", StringComparison.Ordinal);
            return valueNoCr.Replace("\n", $"{Environment.NewLine}{whitespace}", StringComparison.Ordinal);
        }

        public static V GetOrAddValue<K, V>(this Dictionary<K, V> dict, K key) where V : new() {
            if (!dict.TryGetValue(key, out V currentValue)) {
                currentValue = new V();
                dict[key] = currentValue;
            }

            return currentValue;
        }

        public static V GetOrAddValue<K, V>(this Dictionary<K, V> dict, K key, V defaultValue) where V : new() {
            if (!dict.TryGetValue(key, out V currentValue)) {
                currentValue = defaultValue;
                dict[key] = currentValue;
            }

            return currentValue;
        }

        public static V GetOrAddValue<K, V>(this Dictionary<K, V> dict, K key, Func<V> newValueFunc) where V : class{
            if (!dict.TryGetValue(key, out V currentValue)) {
                currentValue = newValueFunc();
                dict[key] = currentValue;
            }

            return currentValue;
        }

        public static V GetValueOrNull<K, V>(this Dictionary<K, V> dict, K key) where V : class {
            if (dict.TryGetValue(key, out V currentValue)) {
                return currentValue;
            }

            return null;
        }

        public static V GetValueOrDefault<K, V>(this Dictionary<K, V> dict, K key) {
            if (dict.TryGetValue(key, out V currentValue)) {
                return currentValue;
            }

            return default(V);
        }

        public static V GetValueOr<K, V>(this Dictionary<K, V> dict, K key, V defaultValue) {
            if (dict.TryGetValue(key, out V currentValue)) {
                return currentValue;
            }

            return defaultValue;
        }
    }
}