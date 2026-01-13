// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Xml;
using ProfileExplorer.Core.Utilities;
using ProfileExplorer.UI.Settings;
using ProfileExplorer.UI.Mcp;
using ProfileExplorer.Mcp;

namespace ProfileExplorer.UI;

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
#if DEBUG
  // For use with "mkdocs serve".
  private const string HelpLocation = @"http://127.0.0.1:8000";
  private const string DocumentationLocation = @"http://127.0.0.1:8000";
#else
  private const string HelpLocation = @"https://microsoft.github.io/profile-explorer/site";
  private const string DocumentationLocation = @"https://microsoft.github.io/profile-explorer";
#endif

  public const string AutoUpdateInfox64 = @"https://microsoft.github.io/profile-explorer/autoupdater.xml";
  public const string AutoUpdateInfoArm64 = @"https://microsoft.github.io/profile-explorer/autoupdater_arm64.xml";
  private const string SettingsPath = @"Microsoft\ProfileExplorer";
  private const string SettingsFile = "ProfileExplorer.settings";
  private const string HelpIndexFile = @"index.json";
  private const string LicenseFile = "NOTICE.md";
  private const string WorkspacesDirectory = "workspaces";
  private const string ScriptsDirectory = "scripts";
  private const string ThemesDirectory = "themes";
  private const string TraceFile = "ProfileExplorer.log";
  private const string BackupTraceFile = "ProfileExplorerBackup.log";
  private const string RemarkDefinitionFile = @"remark-settings.json";
  private const string SectionDefinitionFile = @"section-settings.json";
  private const string FunctionMarkingsFile = @"function-markings.json";
  private const string InternalIRSyntaxHighlightingFile = @"ir.xshd";
  private const string InternalExtensionFile = @"VSExtension.vsix";
  private const string SyntaxFileSearchPattern = @"*.xshd";
  private const string SyntaxFileExtension = @"xshd";
  private const string FunctionTaskScriptsDirectory = "scripts";
  private const string FunctionTaskScriptSearchPattern = @"*.cs";
  public static bool IsFirstRun;
  public static DateTime AppStartTime;
  public static ApplicationSettings Settings;
  public static IUISession Session;
  /// <summary>
  /// When true, suppresses UI dialogs (like source file prompts) during MCP/automation operations.
  /// </summary>
  public static bool SuppressDialogsForAutomation;
  private Task? mcpServerTask;
  private static List<SyntaxFileInfo> cachedSyntaxHighlightingFiles_;
  public static string ApplicationPath => Process.GetCurrentProcess().MainModule?.FileName;
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
    return $"{HelpLocation}/{HelpIndexFile}";
  }

  public static string GetHelpFilePath(string relativeURL) {
    return $"{HelpLocation}/{relativeURL}";
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

  public static string GetFunctionMarkingsFilePath(string compilerIRName) {
    return GetSettingsFilePath(compilerIRName, FunctionMarkingsFile);
  }

  public static List<SyntaxFileInfo> GetSyntaxHighlightingFiles(string compilerIRName, bool internalFiles = false) {
    if (!internalFiles && cachedSyntaxHighlightingFiles_ != null) {
      return cachedSyntaxHighlightingFiles_;
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
      cachedSyntaxHighlightingFiles_ = list;
    }

    return list;
  }

  public static List<SyntaxFileInfo> ReloadSyntaxHighlightingFiles(string compilerIRName) {
    cachedSyntaxHighlightingFiles_ = null;
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
      Settings = UIStateSerializer.Deserialize<ApplicationSettings>(data);

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
      byte[] data = UIStateSerializer.Serialize(Settings);
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
      MessageBox.Show($"Could not file settings file {settingsPath}", "Profile Explorer",
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
      MessageBox.Show($"Failed to open settings file {settingsPath}\n{ex.Message}", "Profile Explorer",
                      MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  public static void OpenSettingsFolder(string settingsPath) {
    if (string.IsNullOrEmpty(settingsPath) ||
        !Directory.Exists(settingsPath)) {
      MessageBox.Show($"Could not fine directory\n{settingsPath}", "Profile Explorer",
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
      MessageBox.Show($"Failed to open settings folder {settingsPath}\n{ex.Message}", "Profile Explorer",
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
      MessageBox.Show("Failed to open VS extension installer", "Profile Explorer", MessageBoxButton.OK,
                      MessageBoxImage.Error);
    }
  }

  public static void OpenDocumentation() {
    if (!Utils.OpenURL(DocumentationLocation)) {
      MessageBox.Show("Failed to open documentation page", "Profile Explorer", MessageBoxButton.OK,
                      MessageBoxImage.Error);
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

        //? TODO: This should rather try to merge the potentially newer file
        //? with the existing one that may have user customizations.
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
    AppStartTime = DateTime.UtcNow;
    base.OnStartup(e);

    // Initialize UI-specific JSON converters
    UIJsonUtils.Initialize();

    // Register UI-specific type converters for settings system
    RegisterSettingsTypeConverters();

    if (!Debugger.IsAttached) {
      SetupExceptionHandling();
    }

    FixPopupPlacement();

    // Disable most data-binding error reporting, slows down debugging too much.
    PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Critical;

    // Enable file output for tracing.
    OpenLogFile();
    SetupJumplist();

    if (!LoadApplicationSettings()) {
      // Failed to load settings, reset them.
      Utils.TryDeleteFile(GetSettingsFilePath());
      Settings = new ApplicationSettings();
      IsFirstRun = true;
    }

    // Configure CoreSettingsProvider to use UI settings instead of defaults
    CoreSettingsProvider.SetProvider(new UISettingsProvider());

    if (Settings.GeneralSettings.DisableHardwareRendering) {
      Trace.WriteLine($"Disable hardware rendering");
      RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }

    // Create and show the main window manually
    var mainWindow = new MainWindow();
    mainWindow.Show();

    // Initialize MCP server if enabled
    InitializeMcpServerAsync(mainWindow);
  }

  private void InitializeMcpServerAsync(MainWindow mainWindow)
  {
    try
    {
      // Create the MCP action executor
      var executor = new McpActionExecutor(mainWindow);
      
      // Start the MCP server in the background
      mcpServerTask = Task.Run(async () =>
      {
        try
        {
          await McpServerConfiguration.StartServerWithExecutorAsync(executor);
        }
        catch (Exception ex)
        {
          Trace.WriteLine($"MCP Server error: {ex}");
        }
      });
      
      Trace.WriteLine("MCP Server initialization started");
    }
    catch (Exception ex)
    {
      Trace.WriteLine($"Failed to initialize MCP Server: {ex}");
    }
  }

  protected override void OnExit(ExitEventArgs e)
  {
    // Wait for MCP server to shutdown gracefully
    if (mcpServerTask != null && !mcpServerTask.IsCompleted)
    {
      try
      {
        // Give the server a moment to shutdown gracefully
        mcpServerTask.Wait(TimeSpan.FromSeconds(5));
      }
      catch (Exception ex)
      {
        Trace.WriteLine($"Error during MCP server shutdown: {ex}");
      }
    }

    base.OnExit(e);
  }

  private static void FixPopupPlacement() {
    // On touchscreen laptops, popups are not displayed in the right place.
    // Hack taken from https://stackoverflow.com/a/54298981 to fix it.
    bool ifLeft = SystemParameters.MenuDropAlignment;

    if (ifLeft) {
      // change to false
      var t = typeof(SystemParameters);
      var field = t.GetField("_menuDropAlignment", BindingFlags.NonPublic | BindingFlags.Static);
      field.SetValue(null, false);
    }
  }

  private void RegisterSettingsTypeConverters() {
    // Register UI-specific type converters for the settings system
    ProfileExplorer.Core.Settings.SettingsTypeRegistry.RegisterConverter(new ProfileExplorerUI.Settings.ColorSettingsConverter());
  }

  private void SetupJumplist() {
    var instanceTask = new JumpTask {
      ApplicationPath = ApplicationPath,
      Arguments = "",
      Title = "New instance",
      Description = "Start a new Profile Explorer instance",
      CustomCategory = "Tasks"
    };

    // var recordTask = new JumpTask {
    //   ApplicationPath = ApplicationPath,
    //   IconResourcePath = ApplicationPath,
    //   IconResourceIndex = 2,
    //   Arguments = "",
    //   Title = "Record profile",
    //   Description = "Start a new Profile Explorer instance",
    //   CustomCategory = "Tasks"
    // };
    //
    //
    // var recordSystemTask = new JumpTask {
    //   ApplicationPath = ApplicationPath,
    //   IconResourcePath = ApplicationPath,
    //   IconResourceIndex = 2,
    //   Arguments = "",
    //   Title = "Record system-wide profile",
    //   Description = "Start a new Profile Explorer instance",
    //   CustomCategory = "Tasks"
    // };

    var currentJumplist = JumpList.GetJumpList(Current);

    if (currentJumplist != null) {
      currentJumplist.JumpItems.Clear();
      currentJumplist.JumpItems.Add(instanceTask);
      //currentJumplist.JumpItems.Add(recordTask);
      //currentJumplist.JumpItems.Add(recordSystemTask);
      currentJumplist.Apply();
    }
    else {
      var jumpList = new JumpList();
      jumpList.JumpItems.Add(instanceTask);
      //jumpList.JumpItems.Add(recordTask);
      //jumpList.JumpItems.Add(recordSystemTask);
      JumpList.SetJumpList(Current, jumpList);
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
    //? TODO: Start new instance only after settings were saved.
    Process.Start(Process.GetCurrentProcess().MainModule.FileName);
    Current.Shutdown();
  }
}