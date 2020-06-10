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

        static readonly string SettingsPath = @"Microsoft\IRExplorer";
        static readonly string SettingsFile = "IRExplorer.settings";
        static readonly string TraceFile = "IRExplorer.trace";
        static readonly string SyntaxHighlightingFile = @"utc.xshd";
        static readonly string InternalIRSyntaxHighlightingFile = @"ir.xshd";
        static readonly string ThemeFileDirectory = @"themes";
        static readonly string ThemeFileExtension = @"*.xshd";

        private static bool CreateSettingsDirectory()
        {
            try
            {
                var path = GetSettingsDirectoryPath();

                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                return true;
            }
            catch(Exception ex)
            {
                Trace.TraceError($"Failed to create settings directory: {ex}");
                return false;
            }
        }

        private static string GetSettingsDirectoryPath()
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(path, SettingsPath);
        }

        private static string GetSettingsFilePath() {
            var path = GetSettingsDirectoryPath();
            return Path.Combine(path, SettingsFile);
        }

        private static string GetTraceFilePath() {
            var path = GetSettingsDirectoryPath();
            return Path.Combine(path, TraceFile);
        }

        public static string GetSyntaxHighlightingFilePath() {
            var customFile = Settings.DocumentSettings.SyntaxHighlightingFilePath;

            if (!string.IsNullOrEmpty(customFile) &&
                File.Exists(customFile)) {
                return customFile;
            }

            return GetDefaultSyntaxHighlightingFilePath();
        }

        public static string GetDefaultSyntaxHighlightingFilePath() {
            var appDir = Utils.GetApplicationDirectory();
            return Path.Combine(appDir, SyntaxHighlightingFile);
        }

        public static string GetInternalIRSyntaxHighlightingFilePath() {
            var appDir = Utils.GetApplicationDirectory();
            return Path.Combine(appDir, InternalIRSyntaxHighlightingFile);
        }

        public static List<SyntaxThemeInfo> GetSyntaxHighlightingThemes() {
            var list = new List<SyntaxThemeInfo>();

            try {
                var appDir = Utils.GetApplicationDirectory();
                var themes = Directory.GetFiles(Path.Combine(appDir, ThemeFileDirectory), ThemeFileExtension);
                
                foreach(var theme in themes) {
                    list.Add(new SyntaxThemeInfo(Path.GetFileNameWithoutExtension(theme), theme));
                }
            }
            catch(Exception ex) {
                Trace.TraceError($"Failed to get theme list: {ex}");
            }

            return list;
        }

        public static void LoadApplicationSettings() {
            try {
                CreateSettingsDirectory();
                var path = GetSettingsFilePath();
                var data = File.ReadAllBytes(path);
                Settings = StateSerializer.Deserialize<ApplicationSettings>(data);
            }
            catch (Exception ex) {
                Settings = new ApplicationSettings();
                Trace.TraceError($"Failed to load app settings: {ex}");
            }
        }


        public static void SaveApplicationSettings() {
            try {
                var data = StateSerializer.Serialize<ApplicationSettings>(Settings);
                CreateSettingsDirectory();
                var path = GetSettingsFilePath();
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
                SetupExceptionHandling(showUIPrompt: true);
            }

#if DEBUG
            // Enable file output for tracing.
            try
            {
                var traceFilePath = GetTraceFilePath();

                if (File.Exists(traceFilePath))
                {
                    File.Delete(traceFilePath);
                }

                Trace.Listeners.Add(new TextWriterTraceListener(traceFilePath));
                Trace.AutoFlush = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create trace file: {ex}");
            }
#endif
            LoadApplicationSettings();
        }

        public void SetupExceptionHandling(bool showUIPrompt = true) {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                ErrorReporting.LogUnhandledException((Exception)e.ExceptionObject,
                "AppDomain.CurrentDomain.UnhandledException", showUIPrompt);

            Dispatcher.UnhandledException += (s, e) =>
                ErrorReporting.LogUnhandledException(e.Exception,
                "Application.Current.DispatcherUnhandledException", showUIPrompt);

            Application.Current.DispatcherUnhandledException += (s, e) =>
                ErrorReporting.LogUnhandledException(e.Exception,
                "Application.Current.DispatcherUnhandledException", showUIPrompt);

            TaskScheduler.UnobservedTaskException += (s, e) =>
                ErrorReporting.LogUnhandledException(e.Exception,
                "TaskScheduler.UnobservedTaskException", showUIPrompt);
        }
    }
}
