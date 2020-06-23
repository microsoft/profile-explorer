// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using CoreLib.Analysis;
using CoreLib.IR;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace Client {
    static class Utils {
        private static readonly char[] NewLineChars = {'\r', '\n'};

        public static T FindChild<T>(DependencyObject parent, string childName = null)
            where T : DependencyObject {
            // Confirm parent and childName are valid. 
            if (parent == null) {
                return null;
            }

            T foundChild = null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childrenCount; i++) {
                var child = VisualTreeHelper.GetChild(parent, i);

                // If the child is not of the request child type child
                var childType = child as T;

                if (childType == null) {
                    // recursively drill down the tree
                    foundChild = FindChild<T>(child, childName);

                    // If the child is found, break so we do not overwrite the found child. 
                    if (foundChild != null) {
                        break;
                    }
                }
                else if (!string.IsNullOrEmpty(childName)) {
                    // If the child's name is set for search
                    if (child is FrameworkElement frameworkElement && frameworkElement.Name == childName) {
                        // if the child's name is of the request name
                        foundChild = (T) child;
                        break;
                    }
                }
                else {
                    // child element found.
                    foundChild = (T) child;
                    break;
                }
            }

            return foundChild;
        }

        public static void PatchComboBoxStyle(ComboBox control) {
            if (control == null) {
                return;
            }

            var chrome = FindChild<Border>(control, "Chrome");

            if (chrome != null) {
                chrome.BorderBrush = null;
            }
        }

        public static void PatchToolbarStyle(ToolBar toolbar) {
            if (toolbar == null) {
                return;
            }

            // Change overflow arrow color.
            if (toolbar.Template.FindName("OverflowButton", toolbar) is ToggleButton overflowButton) {
                overflowButton.Background = Brushes.Gainsboro;
            }

            // Hide overflow arrow if not needed.
            if (toolbar.Template.FindName("OverflowGrid", toolbar) is FrameworkElement overflowGrid) {
                overflowGrid.Visibility =
                    toolbar.HasOverflowItems ? Visibility.Visible : Visibility.Collapsed;
            }

            toolbar.SizeChanged += Control_SizeChanged;
            toolbar.UpdateLayout();
        }

        public static void RemoveToolbarOverflowButton(ToolBar toolbar) {
            if (toolbar == null) {
                return;
            }

            if (toolbar.Template.FindName("OverflowGrid", toolbar) is FrameworkElement overflowGrid) {
                overflowGrid.Visibility = Visibility.Collapsed;
            }

            if (toolbar.Template.FindName("MainPanelBorder", toolbar) is FrameworkElement mainPanelBorder) {
                mainPanelBorder.Margin = new Thickness(0);
            }
        }

        private static void Control_SizeChanged(object sender, SizeChangedEventArgs e) {
            if (!(sender is ToolBar toolbar)) {
                return;
            }

            if (toolbar.Template.FindName("OverflowGrid", toolbar) is FrameworkElement overflowGrid) {
                overflowGrid.Visibility =
                    toolbar.HasOverflowItems ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public static HighlightingStyle GetSelectedColorStyle(ColorEventArgs args, Pen pen = null) {
            return args != null ? new HighlightingStyle(args.SelectedColor, pen) : null;
        }

        public static bool IsKeyboardModifierActive() {
            return IsControlModifierActive() || IsAltModifierActive() || IsShiftModifierActive();
        }

        public static bool IsControlModifierActive() {
            return (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        }

        public static bool IsAltModifierActive() {
            return (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        }

        public static bool IsShiftModifierActive() {
            return (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        }

        public static Color ChangeColorLuminisity(Color color, double adjustment) {
            return Color.FromArgb(color.A, (byte) Math.Clamp(color.R * adjustment, 0, 255),
                                  (byte) Math.Clamp(color.G * adjustment, 0, 255),
                                  (byte) Math.Clamp(color.B * adjustment, 0, 255));
        }

        public static int EstimateBrightness(Color color) {
            return (int)Math.Sqrt(
               color.R * color.R * .241 +
               color.G * color.G * .691 +
               color.B * color.B * .068);
        }

        public static Color ColorFromString(string color) {
            return (Color) ColorConverter.ConvertFromString(color);
        }

        public static Brush BrushFromString(string color) {
            return BrushFromColor(ColorFromString(color));
        }

        public static Brush BrushFromColor(Color color) {
            return ColorBrushes.GetBrush(color);
        }

        public static string BrushToString(Brush brush) {
            if (brush == null || !(brush is SolidColorBrush colorBrush)) {
                return "";
            }

            var color = colorBrush.Color;
            return ColorToString(color);
        }

        public static string ColorToString(Color color) {
            return $"#{color.R:x2}{color.G:x2}{color.B:x2}";
        }

        //? TODO: This should be part of the IR NameProvider
        public static string MakeBlockDescription(BlockIR block) {
            if (block == null) {
                return "";
            }

            if (block.HasLabel) {
                return $"B{block.Number} ({block.Label.Name.ToString()})";
            }

            return $"B{block.Number}";
        }

        public static string MakeElementDescription(IRElement element) {
            switch (element) {
                case BlockIR block: return MakeBlockDescription(block);
                case InstructionIR instr: {
                    var builder = new StringBuilder();
                    bool needsComma = false;

                    if (instr.Destinations.Count > 0) {
                        foreach (var destOp in instr.Destinations) {
                            if (needsComma) {
                                builder.Append(", ");
                            }
                            else {
                                needsComma = true;
                            }

                            builder.Append(MakeElementDescription(destOp));
                        }

                        builder.Append(" = ");
                    }

                    builder.Append($"{instr.OpcodeText} ");
                    needsComma = false;

                    foreach (var sourceOp in instr.Sources) {
                        if (needsComma) {
                            builder.Append(", ");
                        }
                        else {
                            needsComma = true;
                        }

                        builder.Append(MakeElementDescription(sourceOp));
                    }

                    return builder.ToString();
                }
                case OperandIR op: {
                    string text = ReferenceFinder.GetSymbolName(op);
                    var ssaTag = op.GetTag<ISSAValue>();

                    if (ssaTag != null) {
                        text += $"<{ssaTag.DefinitionId}>";
                    }

                    return text;
                }
                default: return element.ToString();
            }
        }

        public static KeyCharInfo KeyToChar(Key key) {
            var info = new KeyCharInfo();
            info.IsAlt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
            info.IsControl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            info.IsShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            info.IsLetter = !info.IsAlt && !info.IsControl;
            bool isUpperCase = (Console.CapsLock && !info.IsShift) || (!Console.CapsLock && info.IsShift);

            switch (key) {
                case Key.A:
                    info.Letter = isUpperCase ? 'A' : 'a';
                    break;
                case Key.B:
                    info.Letter = isUpperCase ? 'B' : 'b';
                    break;
                case Key.C:
                    info.Letter = isUpperCase ? 'C' : 'c';
                    break;
                case Key.D:
                    info.Letter = isUpperCase ? 'D' : 'd';
                    break;
                case Key.E:
                    info.Letter = isUpperCase ? 'E' : 'e';
                    break;
                case Key.F:
                    info.Letter = isUpperCase ? 'F' : 'f';
                    break;
                case Key.G:
                    info.Letter = isUpperCase ? 'G' : 'g';
                    break;
                case Key.H:
                    info.Letter = isUpperCase ? 'H' : 'h';
                    break;
                case Key.I:
                    info.Letter = isUpperCase ? 'I' : 'i';
                    break;
                case Key.J:
                    info.Letter = isUpperCase ? 'J' : 'j';
                    break;
                case Key.K:
                    info.Letter = isUpperCase ? 'K' : 'k';
                    break;
                case Key.L:
                    info.Letter = isUpperCase ? 'L' : 'l';
                    break;
                case Key.M:
                    info.Letter = isUpperCase ? 'M' : 'm';
                    break;
                case Key.N:
                    info.Letter = isUpperCase ? 'N' : 'n';
                    break;
                case Key.O:
                    info.Letter = isUpperCase ? 'O' : 'o';
                    break;
                case Key.P:
                    info.Letter = isUpperCase ? 'P' : 'p';
                    break;
                case Key.Q:
                    info.Letter = isUpperCase ? 'Q' : 'q';
                    break;
                case Key.R:
                    info.Letter = isUpperCase ? 'R' : 'r';
                    break;
                case Key.S:
                    info.Letter = isUpperCase ? 'S' : 's';
                    break;
                case Key.T:
                    info.Letter = isUpperCase ? 'T' : 't';
                    break;
                case Key.U:
                    info.Letter = isUpperCase ? 'U' : 'u';
                    break;
                case Key.V:
                    info.Letter = isUpperCase ? 'V' : 'v';
                    break;
                case Key.W:
                    info.Letter = isUpperCase ? 'W' : 'w';
                    break;
                case Key.X:
                    info.Letter = isUpperCase ? 'X' : 'x';
                    break;
                case Key.Y:
                    info.Letter = isUpperCase ? 'Y' : 'y';
                    break;
                case Key.Z:
                    info.Letter = isUpperCase ? 'Z' : 'z';
                    break;
                case Key.D0:
                    info.Letter = info.IsShift ? ')' : '0';
                    break;
                case Key.D1:
                    info.Letter = info.IsShift ? '!' : '1';
                    break;
                case Key.D2:
                    info.Letter = info.IsShift ? '@' : '2';
                    break;
                case Key.D3:
                    info.Letter = info.IsShift ? '#' : '3';
                    break;
                case Key.D4:
                    info.Letter = info.IsShift ? '$' : '4';
                    break;
                case Key.D5:
                    info.Letter = info.IsShift ? '%' : '5';
                    break;
                case Key.D6:
                    info.Letter = info.IsShift ? '^' : '6';
                    break;
                case Key.D7:
                    info.Letter = info.IsShift ? '&' : '7';
                    break;
                case Key.D8:
                    info.Letter = info.IsShift ? '*' : '8';
                    break;
                case Key.D9:
                    info.Letter = info.IsShift ? '(' : '9';
                    break;
                case Key.OemPlus:
                    info.Letter = info.IsShift ? '+' : '=';
                    break;
                case Key.OemMinus:
                    info.Letter = info.IsShift ? '_' : '-';
                    break;
                case Key.OemQuestion:
                    info.Letter = info.IsShift ? '?' : '/';
                    break;
                case Key.OemComma:
                    info.Letter = info.IsShift ? '<' : ',';
                    break;
                case Key.OemPeriod:
                    info.Letter = info.IsShift ? '>' : '.';
                    break;
                case Key.OemOpenBrackets:
                    info.Letter = info.IsShift ? '{' : '[';
                    break;
                case Key.OemQuotes:
                    info.Letter = info.IsShift ? '"' : '\'';
                    break;
                case Key.Oem1:
                    info.Letter = info.IsShift ? ':' : ';';
                    break;
                case Key.Oem3:
                    info.Letter = info.IsShift ? '~' : '`';
                    break;
                case Key.Oem5:
                    info.Letter = info.IsShift ? '|' : '\\';
                    break;
                case Key.Oem6:
                    info.Letter = info.IsShift ? '}' : ']';
                    break;
                case Key.Space:
                    info.Letter = ' ';
                    break;
                case Key.Tab:
                    info.Letter = '\t';
                    break;
                case Key.NumPad0:
                    info.Letter = '0';
                    break;
                case Key.NumPad1:
                    info.Letter = '1';
                    break;
                case Key.NumPad2:
                    info.Letter = '2';
                    break;
                case Key.NumPad3:
                    info.Letter = '3';
                    break;
                case Key.NumPad4:
                    info.Letter = '4';
                    break;
                case Key.NumPad5:
                    info.Letter = '5';
                    break;
                case Key.NumPad6:
                    info.Letter = '6';
                    break;
                case Key.NumPad7:
                    info.Letter = '7';
                    break;
                case Key.NumPad8:
                    info.Letter = '8';
                    break;
                case Key.NumPad9:
                    info.Letter = '9';
                    break;
                case Key.Subtract:
                    info.Letter = '-';
                    break;
                case Key.Add:
                    info.Letter = '+';
                    break;
                case Key.Decimal:
                    info.Letter = '.';
                    break;
                case Key.Divide:
                    info.Letter = '/';
                    break;
                case Key.Multiply:
                    info.Letter = '*';
                    break;
                default: {
                    info.IsLetter = false;
                    info.Letter = '\x00';
                    break;
                }
            }

            return info;
        }

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
                list.Add(new Tuple<K2, V>((K2) item.Key, item.Value));
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
                    dict.Add((K2) item.Item1, item.Item2);
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

        public static bool AreEqual<TKey, TValue>(Dictionary<TKey, TValue> first, Dictionary<TKey, TValue> second) {
            if (first == second) return true;
            if ((first == null) || (second == null)) return false;
            if (first.Count != second.Count) return false;

            var valueComparer = EqualityComparer<TValue>.Default;

            foreach (var kvp in first) {
                TValue value2;
                if (!second.TryGetValue(kvp.Key, out value2)) return false;
                if (!valueComparer.Equals(kvp.Value, value2)) return false;
            }

            return true;
        }

        public static string GetApplicationPath() {
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        public static string GetApplicationDirectory() {
            return Path.GetDirectoryName(GetApplicationPath());
        }

        public static bool StartNewApplicationInstance(string args) {
            var psi = new ProcessStartInfo(GetApplicationPath());
            psi.Arguments = args;
            psi.UseShellExecute = true;

            try {
                var process = new Process();
                process.StartInfo = psi;
                process.Start();
                return true;
            }
            catch (Exception ex) {
                Debug.WriteLine($"Failed to start new app instance: {ex}");
                return false;
            }
        }

        public static IHighlightingDefinition LoadSyntaxHighlightingFile(string filePath) {
            try {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new XmlTextReader(stream);
                return HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            catch (Exception ex) {
                // MessageBox.Show($"Failed to load syntax file {filePath}");
            }

            return null;
        }

        public static void Swap<T>(ref T a, ref T b) {
            var temp = a;
            a = b;
            b = temp;
        }

        public static void EnableControl(UIElement control, double opacity = 1.0) {
            control.IsEnabled = true;
            control.IsHitTestVisible = true;
            control.Focusable = true;
            control.Opacity = opacity;
        }

        public static void DisableControl(UIElement control, double opacity = 1.0) {
            control.IsEnabled = false;
            control.IsHitTestVisible = false;
            control.Focusable = false;
            control.Opacity = opacity;
        }

        public static string GetAutoSaveFilePath() {
            string AUTO_SAVE_TEMP_FILE = "compiler_studio_autosave.csf";
            return Path.Combine(Path.GetTempPath(), AUTO_SAVE_TEMP_FILE);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point SnapToPixels(Point point)
        {
            return new Point(Math.Round(point.X), Math.Round(point.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rect SnapToPixels(Rect rect)
        {
            return new Rect(Math.Round(rect.X), Math.Round(rect.Y), Math.Round(rect.Width), Math.Round(rect.Height));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rect SnapToPixels(Rect rect, double adjustX, double adjustY, double adjustWidth, double adjustHeight)
        {
            return new Rect(Math.Round(rect.X + adjustX), Math.Round(rect.Y + adjustY), 
                Math.Round(rect.Width + adjustWidth), Math.Round(rect.Height + adjustHeight));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rect SnapToPixelsRect(double x, double y, double width, double height)
        {
            return new Rect(Math.Round(x), Math.Round(y), Math.Round(width), Math.Round(height));
        }

        public struct KeyCharInfo {
            public bool IsLetter;
            public char Letter;
            public bool IsShift;
            public bool IsControl;
            public bool IsAlt;
        }
    }
}
