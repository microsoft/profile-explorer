// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Core.UTC;

namespace Client {
    public static class ErrorReporting {
        public static string CreateMiniDump() {
            var time = DateTime.Now;
            var fileName = $"CompilerStudio-{time.Month}.{time.Day}-{time.Hour}.{time.Minute}.dmp";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);

            using (var stream = new FileStream(path, FileMode.Create,
                                               FileAccess.ReadWrite, FileShare.Write)) {
                Minidump.WriteDump(stream.SafeFileHandle,
                                   Minidump.Option.WithFullAuxiliaryState |
                                   Minidump.Option.WithFullMemory |
                                   Minidump.Option.WithFullMemoryInfo |
                                   Minidump.Option.WithHandleData |
                                   Minidump.Option.WithThreadInfo |
                                   Minidump.Option.WithProcessThreadData);
            }

            return path;
        }

        public static string CreateStackTraceDump(string stackTrace) {
            var time = DateTime.Now;
            var fileName = $"CompilerStudio-{time.Month}.{time.Day}-{time.Hour}.{time.Minute}.trace";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);

            File.WriteAllText(path, stackTrace);
            return path;
        }

        public static string CreateSectionDump() {
            var time = DateTime.Now;
            var fileName = $"CompilerStudio-{time.Month}.{time.Day}-{time.Hour}.{time.Minute}.ir";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);

            var window = App.Current.MainWindow as Client.MainWindow;
            var builder = new StringBuilder();

            if (window == null) {
                builder.AppendLine(">> COULD NOT FIND MAIN WINDOW <<");
                File.WriteAllText(path, builder.ToString());
                return path;
            }

            if(window.OpenDocuments == null) {
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
                    builder.AppendLine($"IR for section {document.Section.Name} in func. {document.Section.ParentFunction.Name}");
                    builder.AppendLine(UTCReader.SECTION_START);
                    builder.AppendLine(document.Text);
                    builder.AppendLine();
                }
            }

            File.WriteAllText(path, builder.ToString());
            return path;
        }

        public static void LogUnhandledException(Exception exception, string source, bool showUIPrompt = true) {
            var message = $"Unhandled exception:\n{exception}";

            if (showUIPrompt) {
                MessageBox.Show(message, "Compiler Studio Crash",
                                MessageBoxButton.OK, MessageBoxImage.Error,
                                MessageBoxResult.OK, MessageBoxOptions.None);
            }

            try {
                var stackTrace = exception.StackTrace;

                if (exception.InnerException != null) {
                    stackTrace += $"\n\nInner exception: {exception.InnerException.StackTrace}";
                }

                var stackTracePath = CreateStackTraceDump(stackTrace);
                var sectionPath = CreateSectionDump();
                var minidumpPath = CreateMiniDump();

                if (showUIPrompt) {
                    MessageBox.Show($"Crash information written to:\n{sectionPath}\n{stackTracePath}\n{minidumpPath}",
                                    "Compiler Studio Crash", MessageBoxButton.OK, MessageBoxImage.Information);
                    OpenExplorerAtFile(minidumpPath);

                    // Show auto-saved backup info.
                    var autosavePath = Utils.GetAutoSaveFilePath();

                    if (File.Exists(autosavePath)) {
                        MessageBox.Show($"Current session auto-saved to: {autosavePath}",
                                        "Compiler Studio Crash", MessageBoxButton.OK, MessageBoxImage.Information);
                        OpenExplorerAtFile(autosavePath);
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

        public static void SaveOpenSections() {
            try {
                var sectionPath = CreateSectionDump();
                OpenExplorerAtFile(sectionPath);
            }
            catch (Exception ex) {
                MessageBox.Show($"Failed to save crash information: {ex}");
            }
        }

        private static void OpenExplorerAtFile(string path) {
            Process.Start("explorer.exe", "/select, " + path);
        }
    }
}
