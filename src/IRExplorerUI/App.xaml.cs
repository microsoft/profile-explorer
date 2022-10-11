// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml;

namespace IRExplorerUI {
    public class SyntaxFileInfo {
        public SyntaxFileInfo(string name, string compiler, string path) {
            Name = name;
            Compiler = compiler;
            Path = path;
        }

        public string Name { get; set; }
        public string Compiler { get; set; }
        public string Path { get; set; }

        public override bool Equals(object obj) {
            return obj is SyntaxFileInfo info &&
                   Name == info.Name &&
                   Compiler == info.Compiler &&
                   Path == info.Path;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Name, Compiler, Path);
        }

        public static bool operator ==(SyntaxFileInfo left, SyntaxFileInfo right) {
            return EqualityComparer<SyntaxFileInfo>.Default.Equals(left, right);
        }

        public static bool operator !=(SyntaxFileInfo left, SyntaxFileInfo right) {
            return !(left == right);
        }
    }

    public partial class App : Application {
        public static DateTime AppStartTime;
        public static DateTime WindowShowTime;
        public static ApplicationSettings Settings;
        public static ISession Session;

        private const string SettingsPath = @"Microsoft\IRExplorer";
        private const string SettingsFile = "IRExplorer.settings";
        private const string LastDockLayoutFile = "LastDockLayout.xml";
        private const string DefaultDockLayoutFile = "DockLayout.xml";
        private const string WorkspaceDockLayoutFile = "WorkspaceDockLayout-{0}.xml";
        private const string TraceFile = "IRExplorer.log";
        private const string BackupTraceFile = "IRExplorerBackup.log";
        private const string RemarkDefinitionFile = @"remark-settings.json";
        private const string SectionDefinitionFile = @"section-settings.json";
        private const string InternalIRSyntaxHighlightingFile = @"ir.xshd";
        private const string InternalExtensionFile = @"IRExplorerExtension.vsix";
        private const string SyntaxFileSearchPattern = @"*.xshd";
        private const string SyntaxFileExtension = @"xshd";
        private const string FunctionTaskScriptsDirectory = "scripts";
        private const string FunctionTaskScriptSearchPattern = @"*.cs";
        public const string AutoUpdateInfo = @"https://irexplorer.blob.core.windows.net/app/update.xml";
        private const string DocumentationLocation = @"https://irexplorer.z5.web.core.windows.net/";

        private static List<SyntaxFileInfo> cachedSyntaxHighlightinFiles_;

        private static bool CreateSettingsDirectory() {
            try {
                string path = GetSettingsDirectoryPath();
                CreateDirectories(path);

                InitializeSettingsFilesDirectory("utc");
                InitializeSettingsFilesDirectory("llvm");
                InitializeSettingsFilesDirectory("ASM");
                InitializeSettingsFilesDirectory("scripts");
                InitializeSettingsFilesDirectory("themes");
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to create settings directory: {ex}");
                return false;
            }
        }

        private static bool CreateDirectories(string path) {
            try {
                if (!Directory.Exists(path)) {
                    Directory.CreateDirectory(path);
                }

                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to create directories for {path}: {ex}");
                return false;
            }
        }

        public static string[] GetFunctionTaskScripts() {
            try {
                var path = GetSettingsFilePath(FunctionTaskScriptsDirectory);
                return Directory.GetFiles(path, FunctionTaskScriptSearchPattern);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to get function task scripts: {ex}");
                return new string[] { };
            }
        }

        private static string GetSettingsDirectoryPath() {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(path, SettingsPath);
        }

        private static string GetSettingsFilePath(string file) {
            // File relative to the settings directory.
            string path = GetSettingsDirectoryPath();
            return Path.Combine(path, file);
        }

        private static string GetSettingsFilePath(string subDir, string file, string extension = "") {
            // File relative to the settings directory.
            string path = GetSettingsDirectoryPath();
            return Path.Combine(path, subDir, !string.IsNullOrEmpty(extension) ?
                                                $"{file}.{extension}" : file);
        }

        public static string GetCompilerSettingsDirectoryPath(string compilerName) {
            return GetSettingsFilePath(compilerName);
        }

        public static string GetLastDockLayoutFilePath() {
            string path = GetSettingsDirectoryPath();
            return Path.Combine(path, DefaultDockLayoutFile);
        }

        public static string GetDockLayoutFilePath(string layoutName) {
            string path = GetSettingsDirectoryPath();
            return Path.Combine(path, $"{layoutName}.xml");
        }

        public static string GetCompilerSettingsFilePath(string file, string compilerName, string extension = "") {
            // Remove extension if another one should be used.
            file = string.IsNullOrEmpty(extension) ? file : Path.GetFileNameWithoutExtension(file);
            return GetSettingsFilePath(compilerName, file, extension);
        }

        private static string GetApplicationFilePath(string file) {
            // File relative to the application install directory.
            string path = App.ApplicationDirectory;
            return Path.Combine(path, file);
        }

        private static string GetApplicationFilePath(string subDir, string file, string extension = "") {
            // File relative to the application install directory.
            string path = App.ApplicationDirectory;
            return Path.Combine(path, subDir, file, extension);
        }

        private static string GetCompilerApplicationDirectoryPath(string compilerName) {
            return GetApplicationFilePath(compilerName);
        }

        private static string GetCompilerApplicationFilePath(string file, string compilerName, string extension = "") {
            file = string.IsNullOrEmpty(extension) ? file : Path.GetFileNameWithoutExtension(file);
            return GetApplicationFilePath(compilerName, file, extension);
        }

        private static bool CreateDirectoriesForFile(string file) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to create directories for {file}: {ex}");
                return false;
            }
        }

        private static string GetSettingsFilePath() {
            return GetSettingsFilePath(SettingsFile);
        }

        public static string GetTraceFilePath() {
            return GetSettingsFilePath(TraceFile);
        }

        public static string GetBackupTraceFilePath() {
            return GetSettingsFilePath(BackupTraceFile);
        }

        public static string GetExtensionFilePath() {
            return GetApplicationFilePath(InternalExtensionFile);
        }

        public static string GetInternalSyntaxHighlightingFilePath(string name, string compilerIRName) {
            var files = GetSyntaxHighlightingFiles(compilerIRName, true);
            var result = files.Find(item => item.Name == name);
            return result?.Path;
        }

        public static string GetInternalIRSyntaxHighlightingFilePath() {
            string appDir = App.ApplicationDirectory;
            return Path.Combine(appDir, InternalIRSyntaxHighlightingFile);
        }

        public static string GetRemarksDefinitionFilePath(string compilerIRName) {
            string userFile = GetUserRemarksDefinitionFilePath(compilerIRName);

            if (File.Exists(userFile)) {
                return userFile;
            }

            var internalFile = GetInternalRemarksDefinitionFilePath(compilerIRName);

            if (File.Exists(internalFile) && CreateDirectoriesForFile(userFile)) {
                try {
                    File.Copy(internalFile, userFile);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to get copy file {internalFile} to {userFile}: {ex}");
                    return null;
                }

                return userFile;
            }

            return null;
        }

        public static string GetSectionsDefinitionFilePath(string compilerIRName) {
            string userFile = GetUserSectionsDefinitionFilePath(compilerIRName);

            if (File.Exists(userFile)) {
                return userFile;
            }

            var internalFile = GetInternalSectionsDefinitionFilePath(compilerIRName);

            if (File.Exists(internalFile) && CreateDirectoriesForFile(userFile)) {
                try {
                    File.Copy(internalFile, userFile);
                }
                catch (Exception ex) {
                    Trace.TraceError($"Failed to get copy file {internalFile} to {userFile}: {ex}");

                }

                return userFile;
            }

            return null;
        }

        public static string GetUserRemarksDefinitionFilePath(string compilerIRName) {
            return GetSettingsFilePath(compilerIRName, RemarkDefinitionFile);
        }

        public static string GetInternalRemarksDefinitionFilePath(string compilerIRName) {
            return GetApplicationFilePath(compilerIRName, RemarkDefinitionFile);
        }


        public static string GetUserSectionsDefinitionFilePath(string compilerIRName) {
            return GetSettingsFilePath(compilerIRName, SectionDefinitionFile);
        }

        public static string GetInternalSectionsDefinitionFilePath(string compilerIRName) {
            return GetApplicationFilePath(compilerIRName, SectionDefinitionFile);
        }

        private static string GetSyntaxFileName(string filePath) {
            try {
                var xmlDoc = new XmlDocument();
                var ns = new XmlNamespaceManager(xmlDoc.NameTable);
                ns.AddNamespace("syntax", "http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008");

                xmlDoc.Load(filePath);
                var node = xmlDoc.SelectSingleNode("//syntax:SyntaxDefinition", ns);

                if (node != null) {
                    return node.Attributes.GetNamedItem("name").InnerText;
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to get syntax file name: {ex}");
            }

            return "";
        }

        public static List<SyntaxFileInfo> GetSyntaxHighlightingFiles(string compilerIRName, bool internalFiles = false) {
            if (!internalFiles && cachedSyntaxHighlightinFiles_ != null) {
                return cachedSyntaxHighlightinFiles_;
            }

            var list = new List<SyntaxFileInfo>();

            try {
                string baseDir = internalFiles ?
                    GetCompilerApplicationDirectoryPath(compilerIRName) :
                    GetCompilerSettingsDirectoryPath(compilerIRName);

                var themes = Directory.GetFiles(baseDir, SyntaxFileSearchPattern);

                foreach (string theme in themes) {
                    list.Add(new SyntaxFileInfo(GetSyntaxFileName(theme), compilerIRName, theme));
                }
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to get syntax file list: {ex}");
            }

            if (!internalFiles) {
                cachedSyntaxHighlightinFiles_ = list;
            }

            return list;
        }

        public static List<SyntaxFileInfo> ReloadSyntaxHighlightingFiles(string compilerIRName) {
            cachedSyntaxHighlightinFiles_ = null;
            return GetSyntaxHighlightingFiles(compilerIRName);
        }

        private static bool InitializeSettingsFilesDirectory(string directory) {
            try {
                var syntaxFilesDir = GetCompilerSettingsDirectoryPath(directory);
                CreateDirectories(syntaxFilesDir);

                var compilerDir = GetCompilerApplicationDirectoryPath(directory);
                var files = Directory.GetFiles(compilerDir, "*.*");

                foreach (var file in files) {
                    var destFile = GetCompilerSettingsFilePath(Path.GetFileName(file), directory);

                    File.Copy(file, destFile, overwrite: true);
                }

                return true;
            }
            catch (Exception ex) {
                //Trace.TraceError($"Failed to create syntax file directory: {ex}");
                return false;
            }
        }


        public static SyntaxFileInfo GetSyntaxHighlightingFileInfo(string name, string compilerIRName) {
            var files = GetSyntaxHighlightingFiles(compilerIRName);
            return files.Find(item => item.Name == name);
        }

        public static string GetSyntaxHighlightingFilePath() {
            // If a file is not set yet (first run for ex), set the default one.
            var docSettings = Settings.DocumentSettings;

            //? TODO: Each compiler mode should have its own syntax saved
            if (string.IsNullOrEmpty(docSettings.SyntaxHighlightingName)) {
                docSettings.SyntaxHighlightingName = Session.CompilerInfo.DefaultSyntaxHighlightingFile;
            }

            var result = GetSyntaxHighlightingFileInfo(docSettings.SyntaxHighlightingName,
                                                       Session.CompilerInfo.CompilerIRName);
            return result?.Path;

        }

        public static string GetSyntaxHighlightingFilePath(SyntaxFileInfo syntaxFile) {
            if (syntaxFile != null && File.Exists(syntaxFile.Path)) {
                return syntaxFile.Path;
            }
            return GetSyntaxHighlightingFilePath();
        }

        public static string GetSyntaxHighlightingFilePath(string name, string compilerIRName) {
            InitializeSettingsFilesDirectory(compilerIRName);
            return GetCompilerSettingsFilePath(name, compilerIRName, SyntaxFileExtension);
        }

        public static bool LoadApplicationSettings() {
            try {
                CreateSettingsDirectory();

                string path = GetSettingsFilePath();
                var data = File.ReadAllBytes(path);
                Settings = StateSerializer.Deserialize<ApplicationSettings>(data);

                // Do some basic sanity checks in case the settings file is incompatible.
                if (Settings.RecentFiles == null) {
                    Settings.RecentFiles = new List<string>();
                }

                if (Settings.RecentComparedFiles == null) {
                    Settings.RecentComparedFiles = new List<Tuple<string, string>>();
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

        public static void LaunchSettingsFileEditor(string settingsPath) {
            if (string.IsNullOrEmpty(settingsPath) ||
                !File.Exists(settingsPath)) {
                MessageBox.Show($"Could not file settings file {settingsPath}", "IR Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try {
                var psi = new ProcessStartInfo(settingsPath) {
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex) {
                MessageBox.Show($"Failed to open settings file {settingsPath}\n{ex.Message}", "IR Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void OpenSettingsFolder(string settingsPath) {
            if (string.IsNullOrEmpty(settingsPath) ||
                !Directory.Exists(settingsPath)) {
                MessageBox.Show($"Could not fine directory\n{settingsPath}", "IR Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try {
                var psi = new ProcessStartInfo(settingsPath) {
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex) {
                MessageBox.Show($"Failed to open settings folder {settingsPath}\n{ex.Message}", "IR Explorer",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void DeleteSettingsFile(string settingsPath) {
            if (string.IsNullOrEmpty(settingsPath) ||
                !File.Exists(settingsPath)) {
                return;
            }

            try {
                File.Delete(settingsPath);
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to delete file {settingsPath}: {ex}");
            }
        }

        protected override void OnStartup(StartupEventArgs e) {
            //? TODO: Needed to run under Wine on Linux
            //RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

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
                    File.Copy(traceFilePath, GetBackupTraceFilePath());
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
                ErrorReporting.LogUnhandledException((Exception)e.ExceptionObject,
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

        public static void InstallExtension() {
            if (!Utils.OpenExternalFile(GetExtensionFilePath())) {
                MessageBox.Show($"Failed to open VS extension installer", "IR Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void OpenDocumentation() {
            if (!Utils.OpenExternalFile(DocumentationLocation)) {
                MessageBox.Show($"Failed to open documentation page", "IR Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        public static string ApplicationPath => Process.GetCurrentProcess().MainModule.FileName;

        public static string ApplicationDirectory => Path.GetDirectoryName(ApplicationPath);

        public static bool StartNewApplicationInstance(string args = "", bool adminMode = false) {
            var psi = new ProcessStartInfo(ApplicationPath);
            psi.Arguments = args;
            psi.UseShellExecute = true;

            if (adminMode) {
                psi.Verb = "runas";
            }

            try {
                using var process = new Process();
                process.StartInfo = psi;
                process.Start();
                return true;
            }
            catch (Exception ex) {
                Debug.WriteLine($"Failed to start new app instance: {ex}");
                return false;
            }
        }

        public static void RestartApplicationAsAdmin(string args = "") {
            StartNewApplicationInstance(args, true);
            Application.Current.Shutdown();
        }
    }
}