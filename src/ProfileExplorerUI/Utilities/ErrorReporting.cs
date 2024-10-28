// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace ProfileExplorer.UI;

public static class ErrorReporting {
  public static string CreateStackTraceDump(string stackTrace) {
    var time = DateTime.Now;
    string fileName = $"ProfileExplorer-{time.Month}.{time.Day}-{time.Hour}.{time.Minute}.log";
    string folderPath =
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProfileExplorer");
    string path = Path.Combine(folderPath, fileName);
    Directory.CreateDirectory(folderPath);
    File.WriteAllText(path, stackTrace);
    return path;
  }

  public static string CreateSectionDump() {
    var time = DateTime.Now;
    string fileName = $"ProfileExplorer-{time.Month}.{time.Day}-{time.Hour}.{time.Minute}.ir";
    string folderPath =
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProfileExplorer");
    string path = Path.Combine(folderPath, fileName);
    Directory.CreateDirectory(folderPath);

    var window = Application.Current.MainWindow as MainWindow;
    var builder = new StringBuilder();

    if (window == null) {
      builder.AppendLine(">> COULD NOT FIND MAIN WINDOW <<");
      File.WriteAllText(path, builder.ToString());
      return path;
    }

    if (window.OpenDocuments == null) {
      File.WriteAllText(path, builder.ToString());
      return path;
    }

    foreach (var document in window.OpenDocuments) {
      if (document.Section == null) {
        builder.AppendLine(">> MISSING DOCUMENT SECTION <<");
      }
      else if (document.Section.ParentFunction == null) {
        builder.AppendLine(">> MISSING DOCUMENT FUNCTION <<");
      }
      else {
        builder.AppendLine(
          $"IR for section {document.Section.Name} in func. {document.Section.ParentFunction.Name}");

        builder.AppendLine(document.Text);
        builder.AppendLine();
      }
    }

    File.WriteAllText(path, builder.ToString());
    return path;
  }

  public static void
    LogUnhandledException(Exception exception, string source, bool showUIPrompt = true) {
    string message = $"Unhandled exception:\n{exception}";

    if (showUIPrompt) {
      MessageBox.Show(message, "Profile Explorer Crash", MessageBoxButton.OK, MessageBoxImage.Error,
                      MessageBoxResult.OK, MessageBoxOptions.None);
    }

    try {
      string stackTrace = exception.StackTrace;

      if (exception.InnerException != null) {
        stackTrace += $"\n----------------------------\n";
        stackTrace += $"\nInner exception:";
        stackTrace += $"\n    Message: {exception.InnerException.Message}";
        stackTrace += $"\n    Stack trace: {exception.InnerException.StackTrace}";
      }

      // Report exception to telemetry service.
      string trace = $"Message: {exception.Message}\nStack trace:\n{stackTrace}";
      trace += $"\n----------------------------\n";
      trace += $"\n{GetEnvironmentVersionInfo()}";

      string stackTracePath = CreateStackTraceDump(trace);
      string sectionPath = CreateSectionDump();

      if (showUIPrompt) {
        MessageBox.Show(
          $"Crash information written to:\n{sectionPath}\n{stackTracePath}",
          "Profile Explorer Crash", MessageBoxButton.OK, MessageBoxImage.Information);

        Utils.OpenExplorerAtFile(stackTracePath);

        // Show auto-saved backup info.
        string autosavePath = Utils.GetAutoSaveFilePath();

        if (File.Exists(autosavePath)) {
          MessageBox.Show($"Current session auto-saved to: {autosavePath}",
                          "Profile Explorer Crash", MessageBoxButton.OK,
                          MessageBoxImage.Information);

          Utils.OpenExplorerAtFile(autosavePath);
        }
      }
    }
    catch (Exception ex) {
      if (showUIPrompt) {
        MessageBox.Show($"Failed to save crash information: {ex}");
      }
    }

    Environment.Exit(-1);
  }

  private static string GetEnvironmentVersionInfo() {
    string buildNumber = "unknown";

    try {
      RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
      buildNumber = registryKey.GetValue("UBR").ToString();
    }
    catch { }

    var version = Environment.OSVersion.Version;
    var trace = $"Windows version: {version.Major}.{version.Minor} (Build {version.Build}.{buildNumber})";
    trace += $"\n.NET version: {RuntimeEnvironment.GetSystemVersion()}";
    return trace;
  }

  public static void SaveOpenSections() {
    try {
      string sectionPath = CreateSectionDump();
      Utils.OpenExplorerAtFile(sectionPath);
    }
    catch (Exception ex) {
      MessageBox.Show($"Failed to save crash information: {ex}");
    }
  }
}