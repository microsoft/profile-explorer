// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ProfileExplorer.Core.Utilities;

/// <summary>
/// Diagnostic logger that writes to a file that can be easily retrieved by users.
/// This is specifically designed for debugging issues that don't reproduce locally
/// and need to be sent back by users.
/// 
/// Logging is controlled by the PROFILE_EXPLORER_DEBUG environment variable.
/// Set to "1", "true", "on", or "enabled" to enable diagnostic logging.
/// Defaults to disabled for performance and privacy.
/// </summary>
public static class DiagnosticLogger {
  private static readonly object lockObject_ = new();
  private static ILogger logger_;
  private static string logFilePath_;
  private static readonly ConcurrentQueue<string> recentMessages_ = new();
  private static readonly int MaxRecentMessages = 1000;
  private static bool initialized_ = false;
  private static readonly Lazy<bool> isEnabled_ = new Lazy<bool>(CheckIfEnabled);

  /// <summary>
  /// Check if diagnostic logging is enabled via environment variable
  /// </summary>
  private static bool CheckIfEnabled() {
    string envValue = Environment.GetEnvironmentVariable("PROFILE_EXPLORER_DEBUG");
    if (string.IsNullOrEmpty(envValue)) {
      return false;
    }

    envValue = envValue.Trim().ToLowerInvariant();
    return envValue == "1" || envValue == "true" || envValue == "on" || envValue == "enabled";
  }

  /// <summary>
  /// Gets whether diagnostic logging is enabled
  /// </summary>
  public static bool IsEnabled => isEnabled_.Value;

  /// <summary>
  /// Ensure the diagnostic logger is initialized
  /// </summary>
  private static void EnsureInitialized() {
    if (!IsEnabled) {
      return; // Don't initialize if logging is disabled
    }

    if (!initialized_) {
      lock (lockObject_) {
        if (!initialized_ && IsEnabled) {
          Initialize();
          initialized_ = true;
        }
      }
    }
  }

  /// <summary>
  /// Initialize the diagnostic logger with a file in the user's temp directory
  /// </summary>
  private static void Initialize() {
    try {
      // Create log file in user's temp directory with timestamp
      string tempDir = Path.GetTempPath();
      string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
      string processId = Process.GetCurrentProcess().Id.ToString();
      logFilePath_ = Path.Combine(tempDir, $"ProfileExplorer_Diagnostic_{timestamp}_{processId}.log");

      // Create a simple file logger
      var loggerFactory = LoggerFactory.Create(builder => {
        builder.AddProvider(new FileLoggerProvider(logFilePath_));
        builder.SetMinimumLevel(LogLevel.Debug);
      });

      logger_ = loggerFactory.CreateLogger("Diagnostic");
      
      // Log startup info directly to avoid circular calls during initialization
      LogMessageDirect(LogLevel.Information, "=== Profile Explorer Diagnostic Log Started ===");
      LogMessageDirect(LogLevel.Information, $"Diagnostic logging enabled via PROFILE_EXPLORER_DEBUG environment variable");
      LogMessageDirect(LogLevel.Information, $"Process ID: {processId}");
      LogMessageDirect(LogLevel.Information, $"Working Directory: {Environment.CurrentDirectory}");
      LogMessageDirect(LogLevel.Information, $"Machine Name: {Environment.MachineName}");
      LogMessageDirect(LogLevel.Information, $"User: {Environment.UserName}");
      LogMessageDirect(LogLevel.Information, $"OS Version: {Environment.OSVersion}");
      LogMessageDirect(LogLevel.Information, $"CLR Version: {Environment.Version}");
      LogMessageDirect(LogLevel.Information, $"Log File: {logFilePath_}");
      LogMessageDirect(LogLevel.Information, "=================================================");
    }
    catch (Exception ex) {
      // Fallback to trace if file logging fails
      Trace.TraceError($"Failed to initialize DiagnosticLogger: {ex.Message}");
    }
  }

  /// <summary>
  /// Get the path to the current diagnostic log file
  /// Returns null if logging is disabled
  /// </summary>
  public static string LogFilePath {
    get {
      if (!IsEnabled) {
        return null;
      }
      EnsureInitialized();
      return logFilePath_;
    }
  }

  /// <summary>
  /// Log debug information
  /// </summary>
  public static void LogDebug(string message) {
    if (!IsEnabled) return;
    LogMessage(LogLevel.Debug, message);
  }

  /// <summary>
  /// Log informational message
  /// </summary>
  public static void LogInfo(string message) {
    if (!IsEnabled) return;
    LogMessage(LogLevel.Information, message);
  }

  /// <summary>
  /// Log warning message
  /// </summary>
  public static void LogWarning(string message) {
    if (!IsEnabled) return;
    LogMessage(LogLevel.Warning, message);
  }

  /// <summary>
  /// Log error message
  /// </summary>
  public static void LogError(string message) {
    if (!IsEnabled) return;
    LogMessage(LogLevel.Error, message);
  }

  /// <summary>
  /// Log error with exception
  /// </summary>
  public static void LogError(string message, Exception ex) {
    if (!IsEnabled) return;
    LogMessage(LogLevel.Error, $"{message}\nException: {ex}");
  }

  private static void LogMessage(LogLevel level, string message) {
    try {
      EnsureInitialized();
      LogMessageDirect(level, message);
    }
    catch {
      // Silently ignore logging errors to avoid cascading failures
    }
  }

  /// <summary>
  /// Log message directly without initialization check - used during initialization only
  /// </summary>
  private static void LogMessageDirect(LogLevel level, string message) {
    try {
      lock (lockObject_) {
        // Add to recent messages queue
        string timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        recentMessages_.Enqueue(timestampedMessage);

        // Maintain queue size
        while (recentMessages_.Count > MaxRecentMessages) {
          recentMessages_.TryDequeue(out _);
        }

        // Log to file if available
        logger_?.Log(level, message);

        // Also log to trace for debugging
        switch (level) {
          case LogLevel.Error:
            Trace.TraceError(message);
            break;
          case LogLevel.Warning:
            Trace.TraceWarning(message);
            break;
          default:
            Trace.TraceInformation(message);
            break;
        }
      }
    }
    catch {
      // Silently ignore logging errors to avoid cascading failures
    }
  }

  /// <summary>
  /// Get recent log messages (useful for displaying in UI)
  /// Returns empty array if logging is disabled
  /// </summary>
  public static string[] GetRecentMessages() {
    if (!IsEnabled) {
      return new string[0];
    }
    EnsureInitialized();
    return recentMessages_.ToArray();
  }

  /// <summary>
  /// Open the log file in the default text editor
  /// </summary>
  public static void OpenLogFile() {
    if (!IsEnabled) {
      return;
    }

    try {
      EnsureInitialized();
      if (File.Exists(logFilePath_)) {
        Process.Start(new ProcessStartInfo(logFilePath_) { UseShellExecute = true });
      }
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to open log file: {ex.Message}");
    }
  }

  /// <summary>
  /// Copy log file path to clipboard
  /// </summary>
  public static void CopyLogFilePathToClipboard() {
    if (!IsEnabled) {
      return;
    }

    try {
      EnsureInitialized();
      // Just trace the path since we don't have clipboard access in core library
      Trace.TraceInformation($"Diagnostic log path: {logFilePath_}");
    }
    catch {
      // Ignore errors
    }
  }

  /// <summary>
  /// Show message to user about where to find the diagnostic log
  /// This method is UI-framework agnostic and returns the log file path
  /// </summary>
  public static void ShowLogFileLocation() {
    if (!IsEnabled) {
      return;
    }

    try {
      EnsureInitialized();
      // Open the file location in Windows Explorer
      if (File.Exists(logFilePath_)) {
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{logFilePath_}\"");
      }
      else {
        // If file doesn't exist, open the directory
        string directory = Path.GetDirectoryName(logFilePath_) ?? Path.GetTempPath();
        System.Diagnostics.Process.Start("explorer.exe", directory);
      }
      
      LogInfo("User accessed diagnostic log file location");
    }
    catch (Exception ex) {
      Trace.TraceError($"Failed to show log file location: {ex.Message}");
    }
  }

  /// <summary>
  /// Get diagnostic log file information
  /// </summary>
  public static string GetLogFileInfo() {
    if (!IsEnabled) {
      return "Diagnostic logging is disabled. Set PROFILE_EXPLORER_DEBUG environment variable to '1', 'true', 'on', or 'enabled' to enable logging.";
    }

    EnsureInitialized();
    if (File.Exists(logFilePath_)) {
      var info = new FileInfo(logFilePath_);
      return $"Diagnostic log file location:\n{logFilePath_}\n\nFile size: {info.Length:N0} bytes\nLast modified: {info.LastWriteTime}";
    }
    else {
      return $"Diagnostic log file not found at:\n{logFilePath_}";
    }
  }

  /// <summary>
  /// Simple file logger provider for writing to a single file
  /// </summary>
  private class FileLoggerProvider : ILoggerProvider {
    private readonly string filePath_;
    private readonly object writeLock_ = new();

    public FileLoggerProvider(string filePath) {
      filePath_ = filePath;
    }

    public ILogger CreateLogger(string categoryName) {
      return new FileLogger(filePath_, categoryName, writeLock_);
    }

    public void Dispose() {
    }

    private class FileLogger : ILogger {
      private readonly string filePath_;
      private readonly string categoryName_;
      private readonly object writeLock_;

      public FileLogger(string filePath, string categoryName, object writeLock) {
        filePath_ = filePath;
        categoryName_ = categoryName;
        writeLock_ = writeLock;
      }

      public IDisposable BeginScope<TState>(TState state) => null;
      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, 
                              Func<TState, Exception, string> formatter) {
        try {
          string message = formatter(state, exception);
          string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] [{categoryName_}] {message}";

          if (exception != null) {
            logLine += $"\n{exception}";
          }

          lock (writeLock_) {
            File.AppendAllText(filePath_, logLine + Environment.NewLine);
          }
        }
        catch {
          // Ignore file write errors
        }
      }
    }
  }
}