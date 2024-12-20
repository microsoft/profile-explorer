// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;
using ProfileExplorer.Core;
using ProfileExplorer.Core.IR;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.UI.Controls;
using Xceed.Wpf.Toolkit.Core.Utilities;

namespace ProfileExplorer.UI;

public static class Utils {
  private static readonly TaskFactory TaskFactoryInstance = new(CancellationToken.None,
                                                                TaskCreationOptions.None,
                                                                TaskContinuationOptions.None,
                                                                TaskScheduler.Default);
  private static readonly string HTML_COPY_HEADER =
    "Version:0.9\r\n" +
    "StartHTML:{0:0000000000}\r\n" +
    "EndHTML:{1:0000000000}\r\n" +
    "StartFragment:{2:0000000000}\r\n" +
    "EndFragment:{3:0000000000}\r\n";
  private static readonly string HTML_COPY_START =
    "<html>\r\n" +
    "<body>\r\n" +
    "<!--StartFragment-->";
  private static readonly string HTML_COPY_END =
    "<!--EndFragment-->\r\n" +
    "</body>\r\n" +
    "</html>";

  public static TResult RunSync<TResult>(Func<Task<TResult>> func) {
    return TaskFactoryInstance.StartNew<Task<TResult>>(func).
      Unwrap<TResult>().GetAwaiter().GetResult();
  }

  public static void RunSync(Func<Task> func) {
    TaskFactoryInstance.StartNew<Task>(func).Unwrap().GetAwaiter().GetResult();
  }

  public static bool IsShiftModifierActive() {
    return (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
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

  public static string GetAutoSaveFilePath() {
    string AUTO_SAVE_TEMP_FILE = "autosave.pex";
    return Path.Combine(Path.GetTempPath(), AUTO_SAVE_TEMP_FILE);
  }

  public static void WaitForDebugger(bool showMessageBox = true) {
#if DEBUG
    if (Debugger.IsAttached) {
      return;
    }

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
#endif
  }

  //? TODO: Replace MessageBox.Show all over the app
  public static MessageBoxResult ShowMessageBox(string text, FrameworkElement owner) {
    return ShowMessageBox(text, owner, MessageBoxButton.OK, MessageBoxImage.Information);
  }

  public static MessageBoxResult ShowWarningMessageBox(string text, FrameworkElement owner) {
    return ShowMessageBox(text, owner, MessageBoxButton.OK, MessageBoxImage.Warning);
  }

  public static MessageBoxResult ShowErrorMessageBox(string text, FrameworkElement owner) {
    return ShowMessageBox(text, owner, MessageBoxButton.OK, MessageBoxImage.Error);
  }

  public static MessageBoxResult ShowYesNoMessageBox(string text, FrameworkElement owner) {
    return ShowMessageBox(text, owner, MessageBoxButton.YesNo, MessageBoxImage.Question);
  }

  public static MessageBoxResult ShowYesNoCancelMessageBox(string text, FrameworkElement owner) {
    return ShowMessageBox(text, owner, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
  }

  private static MessageBoxResult ShowMessageBox(string text, FrameworkElement owner, MessageBoxButton buttons,
                                                 MessageBoxImage image) {
    if (owner != null) {
      using var centerForm = new DialogCenteringHelper(owner);
      return MessageBox.Show(Application.Current.MainWindow, text, "Profile Explorer", buttons, image,
                             MessageBoxResult.OK);
    }
    else {
      // Likely called from a non-UI thread, show through the Dispatcher.
      return Application.Current.Dispatcher.Invoke(() => MessageBox.Show(Application.Current.MainWindow, text,
                                                                         "Profile Explorer", buttons,
                                                                         image, MessageBoxResult.OK));
    }
  }

  public static bool TryDeleteFile(string path) {
    if (!File.Exists(path)) {
      return false;
    }

    try {
      File.Delete(path);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to delete {path}: {ex.Message}");
      return false;
    }

    return true;
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
      MessageBox.Show($"Could not find {fileType} file {path}", "Profile Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Exclamation);

      box.Focus();
      return false;
    }

    return true;
  }

  public static bool ValidateOptionalFilePath(string path, AutoCompleteBox box, string fileType,
                                              FrameworkElement owner) {
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

    bool? result = fileDialog.ShowDialog();

    if (result.HasValue && result.Value) {
      return fileDialog.FileName;
    }

    return null;
  }

  public static bool ShowOpenFileDialog(AutoCompleteBox box, string filter, string defaultExt = "*.*",
                                        string title = "Open") {
    string path = ShowOpenFileDialog(filter, defaultExt, title);

    if (path != null) {
      box.Text = path;
      return true;
    }

    return false;
  }

  public static bool ShowExecutableOpenFileDialog(AutoCompleteBox box) {
    return ShowOpenFileDialog(box, "Executables|*.exe|All Files|*.*");
  }

  public static bool ShowOpenFileDialog(TextBox box, string filter, string defaultExt = "*.*", string title = "Open") {
    string path = ShowOpenFileDialog(filter, defaultExt, title);

    if (path != null) {
      box.Text = path;
      return true;
    }

    return false;
  }

  public static bool ShowOpenFileDialog(string filter, string defaultExt, string title,
                                        Action<string> setOutput) {
    string path = ShowOpenFileDialog(filter, defaultExt, title);

    if (path != null) {
      setOutput(path);
      return true;
    }

    return false;
  }

  public static async Task<bool> ShowOpenFileDialogAsync(string filter, string defaultExt, string title,
                                                         Func<string, Task> setOutput) {
    string path = ShowOpenFileDialog(filter, defaultExt, title);

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

    bool? result = fileDialog.ShowDialog();

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
        path[^1] == '"') {
      path = path.Substring(1, path.Length - 2);
    }

    return path;
  }

  public static bool OpenExternalFile(string path, bool checkFileExists = true) {
    if (checkFileExists && !File.Exists(path)) {
      return false;
    }

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

  public static bool OpenURL(string path) {
    return OpenExternalFile(path, false);
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
      using var process = new Process {StartInfo = procInfo, EnableRaisingEvents = true};

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
        Trace.TraceError($"  Output:\n{outputText}");
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
    string vswherePath = @"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe";
    string vswhereArgs =
      @"-latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath";

    vswherePath = Environment.ExpandEnvironmentVariables(vswherePath);

    try {
      string vsPath = ExecuteToolWithOutput(vswherePath, vswhereArgs);

      if (string.IsNullOrEmpty(vsPath)) {
        Trace.TraceError("Failed to run vswhere");
        return null;
      }

      vsPath = vsPath.SplitLinesRemoveEmpty().First();
      string msvcPathInfo = Path.Combine(vsPath, @"VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt");
      string msvcVersion = File.ReadLines(msvcPathInfo).First();
      return Path.Combine(vsPath, @"VC\Tools\MSVC", msvcVersion, @"bin\HostX64\x64");
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to find MSVC path: {ex.Message}");
    }

    return null;
  }

  public static DebugFileSearchResult LocateDebugInfoFile(string imagePath, string extension) {
    try {
      if (!File.Exists(imagePath)) {
        return DebugFileSearchResult.None;
      }

      string path = Path.GetDirectoryName(imagePath);
      string imageName = Path.GetFileNameWithoutExtension(imagePath);
      string pdbPath = Path.Combine(path, imageName) + extension;

      if (File.Exists(pdbPath)) {
        return DebugFileSearchResult.Success(pdbPath);
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to find debug file for {imagePath}: {ex.Message}");
    }

    return DebugFileSearchResult.None;
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

  public static long ComputeDirectorySize(string path, bool recursive = false) {
    try {
      if (!Directory.Exists(path)) {
        return 0;
      }

      long total = 0;

      foreach (string file in Directory.EnumerateFileSystemEntries(path)) {
        if (!File.GetAttributes(file).HasFlag(FileAttributes.Directory)) {
          total += new FileInfo(file).Length;
        }
        else if (recursive) {
          total += ComputeDirectorySize(file, true);
        }
      }

      return total;
    }
    catch {
      return 0;
    }
  }

  public static void OpenExplorerAtFile(string path) {
    if (!File.Exists(path) && !Directory.Exists(path)) {
      return;
    }

    try {
      Process.Start("explorer.exe", "/select, " + path);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to start explorer.exe for {path}: {ex.Message}");
    }
  }

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
      if (logicalRoot is ContextMenu cmenu) {
        cmenu.IsOpen = false;
        break;
      }

      if (logicalRoot is Menu menu) {
        break;
      }
      else if (logicalRoot is MenuItem menuItem) {
        // Close each submenu until reaching the entry menu.
        menuItem.IsSubmenuOpen = false;
      }

      if (logicalRoot is Popup popup) {
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

  public static T FindParent<T>(DependencyObject child, string parentName = null)
    where T : DependencyObject {
    if (child == null) {
      return null;
    }

    T foundParent = null;
    var currentParent = VisualTreeHelper.GetParent(child);

    while (currentParent != null) {
      var element = currentParent as FrameworkElement;

      if (element is T && (parentName == null || element?.Name == parentName)) {
        foundParent = (T)currentParent;
        break;
      }

      currentParent = VisualTreeHelper.GetParent(currentParent);
    }

    return foundParent;
  }

  public static FrameworkElement FindContextMenuParent(DependencyObject child) {
    if (child == null) {
      return null;
    }

    var currentParent = child;

    do {
      var element = currentParent as FrameworkElement;

      if (element?.ContextMenu != null) {
        return element;
      }

      currentParent = VisualTreeHelper.GetParent(currentParent);
    } while (currentParent != null);

    return null;
  }

  public static bool ShowContextMenu(FrameworkElement element, object dataContext) {
    var host = FindContextMenuParent(element);

    if (host != null) {
      host.ContextMenu.DataContext = dataContext;
      host.ContextMenu.PlacementTarget = element;
      host.ContextMenu.IsOpen = true;
      return true;
    }

    return false;
  }

  public static Point CoordinatesToScreen(Point point, UIElement control) {
    var source = PresentationSource.FromVisual(control);

    if (source == null) {
      return point;
    }

    if (source.CompositionTarget != null) {
      var transform = source.CompositionTarget.TransformFromDevice;
      return transform.Transform(point);
    }

    return point;
  }

  public static IntPtr GetHwnd(UIElement target) {
    var source = PresentationSource.FromVisual(target) as HwndSource;
    return source?.Handle ?? IntPtr.Zero;
  }

  public static void BringToFront(UIElement target, double width, double height) {
    IntPtr handle = GetHwnd(target);

    if (NativeMethods.GetWindowRect(handle, out var rect)) {
      NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOP,
                                 rect.Left, rect.Top, (int)width, (int)height,
                                 NativeMethods.TOPMOST_FLAGS);
    }
  }

  public static void SetAlwaysOnTop(UIElement target, bool value, double width, double height) {
    IntPtr handle = GetHwnd(target);

    if (NativeMethods.GetWindowRect(handle, out var rect)) {
      NativeMethods.SetWindowPos(handle,
                                 value ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                                 rect.Left, rect.Top, (int)width, (int)height,
                                 NativeMethods.TOPMOST_FLAGS);
    }
  }

  public static void SendToBack(UIElement target, double width, double height) {
    IntPtr handle = GetHwnd(target);

    if (NativeMethods.GetWindowRect(handle, out var rect)) {
      NativeMethods.SetWindowPos(handle, NativeMethods.HWND_NOTOPMOST,
                                 rect.Left, rect.Top, (int)width, (int)height,
                                 NativeMethods.TOPMOST_FLAGS);
    }
  }

  public static Point CoordinatesFromScreen(Point point, UIElement control) {
    var source = PresentationSource.FromVisual(control);

    if (source == null) {
      return point;
    }

    if (source.CompositionTarget != null) {
      var transform = source.CompositionTarget.TransformToDevice;
      return transform.Transform(point);
    }

    return point;
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
    try {
      return (Color)ColorConverter.ConvertFromString(color);
    }
    catch (Exception ex) {
      return Colors.Transparent;
    }
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

  public static KeyCharInfo KeyToChar(Key key) {
    var info = new KeyCharInfo();
    info.IsAlt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
    info.IsControl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
    info.IsShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
    info.IsLetter = !info.IsAlt && !info.IsControl;
    bool isUpperCase = Console.CapsLock && !info.IsShift || !Console.CapsLock && info.IsShift;

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
  public static Rect SnapRectToPixels(Rect rect, double adjustX, double adjustY, double adjustWidth,
                                      double adjustHeight) {
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

  public static void UpdateMaxMenuItemWidth(string title, ref double maxWidth, MenuItem targetMenu) {
    double width = MeasureString(title, targetMenu).Width + 20;
    maxWidth = Math.Max(width, maxWidth);
  }

  public static Size MeasureString(string text, Control targetControl) {
    var font = new Typeface(targetControl.FontFamily, targetControl.FontStyle,
                            targetControl.FontWeight, targetControl.FontStretch);
    return MeasureString(text, font, targetControl.FontSize);
  }

  public static Size MeasureString(string text, string fontName, double fontSize,
                                   FontWeight? fontWeight = null) {
    var font = new Typeface(new FontFamily(fontName), FontStyles.Normal,
                            fontWeight ?? FontWeights.Normal, FontStretches.Normal);
    return MeasureString(text, font, fontSize);
  }

  public static Size MeasureString(string text, Typeface font, double fontSize) {
    var formattedText = new FormattedText(text, CultureInfo.CurrentCulture,
                                          FlowDirection.LeftToRight, font, fontSize, Brushes.Black,
                                          new NumberSubstitution(), 1);
    return new Size(formattedText.WidthIncludingTrailingWhitespace, formattedText.Height);
  }

  public static Size MeasureString(int letterCount, string fontName, double fontSize,
                                   FontWeight? fontWeight = null) {
    string dummyString = new('X', letterCount);
    return MeasureString(dummyString, fontName, fontSize, fontWeight);
  }

  public static Size MeasureString(int letterCount, Typeface font, double fontSize) {
    if (letterCount == 1) {
      return MeasureString("X", font, fontSize);
    }

    string dummyString = new('X', letterCount);
    return MeasureString(dummyString, font, fontSize);
  }

  public static void SelectEditableListViewItem(ListView listView, int index) {
    listView.SelectedItem = null;
    listView.SelectedIndex = index;
    listView.UpdateLayout(); // Force the ListView to generate the containers.
    var item = listView.ItemContainerGenerator.ContainerFromIndex(index) as ListViewItem;

    if (item != null) {
      var textBox = FindChild<TextBox>(item);

      if (textBox != null) {
        textBox.Focus();
        textBox.SelectAll();
        Keyboard.Focus(textBox);
      }
    }
  }

  public static ListViewItem FindPointedListViewItem(ListView listView) {
    var mousePosition = Mouse.GetPosition(listView);
    var result = listView.GetObjectAtPoint<ListViewItem>(mousePosition);
    return result;
  }

  public static void SelectTextBoxListViewItem(TextBox textBox, ListView listView) {
    if (textBox.IsKeyboardFocused) {
      return;
    }

    textBox.Focus();
    FocusParentListViewItem(textBox, listView);
  }

  public static void SelectTextBoxListViewItem(FileSystemTextBox textBox, ListView listView) {
    if (textBox.IsKeyboardFocused) {
      return;
    }

    textBox.Focus();
    FocusParentListViewItem(textBox, listView);
  }

  public static ListViewItem FocusParentListViewItem(Control control, ListView listView) {
    // Try to move selection to the item in the list view.
    var listViewItem = VisualTreeHelperEx.FindAncestorByType<ListViewItem>(control);

    if (listViewItem != null) {
      listView.SelectedItem = null;
      listViewItem.IsSelected = true;
    }

    return listViewItem;
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

  public static void ScrollToFirstListViewItem(ListView listView, int itemIndex = 0) {
    // Based on https://stackoverflow.com/a/211984.
    // This is a hack to scroll to an item in a ListView.
    if (listView.Items.Count > 0) {
      var vsp =
        (VirtualizingStackPanel)typeof(ItemsControl).InvokeMember("_itemsHost",
                                                                  BindingFlags.Instance | BindingFlags.GetField |
                                                                  BindingFlags.NonPublic, null,
                                                                  listView, null);

      if (vsp != null) {
        double scrollHeight = vsp.ScrollOwner.ScrollableHeight;
        double offset = scrollHeight * itemIndex / listView.Items.Count;
        vsp.SetVerticalOffset(offset);
      }
    }
  }

  public static bool EventSourceIsTextBox(MouseButtonEventArgs e) {
    return e.OriginalSource.GetType().Name == "TextBoxView";
  }

  public static bool IsOptionsUpdateEvent(MouseButtonEventArgs e) {
    string sourceName = e.OriginalSource.GetType().Name;
    return IsOptionsUpdateEvent(sourceName);
  }

  public static bool IsOptionsUpdateEvent(KeyEventArgs e) {
    string sourceName = e.OriginalSource.GetType().Name;
    return IsOptionsUpdateEvent(sourceName);
  }

  private static bool IsOptionsUpdateEvent(string sourceName) {
    return sourceName != "Button" &&
           sourceName != "ToggleButton" &&
           sourceName != "TextBox" &&
           sourceName != "TextBoxView" &&
           sourceName != "TextBlock";
  }

  public static Typeface GetTextTypeface(Control element) {
    return new Typeface(element.FontFamily, element.FontStyle,
                        element.FontWeight, element.FontStretch);
  }

  public static void CopyHtmlToClipboard(string html, string text = null) {
    try {
      var dataObject = new DataObject();
      dataObject.SetData(DataFormats.Html, ConvertHtmlToClipboardFormat(html));

      if (!string.IsNullOrEmpty(text)) {
        dataObject.SetData(DataFormats.Text, text);
      }

      Clipboard.SetDataObject(dataObject);
    }
    catch (Exception ex) {
      Trace.WriteLine($"Failed to copy HTML to clipboard: {ex.Message}");
    }
  }

  public static string ConvertHtmlToClipboardFormat(string html) {
    var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    byte[] data = Array.Empty<byte>();
    byte[] header = encoding.GetBytes(string.Format(HTML_COPY_HEADER, 0, 1, 2, 3));
    data = data.Concat(header).ToArray();

    int startHtml = data.Length;
    data = data.Concat(encoding.GetBytes(HTML_COPY_START)).ToArray();

    int startFragment = data.Length;
    data = data.Concat(encoding.GetBytes(html)).ToArray();
    int endFragment = data.Length;
    data = data.Concat(encoding.GetBytes(HTML_COPY_END)).ToArray();

    int endHtml = data.Length;
    byte[] newHeader = encoding.GetBytes(
      string.Format(HTML_COPY_HEADER, startHtml, endHtml, startFragment, endFragment));
    Array.Copy(newHeader, data, startHtml);
    return encoding.GetString(data);
  }

  public static string RemovePathQuotes(string text) {
    if (string.IsNullOrEmpty(text)) {
      return text;
    }

    return text.Trim('\"');
  }

  public struct KeyCharInfo {
    public bool IsLetter;
    public char Letter;
    public bool IsShift;
    public bool IsControl;
    public bool IsAlt;
  }
}