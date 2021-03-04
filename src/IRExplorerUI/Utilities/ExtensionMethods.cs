using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;
using Grpc.Core.Logging;
using ICSharpCode.AvalonEdit.Document;
using IRExplorerCore.IR;
using IRExplorerUI;

namespace IRExplorerUI {
    static class ExtensionMethods {
        private static readonly char[] NewLineChars = { '\r', '\n' };

        public static string RemoveChars(this string value, params char[] charList) {
            if (string.IsNullOrEmpty(value)) {
                return value;
            }

            var sb = new StringBuilder(value.Length);

            foreach (char c in value) {
                if (Array.IndexOf(charList, c) == -1) {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        public static string RemoveNewLines(this string value) {
            return value.RemoveChars(NewLineChars);
        }

        public static List<T> CloneList<T>(this List<T> list) {
            return list.ConvertAll(item => item);
        }

        public static Dictionary<TKey, TValue> CloneDictionary<TKey, TValue>(
            this Dictionary<TKey, TValue> dict) {
            var newDict = new Dictionary<TKey, TValue>(dict.Count);

            foreach (var item in dict) {
                newDict.Add(item.Key, item.Value);
            }

            return newDict;
        }

        public static HashSet<T> CloneHashSet<T>(this HashSet<T> hashSet) {
            var newHashSet = new HashSet<T>(hashSet.Count);

            foreach (var item in hashSet) {
                hashSet.Add(item);
            }

            return newHashSet;
        }

        public static HashSet<T> ToHashSet<T>(this List<T> list) where T : class {
            var hashSet = new HashSet<T>(list.Count);
            list.ForEach(item => hashSet.Add(item));
            return hashSet;
        }

        public static HashSet<TOut> ToHashSet<TIn, TOut>(this List<TIn> list, Func<TIn, TOut> action)
            where TIn : class where TOut : class {
            var hashSet = new HashSet<TOut>(list.Count);
            list.ForEach(item => hashSet.Add(action(item)));
            return hashSet;
        }

        public static List<T> ToList<T>(this HashSet<T> hashSet) where T : class {
            var list = new List<T>(hashSet.Count);

            foreach (var item in hashSet) {
                list.Add(item);
            }

            return list;
        }

        public static List<TOut> ToList<TIn, TOut>(this HashSet<TIn> hashSet, Func<TIn, TOut> action)
            where TIn : class {
            var list = new List<TOut>(hashSet.Count);

            foreach (var item in hashSet) {
                list.Add(action(item));
            }

            return list;
        }

        public static List<T> ToList<T>(this TextSegmentCollection<T> segments) where T : TextSegment {
            var list = new List<T>(segments.Count);

            foreach (var item in segments) {
                list.Add(item);
            }

            return list;
        }

        public static List<Tuple<K, V>> ToList<K, V>(this IDictionary<K, V> dict)
            where K : class where V : class {
            var list = new List<Tuple<K, V>>(dict.Count);

            foreach (var item in dict) {
                list.Add(new Tuple<K, V>(item.Key, item.Value));
            }

            return list;
        }

        public static List<Tuple<K2, V>> ToList<K1, K2, V>(this Dictionary<K1, V> dict)
            where K1 : IRElement where K2 : IRElementReference where V : class {
            var list = new List<Tuple<K2, V>>(dict.Count);

            foreach (var item in dict) {
                list.Add(new Tuple<K2, V>((K2)item.Key, item.Value));
            }

            return list;
        }

        public static Dictionary<K, V> ToDictionary<K, V>(this List<Tuple<K, V>> list)
            where K : class where V : class {
            var dict = new Dictionary<K, V>(list.Count);

            foreach (var item in list) {
                dict.Add(item.Item1, item.Item2);
            }

            return dict;
        }

        public static Dictionary<K2, V> ToDictionary<K1, K2, V>(this List<Tuple<K1, V>> list)
            where K1 : IRElementReference where K2 : IRElement where V : class {
            var dict = new Dictionary<K2, V>(list.Count);

            foreach (var item in list) {
                if (item.Item1 != null) {
                    dict.Add((K2)item.Item1, item.Item2);
                }
            }

            return dict;
        }

        public static List<int> AllIndexesOf(this string text, string value) {
            if (string.IsNullOrEmpty(value)) {
                return new List<int>();
            }

            var offsetList = new List<int>(32);
            int offset = text.IndexOf(value, StringComparison.InvariantCulture);

            while (offset != -1 && offset < text.Length) {
                offsetList.Add(offset);
                offset += value.Length;
                offset = text.IndexOf(value, offset, StringComparison.InvariantCulture);
            }

            return offsetList;
        }

        public static bool IsValidRGBColor(this RGBColor color) {
            return color.R != 0 || color.G != 0 || color.B != 0;
        }

        public static Color ToColor(this RGBColor color) {
            return Color.FromRgb((byte)color.R, (byte)color.G, (byte)color.B);
        }
    }
}
