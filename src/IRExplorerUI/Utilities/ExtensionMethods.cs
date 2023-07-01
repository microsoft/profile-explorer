using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Grpc.Core.Logging;
using ICSharpCode.AvalonEdit.Document;
using IRExplorerCore.IR;
using IRExplorerUI;
using IRExplorerUI.Profile;

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

        public static List<(K, V)> ToList<K, V>(this IDictionary<K, V> dict) {
            var list = new List<(K, V)>(dict.Count);

            foreach (var item in dict) {
                list.Add((item.Key, item.Value));
            }

            return list;
        }

        public static List<K> ToKeyList<K, V>(this IDictionary<K, V> dict) {
            var list = new List<K>(dict.Count);

            foreach (var item in dict) {
                list.Add(item.Key);
            }

            return list;
        }

        public static List<V> ToValueList<K, V>(this IDictionary<K, V> dict) {
            var list = new List<V>(dict.Count);

            foreach (var item in dict) {
                list.Add(item.Value);
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

        public static bool AreEqual<TKey, TValue>(this Dictionary<TKey, TValue> first,
                                                  Dictionary<TKey, TValue> second) {
            if (first == second)
                return true;
            if ((first == null) || (second == null))
                return false;
            if (first.Count != second.Count)
                return false;

            var valueComparer = EqualityComparer<TValue>.Default;

            foreach (var kvp in first) {
                TValue value2;
                if (!second.TryGetValue(kvp.Key, out value2))
                    return false;
                if (!valueComparer.Equals(kvp.Value, value2))
                    return false;
            }

            return true;
        }

        public static int AccumulateValue<K>(this Dictionary<K, int> dict, K key, int value) {
            if (dict.TryGetValue(key, out var currentValue)) {
                var newValue = currentValue + value;
                dict[key] = newValue;
                return newValue;
            }
            else {
                dict[key] = value;
                return value;
            }
        }

        public static long AccumulateValue<K>(this Dictionary<K, long> dict, K key, long value) {
            if (dict.TryGetValue(key, out var currentValue)) {
                var newValue = currentValue + value;
                dict[key] = newValue;
                return newValue;
            }
            else {
                dict[key] = value;
                return value;
            }
        }

        public static TimeSpan AccumulateValue<K>(this Dictionary<K, TimeSpan> dict, K key, TimeSpan value) {
            if (dict.TryGetValue(key, out var currentValue)) {
                // The TimeSpan + operator does an overflow check that is not relevant
                // (and an exception undesirable), avoid it for some speedup.
                var sum = currentValue.Ticks + value.Ticks;
                var newValue = TimeSpan.FromTicks(sum);
                dict[key] = newValue;
                return newValue;
            }
            else {
                dict[key] = value;
                return value;
            }
        }

        public static int CollectMaxValue<K>(this Dictionary<K, int> dict, K key, int value) {
            if (dict.TryGetValue(key, out var currentValue)) {
                if (value > currentValue) {
                    dict[key] = value;
                    return value;
                }

                return currentValue;
            }
            else {
                dict[key] = value;
                return value;
            }
        }

        public static double CollectMaxValue<K>(this Dictionary<K, double> dict, K key, double value) {
            if (dict.TryGetValue(key, out var currentValue)) {
                if (value > currentValue) {
                    dict[key] = value;
                    return value;
                }

                return currentValue;
            }
            else {
                dict[key] = value;
                return value;
            }
        }

        public static TimeSpan CollectMaxValue<K>(this Dictionary<K, TimeSpan> dict, K key, TimeSpan value) {
            if (dict.TryGetValue(key, out var currentValue)) {
                if (value > currentValue) {
                    dict[key] = value;
                    return value;
                }

                return currentValue;
            }
            else {
                dict[key] = value;
                return value;
            }
        }

        public static SolidColorBrush AsBrush(this Color color) {
            return ColorBrushes.GetBrush(color);
        }

        public static SolidColorBrush AsBrush(this Color color, double opacity) {
            return ColorBrushes.GetTransparentBrush(color, opacity);
        }

        public static SolidColorBrush AsBrush(this Color color, byte alpha) {
            return ColorBrushes.GetTransparentBrush(color, alpha);
        }

        public static Pen AsPen(this Color color, double thickness = 1.0) {
            return ColorPens.GetPen(color, thickness);
        }

        public static Pen AsBoldPen(this Color color) {
            return ColorPens.GetBoldPen(color);
        }

        public static string AsTrimmedPercentageString(this double value, int digits = 2, string suffix = "%") {
            return AsPercentageString(value, digits, true, suffix);
        }

        public static string AsPercentageString(this double value, int digits = 2,
                                                bool trim = false, string suffix="%") {
            value = Math.Round(value * 100, digits);

            if (value == 0 && trim) {
                return "";
            }

            return digits switch {
                1 => $"{value:0.0}{suffix}",
                2 => $"{value:0.00}{suffix}",
                _ => String.Format("{0:0." + new string('0', digits) + "}", value) + suffix
            };
        }

        public static string AsMillisecondsString(this TimeSpan value, int digits = 2,
                                                  string suffix=" ms") {
            var roundedValue = value.TotalMilliseconds.TruncateToDigits(digits);
            return string.Format("{0:N" + Math.Abs(digits) + "}", roundedValue) + suffix;
        }

        public static string AsSecondsString(this TimeSpan value, int digits = 2,
                                             string suffix = " s") {
            var roundedValue = value.TotalSeconds.TruncateToDigits(digits);
            return string.Format("{0:N" + Math.Abs(digits) + "}", roundedValue) + suffix;
        }

        public static string AsTimeString(this TimeSpan value, int digits = 2) {
            return AsTimeString(value, value, digits);
        }

        public static string AsTimeString(this TimeSpan value, TimeSpan totalValue, int digits = 2) {
            if(value.Ticks == 0) {
                return "0";
            }
            else if (totalValue.TotalMinutes >= 60) {
                return value.ToString("h\\:mm\\:ss");
            }
            else if (totalValue.TotalMinutes >= 10) {
                return value.ToString("mm\\:ss");
            }
            else if (totalValue.TotalSeconds >= 60) {
                return $"{value.Minutes}:{value.Seconds:D2}";
            }
            else if (totalValue.TotalSeconds >= 10) {
                return value.ToString("ss");
            }
            else if (totalValue.TotalSeconds >= 1) {
                return $"{value.Seconds}";
            }
            else {
                var roundedValue = value.TotalMilliseconds.TruncateToDigits(digits);
                return string.Format("{0:N" + Math.Abs(digits) + "}", roundedValue) + " ms";
            }
        }

        public static string AsTimeStringWithMilliseconds(this TimeSpan value, int digits = 2) {
            return AsTimeStringWithMilliseconds(value, value, digits);
        }

        public static string AsTimeStringWithMilliseconds(this TimeSpan value, TimeSpan totalValue, int digits = 2) {
            if (value.Ticks == 0) {
                return "0";
            }
            else if (totalValue.TotalMinutes >= 60) {
                return value.ToString("h\\:mm\\:ss\\.fff");
            }
            else if (totalValue.TotalMinutes >= 10) {
                return value.ToString("mm\\:ss\\.fff");
            }
            else if (totalValue.TotalSeconds >= 60) {
                return $"{value.Minutes}:{value:ss\\.fff}";
            }
            else if (totalValue.TotalSeconds >= 10) {
                return value.ToString("ss\\.fff");
            }
            else if (totalValue.TotalSeconds >= 1) {
                return $"{value.Seconds}:{value:\\.fff}";
            }
            else {
                var roundedValue = value.TotalMilliseconds.TruncateToDigits(digits);
                return string.Format("{0:N" + Math.Abs(digits) + "}", roundedValue) + " ms";
            }
        }


        public static double TruncateToDigits(this double value, int digits) {
            double factor = Math.Pow(10, digits);
            value *= factor;
            value = Math.Truncate(value);
            return value / factor;
        }

        public static Point AdjustForMouseCursor(this Point value) {
            return new Point(value.X + SystemParameters.CursorWidth / 2,
                             value.Y + SystemParameters.CursorHeight / 2);
        }

        public static ItemContainer GetObjectAtPoint<ItemContainer>(this ItemsControl control, Point p)
            where ItemContainer : DependencyObject         {
            // ItemContainer - can be ListViewItem, or TreeViewItem and so on(depends on control)
            return control.GetContainerAtPoint<ItemContainer>(p);
        }

        private static ItemContainer GetContainerAtPoint<ItemContainer>(this ItemsControl control, Point p)
            where ItemContainer : DependencyObject         {
            var result = VisualTreeHelper.HitTest(control, p);
            var obj = result?.VisualHit;

            if (obj == null) {
                return null;
            }

            while (VisualTreeHelper.GetParent(obj) != null && !(obj is ItemContainer))             {
                obj = VisualTreeHelper.GetParent(obj);
            }

            // Will return null if not found
            return obj as ItemContainer;
        }

        public static string FormatFunctionName(this ProfileCallTreeNode node, FunctionNameFormatter nameFormatter,
                                                int maxLength = int.MaxValue) {
            return FormatName(node.FunctionName, nameFormatter, maxLength);
        }

        public static string FormatModuleName(this ProfileCallTreeNode node, FunctionNameFormatter nameFormatter,
                                              int maxLength = int.MaxValue) {
            return FormatName(node.ModuleName, nameFormatter, maxLength);
        }

        private static string FormatName(string name, FunctionNameFormatter nameFormatter, int maxLength) {
            if (string.IsNullOrEmpty(name)) {
                return name;
            }

            name = nameFormatter != null ? nameFormatter(name) : name;

            if (name.Length > maxLength && name.Length > 2) {
                name = $"{name.Substring(0, maxLength - 2)}...";
            }

            return name;
        }
    }
}