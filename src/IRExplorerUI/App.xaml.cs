// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
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
        private const string TraceFile = "IRExplorer.trace";
        private const string RemarkDefinitionFile = @"remark-settings.json";
        private const string SectionDefinitionFile = @"section-settings.json";
        private const string InternalIRSyntaxHighlightingFile = @"ir.xshd";
        private const string InternalExtensionFile = @"IRExplorerExtension.vsix";
        private const string SyntaxFileSearchPattern = @"*.xshd";
        private const string SyntaxFileExtension = @"xshd";
        public const string AutoUpdateInfo = @"http://irexplorerstorage.file.core.windows.net/irexplorer-app/update.xml?sv=2019-10-10&ss=bfqt&srt=o&sp=rlacupx&se=2023-03-01T14:12:02Z&st=2020-07-02T05:12:02Z&spr=https,http&sig=VEd7d8WhShT20oknDmfe04wTFniOFVpvohax9xMx%2FOg%3D";
        private const string DocumentationLocation = @"file://ir-explorer/docs/index.html";

        private static bool CreateSettingsDirectory() {
            try {
                string path = GetSettingsDirectoryPath();
                CreateDirectories(path);

                CreateSyntaxFilesDirectory("UTC");
                CreateSyntaxFilesDirectory("LLVM");
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

        public static string GetCompilerSettingsFilePath(string file, string compilerName, string extension = "") {
            // Remove extension if another one should be used.
            file = string.IsNullOrEmpty(extension) ? file : Path.GetFileNameWithoutExtension(file);
            return GetSettingsFilePath(compilerName, file, extension);
        }

        private static string GetApplicationFilePath(string file) {
            // File relative to the application install directory.
            string path = Utils.GetApplicationDirectory();
            return Path.Combine(path, file);
        }

        private static string GetApplicationFilePath(string subDir, string file, string extension = "") {
            // File relative to the application install directory.
            string path = Utils.GetApplicationDirectory();
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

        private static string GetTraceFilePath() {
            return GetSettingsFilePath(TraceFile);
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
            string appDir = Utils.GetApplicationDirectory();
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

        private static List<SyntaxFileInfo> cachedSyntaxHighlightinFiles_;

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

        private static bool CreateSyntaxFilesDirectory(string compilerIRName) {
            try {
                var syntaxFilesDir = GetCompilerSettingsDirectoryPath(compilerIRName);

                if(Directory.Exists(syntaxFilesDir)) {
                    return true;
                }

                CreateDirectories(syntaxFilesDir);
                var files = GetSyntaxHighlightingFiles(compilerIRName, true);

                foreach (var file in files) {
                    var destFile = GetCompilerSettingsFilePath(Path.GetFileName(file.Path), compilerIRName);

                    if (!File.Exists(destFile)) {
                        File.Copy(file.Path, destFile);
                    }
                }

                return true;
            }
            catch (Exception ex) {
                Trace.TraceError($"Failed to create syntax file directory: {ex}");
                return false;
            }
        }

        public static SyntaxFileInfo GetSyntaxHighlightingFileInfo(string name, string compilerIRName) {
            var files = GetSyntaxHighlightingFiles(compilerIRName);
            return files.Find(item => item.Name == name);
        }

        public static string GetSyntaxHighlightingFilePath() {
            // By default use the syntax file from the document settings.
            var result = GetSyntaxHighlightingFileInfo(Settings.DocumentSettings.SyntaxHighlightingName,
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
            CreateSyntaxFilesDirectory(compilerIRName);
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
            try {
                var path = GetExtensionFilePath();
                var psi = new ProcessStartInfo(path) {
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex) {
                MessageBox.Show($"Failed to open extension file\n{ex.Message}", "IR Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void OpenDocumentation() {
            try {
                var psi = new ProcessStartInfo(DocumentationLocation);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception) {
                MessageBox.Show($"Failed to open documentation page,\nmake sure the VPN connection is active", "IR Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
