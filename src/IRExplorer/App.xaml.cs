// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Client {
    public class SyntaxThemeInfo {
        public SyntaxThemeInfo(string name, string path) {
            Name = name;
            Path = path;
        }

        public string Name { get; set; }
        public string Path { get; set; }
    }

    public partial class App : Application {
        public static DateTime AppStartTime;
        public static DateTime WindowShowTime;
        public static ApplicationSettings Settings;

        private static readonly string SettingsPath = @"Microsoft\IRExplorer";
        private static readonly string SettingsFile = "IRExplorer.settings";
        private static readonly string TraceFile = "IRExplorer.trace";
        private static readonly string SyntaxHighlightingFile = @"utc.xshd";
        private static readonly string RemarkDefinitionFile = @"{0}-remark-settings.json";
        private static readonly string InternalIRSyntaxHighlightingFile = @"ir.xshd";
        private static readonly string ThemeFileDirectory = @"themes";
        private static readonly string ThemeFileExtension = @"*.xshd";

        private static bool CreateSettingsDirectory() {
            try {
                string path = GetSettingsDirectoryPath();

                if (!Directory.Exists(path)) {
                    Directory.CreateDirectory(path);
                }

                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to create settings directory: {ex}");
                return false;
            }
        }

        private static string GetSettingsDirectoryPath() {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(path, SettingsPath);
        }

        private static string GetSettingsFilePath() {
            string path = GetSettingsDirectoryPath();
            return Path.Combine(path, SettingsFile);
        }

        private static string GetTraceFilePath() {
            string path = GetSettingsDirectoryPath();
            return Path.Combine(path, TraceFile);
        }

        public static string GetSyntaxHighlightingFilePath() {
            string customFile = Settings.DocumentSettings.SyntaxHighlightingFilePath;

            if (!string.IsNullOrEmpty(customFile) && File.Exists(customFile)) {
                return customFile;
            }

            return GetDefaultSyntaxHighlightingFilePath();
        }

        public static string GetDefaultSyntaxHighlightingFilePath() {
            string appDir = Utils.GetApplicationDirectory();
            return Path.Combine(appDir, SyntaxHighlightingFile);
        }

        public static string GetInternalIRSyntaxHighlightingFilePath() {
            string appDir = Utils.GetApplicationDirectory();
            return Path.Combine(appDir, InternalIRSyntaxHighlightingFile);
        }

        public static string GetRemarksDefinitionFilePath(string compilerIRName)
        {
            string customFile = GetUserRemarksDefinitionFilePath(compilerIRName);

            if (File.Exists(customFile))
            {
                return customFile;
            }

            var internalFile = GetInternalRemarksDefinitionFilePath(compilerIRName);

            if (File.Exists(internalFile) && CreateSettingsDirectory())
            {
                try
                {
                    File.Copy(internalFile, customFile);
                }
                catch(Exception ex)
                {
                    return null;
                }

                return customFile;
            }

            return null;
        }

        public static string GetUserRemarksDefinitionFilePath(string compilerIRName)
        {
            string appDir = GetSettingsDirectoryPath();
            return Path.Combine(appDir, string.Format(RemarkDefinitionFile, compilerIRName));
        }

        public static string GetInternalRemarksDefinitionFilePath(string compilerIRName)
        {
            string appDir = Utils.GetApplicationDirectory();
            return Path.Combine(appDir, string.Format(RemarkDefinitionFile, compilerIRName));
        }

        public static List<SyntaxThemeInfo> GetSyntaxHighlightingThemes() {
            var list = new List<SyntaxThemeInfo>();

            try {
                string appDir = Utils.GetApplicationDirectory();
                var themes = Directory.GetFiles(Path.Combine(appDir, ThemeFileDirectory), ThemeFileExtension);

                foreach (string theme in themes) {
                    list.Add(new SyntaxThemeInfo(Path.GetFileNameWithoutExtension(theme), theme));
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to get theme list: {ex}");
            }

            return list;
        }

        public static bool LoadApplicationSettings() {
            try {
                CreateSettingsDirectory();
                string path = GetSettingsFilePath();
                var data = File.ReadAllBytes(path);
                Settings = StateSerializer.Deserialize<ApplicationSettings>(data);

                // Do some basic sanity checks in case the settings file is incompatible.
                if(Settings.RecentFiles == null || Settings.RecentComparedFiles == null) {
                    return false;
                }

                return true;
            }
            catch (Exception ex) {
                Settings = new ApplicationSettings();
                Trace.TraceError($"Failed to load app settings: {ex}");
                return false;
            }
        }

        public static void SaveApplicationSettings() {
            try {
                var data = StateSerializer.Serialize(Settings);
                CreateSettingsDirectory();
                string path = GetSettingsFilePath();
                File.WriteAllBytes(path, data);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to save app settings: {ex}");
            }
        }

        protected override void OnStartup(StartupEventArgs e) {
            AppStartTime = DateTime.UtcNow;
            base.OnStartup(e);

            if (!Debugger.IsAttached) {
                //? TODO: Disable UI only when the /automation arg is used
                SetupExceptionHandling(true);
            }

            // Enable file output for tracing.
            try {
                string traceFilePath = GetTraceFilePath();

                if (File.Exists(traceFilePath)) {
                    File.Delete(traceFilePath);
                }

                Trace.Listeners.Add(new TextWriterTraceListener(traceFilePath));
#if DEBUG
                Trace.AutoFlush = true;
#endif
            }
            catch (Exception ex) {
                Debug.WriteLine($"Failed to create trace file: {ex}");
            }

            if (!LoadApplicationSettings()) {
                // Failed to load settings, reset them.
                Settings = new ApplicationSettings();
            }
        }

        public void SetupExceptionHandling(bool showUIPrompt = true) {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                ErrorReporting.LogUnhandledException((Exception) e.ExceptionObject,
                                                     "AppDomain.CurrentDomain.UnhandledException",
                                                     showUIPrompt);

            Dispatcher.UnhandledException += (s, e) =>
                ErrorReporting.LogUnhandledException(e.Exception,
                                                     "Application.Current.DispatcherUnhandledException",
                                                     showUIPrompt);

            Current.DispatcherUnhandledException += (s, e) =>
                ErrorReporting.LogUnhandledException(e.Exception,
                                                     "Application.Current.DispatcherUnhandledException",
                                                     showUIPrompt);

            TaskScheduler.UnobservedTaskException += (s, e) =>
                ErrorReporting.LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException",
                                                     showUIPrompt);
        }
    }
}
