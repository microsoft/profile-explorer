// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using IRExplorerUI.Settings;

namespace IRExplorerUI;

public class SyntaxFileInfo {
  public SyntaxFileInfo(string name, string compiler, string path) {
    Name = name;
    Compiler = compiler;
    Path = path;
  }

  public string Name { get; set; }
  public string Compiler { get; set; }
  public string Path { get; set; }

  public static bool operator ==(SyntaxFileInfo left, SyntaxFileInfo right) {
    return EqualityComparer<SyntaxFileInfo>.Default.Equals(left, right);
  }

  public static bool operator !=(SyntaxFileInfo left, SyntaxFileInfo right) {
    return !(left == right);
  }

  public override bool Equals(object obj) {
    return obj is SyntaxFileInfo info &&
           Name == info.Name &&
           Compiler == info.Compiler &&
           Path == info.Path;
  }

  public override int GetHashCode() {
    return HashCode.Combine(Name, Compiler, Path);
  }
}

public partial class App : Application {
  public const string AutoUpdateInfox64 = @"\\ntperformance\Public\benjaming\IRExplorer\x64\autoupdater.xml";
  public const string AutoUpdateInfoArm64 = @"\\ntperformance\Public\benjaming\IRExplorer\arm64\autoupdater.xml";
  private const string SettingsPath = @"Microsoft\IRExplorer";
  private const string SettingsFile = "IRExplorer.settings";
  private const string DefaultDockLayoutFile = "DockLayout.xml";
#if DEBUG
  //private const string HelpLocation = @"help"; // Local directory.
  private const string HelpLocation = @"D:\github\irx\resources\help";
#else
  private const string HelpLocation = @"https://irx.z5.web.core.windows.net/";
#endif
  private const string HelpIndexFile = @"index.json";
  private const string LicenseFile = "license.txt";
  private const string WorkspacesDirectory = "workspaces";
  private const string ScriptsDirectory = "scripts";
  private const string ThemesDirectory = "themes";
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
  private const string DocumentationLocation = @"https://irexplorer.z5.web.core.windows.net/";
  public static bool IsFirstRun;
  public static DateTime AppStartTime;
  public static ApplicationSettings Settings;
  public static ISession Session;
  private static List<SyntaxFileInfo> cachedSyntaxHighlightinFiles_;
  public static string ApplicationPath => Process.GetCurrentProcess().MainModule.FileName;
  public static string ApplicationDirectory => Path.GetDirectoryName(ApplicationPath);


  public static string[] GetFunctionTaskScripts() {
    try {
      string path = GetSettingsFilePath(FunctionTaskScriptsDirectory);
      return Directory.GetFiles(path, FunctionTaskScriptSearchPattern);
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to get function task scripts: {ex}");
      return new string[] { };
    }
  }

  public static string GetCompilerSettingsDirectoryPath(string compilerName) {
    return GetSettingsFilePath(compilerName);
  }

  public static string GetDefaultDockLayoutFilePath() {
    string path = GetSettingsDirectoryPath();
    return Path.Combine(path, DefaultDockLayoutFile);
  }

  public static string GetWorkspacesPath() {
    return GetSettingsFilePath(WorkspacesDirectory);
  }

  public static string GetInternalWorkspacesPath() {
    return GetApplicationFilePath(WorkspacesDirectory);
  }

  public static string GetLicenseText() {
    try {
      return File.ReadAllText(GetApplicationFilePath(LicenseFile));
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to get license text: {ex}");
      return "";
    }
  }

  public static string GetCompilerSettingsFilePath(string file, string compilerName, string extension = "") {
    // Remove extension if another one should be used.
    file = string.IsNullOrEmpty(extension) ? file : Path.GetFileNameWithoutExtension(file);
    return GetSettingsFilePath(compilerName, file, extension);
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
    string appDir = ApplicationDirectory;
    return Path.Combine(appDir, InternalIRSyntaxHighlightingFile);
  }

  public static string GetHelpIndexFilePath() {
    if (HelpLocation.StartsWith("https://")) {
      return $"{HelpLocation}/{HelpIndexFile}";
    }

    string appDir = ApplicationDirectory;
    return Path.Combine(appDir, HelpLocation, HelpIndexFile);
  }

  public static string GetHelpFilePath(string relativeURL) {
    if (HelpLocation.StartsWith("https://")) {
      return $"{HelpLocation}/{relativeURL}";
    }

    string appDir = ApplicationDirectory;
    return Path.Combine(appDir, HelpLocation, relativeURL);
  }

  public static string GetRemarksDefinitionFilePath(string compilerIRName) {
    string userFile = GetUserRemarksDefinitionFilePath(compilerIRName);

    if (File.Exists(userFile)) {
      return userFile;
    }

    string internalFile = GetInternalRemarksDefinitionFilePath(compilerIRName);

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

    string internalFile = GetInternalSectionsDefinitionFilePath(compilerIRName);

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

  public static List<SyntaxFileInfo> GetSyntaxHighlightingFiles(string compilerIRName, bool internalFiles = false) {
    if (!internalFiles && cachedSyntaxHighlightinFiles_ != null) {
      return cachedSyntaxHighlightinFiles_;
    }

    var list = new List<SyntaxFileInfo>();

    try {
      string baseDir = internalFiles ?
        GetCompilerApplicationDirectoryPath(compilerIRName) :
        GetCompilerSettingsDirectoryPath(compilerIRName);

      string[] themes = Directory.GetFiles(baseDir, SyntaxFileSearchPattern);

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
      byte[] data = File.ReadAllBytes(path);
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
      byte[] data = StateSerializer.Serialize(Settings);
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

  public static void InstallExtension() {
    if (!Utils.OpenExternalFile(GetExtensionFilePath())) {
      MessageBox.Show("Failed to open VS extension installer", "IR Explorer", MessageBoxButton.OK,
                      MessageBoxImage.Error);
    }
  }

  public static void OpenDocumentation() {
    if (!Utils.OpenExternalFile(DocumentationLocation)) {
      MessageBox.Show("Failed to open documentation page", "IR Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

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
    Current.Shutdown();
  }

  private static bool CreateSettingsDirectory() {
    try {
      string path = GetSettingsDirectoryPath();
      CreateDirectories(path);

      //? TODO: Walk over list of registers compilers
      InitializeSettingsFilesDirectory("llvm");
      InitializeSettingsFilesDirectory("ASM");

      InitializeSettingsFilesDirectory(ScriptsDirectory);
      InitializeSettingsFilesDirectory(ThemesDirectory);
      InitializeSettingsFilesDirectory(WorkspacesDirectory);
      return true;
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to create settings directory: {ex}");
      return false;
    }
  }

  public static bool CreateDirectories(string path) {
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

  public static string GetSettingsDirectoryPath() {
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

  private static string GetApplicationFilePath(string file) {
    // File relative to the application install directory.
    string path = ApplicationDirectory;
    return Path.Combine(path, file);
  }

  private static string GetApplicationFilePath(string subDir, string file, string extension = "") {
    // File relative to the application install directory.
    string path = ApplicationDirectory;
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

  public static bool ContainsSettingsFile(string dirPath) {
    return File.Exists(Path.Combine(dirPath, SettingsFile));
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

  private static bool InitializeSettingsFilesDirectory(string directory) {
    try {
      string syntaxFilesDir = GetCompilerSettingsDirectoryPath(directory);
      CreateDirectories(syntaxFilesDir);

      string compilerDir = GetCompilerApplicationDirectoryPath(directory);
      string[] files = Directory.GetFiles(compilerDir, "*.*");

      foreach (string file in files) {
        string destFile = GetCompilerSettingsFilePath(Path.GetFileName(file), directory);

        File.Copy(file, destFile, true);
      }

      return true;
    }
    catch (Exception ex) {
      //Trace.TraceError($"Failed to create syntax file directory: {ex}");
      return false;
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

  protected override void OnStartup(StartupEventArgs e) {
    //? TODO: Needed to run under Wine on Linux
    //RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

    AppStartTime = DateTime.UtcNow;
    base.OnStartup(e);

    if (!Debugger.IsAttached) {
      SetupExceptionHandling();
    }

    // Enable file output for tracing.
    OpenLogFile();

    if (!LoadApplicationSettings()) {
      // Failed to load settings, reset them.
      Utils.TryDeleteFile(GetSettingsFilePath());
      Utils.TryDeleteFile(GetDefaultDockLayoutFilePath());
      Settings = new ApplicationSettings();
      IsFirstRun = true;
    }
  }

  public static void OpenLogFile() {
    try {
      string traceFilePath = GetTraceFilePath();

      if (File.Exists(traceFilePath)) {
        File.Copy(traceFilePath, GetBackupTraceFilePath(), true);
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
  }

  public static void CloseLogFile() {
    try {
      Trace.Flush();

      foreach (TraceListener listener in Trace.Listeners) {
        listener.Close();
      }

      Trace.Close();
    }
    catch (Exception ex) {
      Debug.WriteLine($"Failed to close trace file: {ex}");
    }
  }

  public static void Restart() {
    Process.Start(Process.GetCurrentProcess().MainModule.FileName);
    Application.Current.Shutdown();
  }
}