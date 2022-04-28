// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using IRExplorerCore;
using IRExplorerCore.Analysis;
using IRExplorerCore.IR;
using Microsoft.Win32;

namespace IRExplorerUI {
    static class Utils {
        public static UIElement FindParentHost(UIElement control) {
            var logicalRoot = LogicalTreeHelper.GetParent(control);

            while (logicalRoot != null) {
                if (logicalRoot is UserControl || logicalRoot is Window) {
                    break;
                }

                logicalRoot = LogicalTreeHelper.GetParent(logicalRoot);
            }

            return logicalRoot as UIElement;
        }

        public static void CloseParentMenu(UIElement control) {
            // Close the context menu hosting the control.
            var logicalRoot = LogicalTreeHelper.GetParent(control);

            while (logicalRoot != null) {
                if (logicalRoot is ContextMenu menu) {
                    menu.IsOpen = false;
                    break;
                }
                else if (logicalRoot is Popup popup) {
                    popup.IsOpen = false;
                    break;
                }

                logicalRoot = LogicalTreeHelper.GetParent(logicalRoot);
            }
        }

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
                        foundChild = (T)child;
                        break;
                    }
                }
                else {
                    // child element found.
                    foundChild = (T)child;
                    break;
                }
            }

            return foundChild;
        }


        public static T FindChildLogical<T>(DependencyObject parent, string childName = null)
            where T : DependencyObject {
            // Confirm parent and childName are valid. 
            if (parent == null) {
                return null;
            }

            T foundChild = null;
            foreach (object rawChild in LogicalTreeHelper.GetChildren(parent)) {
                var child = rawChild as DependencyObject;

                if (child == null) {
                    continue;
                }

                // If the child is not of the request child type child
                var childType = child as T;

                if (childType == null) {
                    // recursively drill down the tree
                    foundChild = FindChildLogical<T>(child, childName);

                    // If the child is found, break so we do not overwrite the found child. 
                    if (foundChild != null) {
                        break;
                    }
                }
                else if (!string.IsNullOrEmpty(childName)) {
                    // If the child's name is set for search
                    if (child is FrameworkElement frameworkElement && frameworkElement.Name == childName) {
                        // if the child's name is of the request name
                        foundChild = (T)child;
                        break;
                    }
                }
                else {
                    // child element found.
                    foundChild = (T)child;
                    break;
                }
            }

            return foundChild;
        }

        public static Point CoordinatesToScreen(Point point, UIElement control) {
            var source = PresentationSource.FromVisual(control);

            if (source == null) {
                return point;
            }

            var transform = source.CompositionTarget.TransformFromDevice;
            return transform.Transform(point);
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

        public static HighlightingStyle GetSelectedColorStyle(SelectedColorEventArgs args, Pen pen = null) {
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
            return Color.FromArgb(color.A, (byte)Math.Clamp(color.R * adjustment, 0, 255),
                                  (byte)Math.Clamp(color.G * adjustment, 0, 255),
                                  (byte)Math.Clamp(color.B * adjustment, 0, 255));
        }

        public static int EstimateBrightness(Color color) {
            return (int)Math.Sqrt(
               color.R * color.R * .241 +
               color.G * color.G * .691 +
               color.B * color.B * .068);
        }

        public static Color ColorFromString(string color) {
            return (Color)ColorConverter.ConvertFromString(color);
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

            if (block.HasLabel && !string.IsNullOrEmpty(block.Label.Name)) {
                return $"B{block.Number} ({block.Label.Name})";
            }

            return $"B{block.Number}";
        }

        //? TODO: This should be part of the IR NameProvider
        public static string MakeElementDescription(IRElement element) {
            switch (element) {
                case BlockIR block:
                    return MakeBlockDescription(block);
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
                    string text = GetSymbolName(op);

                    switch (op.Kind) {
                        case OperandKind.Address:
                        case OperandKind.LabelAddress: {
                            text = $"&{text}";
                            break;
                        }
                        case OperandKind.Indirection: {
                            text = $"[{MakeElementDescription(op.IndirectionBaseValue)}]";
                            break;
                        }
                        case OperandKind.IntConstant: {
                            text = op.IntValue.ToString();
                            break;
                        }
                        case OperandKind.FloatConstant: {
                            text = op.FloatValue.ToString();
                            break;
                        }
                    }

                    var ssaTag = op.GetTag<ISSAValue>();

                    if (ssaTag != null) {
                        text += $"<{ssaTag.DefinitionId}>";
                    }

                    return text;
                }
                default:
                    return element.ToString();
            }
        }
        
        //? TODO: This should be part of the IR NameProvider
        public static string GetSymbolName(OperandIR op) {
            if (op.HasName) {
                return op.NameValue.ToString();
            }

            return "";
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
        
        public static IHighlightingDefinition LoadSyntaxHighlightingFile(string filePath) {
            if (string.IsNullOrEmpty(filePath)) {
                return null; // File couldn't be loaded.
            }

            try {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new XmlTextReader(stream);
                return HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to load syntax file {filePath}: {ex}");
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
        public static double SnapToPixels(double value) {
            return Math.Round(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point SnapToPixels(Point point) {
            return new Point(Math.Round(point.X), Math.Round(point.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rect SnapToPixels(Rect rect) {
            return new Rect(Math.Round(rect.X), Math.Round(rect.Y), Math.Round(rect.Width), Math.Round(rect.Height));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rect SnapRectToPixels(Rect rect, double adjustX, double adjustY, double adjustWidth, double adjustHeight) {
            return new Rect(Math.Round(rect.X + adjustX), Math.Round(rect.Y + adjustY),
                Math.Round(rect.Width + adjustWidth), Math.Round(rect.Height + adjustHeight));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rect SnapRectToPixels(double x, double y, double width, double height) {
            return new Rect(Math.Round(x), Math.Round(y), Math.Round(width), Math.Round(height));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point SnapPointToPixels(double x, double y) {
            return new Point(Math.Round(x), Math.Round(y));
        }

        public struct KeyCharInfo {
            public bool IsLetter;
            public char Letter;
            public bool IsShift;
            public bool IsControl;
            public bool IsAlt;
        }

        public static void WaitForDebugger(bool showMessageBox = false) {
            if (showMessageBox) {
                MessageBox.Show($"Waiting for debugger PID {Environment.ProcessId}", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            while (!Debugger.IsAttached) {
                Thread.Sleep(1000);
                Trace.TraceWarning(".");
                Trace.Flush();
            }

            Debugger.Break();
        }

        public static string TryGetFileName(string path) {
            try {
                if (string.IsNullOrEmpty(path)) {
                    return "";
                }

                return Path.GetFileName(path);
            }
            catch (Exception ex) {
                return "";
            }
        }

        public static string TryGetFileNameWithoutExtension(string path) {
            try {
                if (string.IsNullOrEmpty(path)) {
                    return "";
                }

                return Path.GetFileNameWithoutExtension(path);
            }
            catch (Exception ex) {
                return "";
            }
        }

        public static string TryGetDirectoryName(string path) {
            try {
                if (string.IsNullOrEmpty(path)) {
                    return "";
                }

                if (!Directory.Exists(path)) {
                    path = Path.GetDirectoryName(path);
                }

                // Remove \ at the end.
                if (path.EndsWith(Path.DirectorySeparatorChar) ||
                    path.EndsWith(Path.AltDirectorySeparatorChar)) {
                    path = path.Substring(0, path.Length - 1);
                }

                return path;
            }
            catch (Exception ex) {
                return "";
            }
        }

        public static bool ValidateFilePath(string path, AutoCompleteBox box, string fileType, FrameworkElement owner) {
            if (!File.Exists(path)) {
                using var centerForm = new DialogCenteringHelper(owner);
                MessageBox.Show($"Could not find {fileType} file {path}", "IR Explorer",
                                MessageBoxButton.OK, MessageBoxImage.Exclamation);

                box.Focus();
                return false;
            }

            return true;
        }

        public static bool ValidateOptionalFilePath(string path, AutoCompleteBox box, string fileType, FrameworkElement owner) {
            if (string.IsNullOrEmpty(path)) {
                return true;
            }

            return ValidateFilePath(path, box, fileType, owner);
        }

        public static string ShowOpenFileDialog(string filter, string defaultExt = "*.*", string title = "Open") {
            var fileDialog = new OpenFileDialog {
                Title = title,
                DefaultExt = defaultExt,
                Filter = filter
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                return fileDialog.FileName;
            }

            return null;
        }

        public static bool ShowOpenFileDialog(AutoCompleteBox box, string filter, string defaultExt = "*.*", string title = "Open") {
            var path = ShowOpenFileDialog(filter, defaultExt, title);

            if (path != null) {
                box.Text = path;
                return true;
            }

            return false;
        }

        public static bool ShowOpenFileDialog(TextBox box, string filter, string defaultExt = "*.*", string title = "Open") {
            var path = ShowOpenFileDialog(filter, defaultExt, title);

            if (path != null) {
                box.Text = path;
                return true;
            }

            return false;
        }

        public static bool ShowOpenFileDialog(string filter, string defaultExt, string title,
                                              Action<string> setOutput) {
            var path = ShowOpenFileDialog(filter, defaultExt, title);

            if (path != null) {
                setOutput(path);
                return true;
            }

            return false;
        }

        public static async Task<bool> ShowOpenFileDialogAsync(string filter, string defaultExt, string title,
                                                                Func<string, Task> setOutput) {
            var path = ShowOpenFileDialog(filter, defaultExt, title);

            if (path != null) {
                await setOutput(path);
                return true;
            }

            return false;
        }

        public static string ShowSaveFileDialog(string filter, string defaultExt = "*.*", string title = "Save") {
            var fileDialog = new SaveFileDialog {
                Title = title,
                DefaultExt = defaultExt,
                Filter = filter
            };

            var result = fileDialog.ShowDialog();

            if (result.HasValue && result.Value) {
                return fileDialog.FileName;
            }

            return null;
        }

        public static string CleanupPath(string path) {
            if (string.IsNullOrEmpty(path)) {
                return path;
            }

            path = path.Trim();

            if (path.Length >= 2 &&
                path[0] == '"' &&
                path[path.Length - 1] == '"') {
                path = path.Substring(1, path.Length - 2);
            }

            return path;
        }

        public static bool OpenExternalFile(string path) {
            try {
                var psi = new ProcessStartInfo(path) {
                    UseShellExecute = true
                };

                Process.Start(psi);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to open file: {path}, exception {ex.Message}");
                return false;
            }
        }

        public static bool ExecuteTool(string path, string args, CancelableTask cancelableTask = null,
                                       Dictionary<string, string> envVariables = null) {
            return ExecuteToolWithOutput(path, args, cancelableTask, envVariables) != null;
        }

        public static string ExecuteToolWithOutput(string path, string args, CancelableTask cancelableTask = null,
                                                   Dictionary<string, string> envVariables = null) {
            if (!File.Exists(path)) {
                return null;
            }

            Trace.TraceInformation($"Executing tool {path} with args {args}");

            var outputText = new StringBuilder();
            var procInfo = new ProcessStartInfo(path) {
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = false,
                RedirectStandardOutput = true
            };

            if (envVariables != null) {
                foreach (var pair in envVariables) {
                    procInfo.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            try {
                using var process = new Process { StartInfo = procInfo, EnableRaisingEvents = true };
                
                process.OutputDataReceived += (sender, e) => {
                    outputText.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();

                do {
                    process.WaitForExit(100);

                    if (cancelableTask != null && cancelableTask.IsCanceled) {
                        Trace.TraceWarning($"Task {ObjectTracker.Track(cancelableTask)}: Canceled");
                        process.Kill();
                        return null;
                    }
                } while (!process.HasExited);

                process.CancelOutputRead();

                if (process.ExitCode != 0) {
                    Trace.TraceError($"Task {ObjectTracker.Track(cancelableTask)}: Failed with error code: {process.ExitCode}");
                    Trace.TraceError($"  Output:\n{outputText.ToString()}");
                    return null;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Task {ObjectTracker.Track(cancelableTask)}: Failed with exception: {ex.Message}");
                return null;
            }

            return outputText.ToString();
        }

        public static string DetectMSVCPath() {
            // https://devblogs.microsoft.com/cppblog/finding-the-visual-c-compiler-tools-in-visual-studio-2017/
            var vswherePath = @"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe";
            var vswhereArgs =
                @"-latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath";

            vswherePath = Environment.ExpandEnvironmentVariables(vswherePath);
            
            try {
                var vsPath = ExecuteToolWithOutput(vswherePath, vswhereArgs);

                if (string.IsNullOrEmpty(vsPath)) {
                    Trace.TraceError("Failed to run vswhere");
                    return null;
                }

                vsPath = vsPath.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).First();
                var msvcPathInfo = Path.Combine(vsPath, @"VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt");
                var msvcVersion = File.ReadLines(msvcPathInfo).First();
                return Path.Combine(vsPath, @"VC\Tools\MSVC", msvcVersion, @"bin\HostX64\x64");
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to find MSVC path: {ex.Message}");
            }

            return null;
        }

        public static string LocateDebugInfoFile(string imagePath, string extension) {
            try {
                if (!File.Exists(imagePath)) {
                    return null;
                }

                var path = Path.GetDirectoryName(imagePath);
                var pdbPath = Path.Combine(path, Path.GetFileNameWithoutExtension(imagePath)) + extension;

                if (File.Exists(pdbPath)) {
                    return pdbPath;
                }
            }
            catch (Exception ex) {

            }

            return null;
        }

        public static bool IsBinaryFile(string filePath) {
            return FileHasExtension(filePath, ".exe") ||
                   FileHasExtension(filePath, ".dll") ||
                   FileHasExtension(filePath, ".sys");
        }

        public static bool IsExecutableFile(string filePath) {
            return FileHasExtension(filePath, ".exe");
        }

        public static bool FileHasExtension(string filePath, string extension) {
            if (string.IsNullOrEmpty(filePath)) {
                return false;
            }

            try {
                extension = extension.ToLowerInvariant();

                if (!extension.StartsWith(".")) {
                    extension = $".{extension}";
                }

                return Path.GetExtension(filePath).ToLowerInvariant() == extension;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed FileHasExtension for {filePath}: {ex}");
                return false;
            }
        }

        public static string GetFileExtension(string filePath) {
            if (string.IsNullOrEmpty(filePath)) {
                return "";
            }

            try {
                return Path.GetExtension(filePath);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed GetFileExtension for {filePath}: {ex}");
                return "";
            }
        }

        public static Size MeasureString(string text, string fontName, double fontSize, 
                                         FontWeight? fontWeight = null) {
            var formattedText = new FormattedText(text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily(fontName), FontStyles.Normal,
                             fontWeight ?? FontWeights.Normal, FontStretches.Normal),
                fontSize, Brushes.Black, new NumberSubstitution(), 1);
            return new Size(formattedText.WidthIncludingTrailingWhitespace, formattedText.Height);
        }

        public static Size MeasureString(int letterCount, string fontName, double fontSize,
            FontWeight? fontWeight = null) {
            var dummyString = new string('X', letterCount);
            return MeasureString(dummyString, fontName, fontSize, fontWeight);
        }
    }
}
