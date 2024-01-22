using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using IRExplorerUI.Profile;

namespace IRExplorerUI;

public class RecordingSession : BindableObject {
  private static readonly SortedList<double, Func<TimeSpan, string>> offsets =
    new SortedList<double, Func<TimeSpan, string>> {
      {0.75, x => $"{x.TotalSeconds:F0} seconds"},
      {1.5, x => "a minute"},
      {45, x => $"{x.TotalMinutes:F0} minutes"},
      {90, x => "an hour"},
      {1440, x => $"{x.TotalHours:F0} hours"},
      {2880, x => "a day"},
      {43200, x => $"{x.TotalDays:F0} days"},
      {86400, x => "a month"},
      {525600, x => $"{x.TotalDays / 30:F0} months"},
      {1051200, x => "a year"},
      {double.MaxValue, x => $"{x.TotalDays / 365:F0} years"}
    };
  private ProfileDataReport report_;
  private bool isLoadedFile_;
  private bool isNewSession_;
  private string title_;
  private string description_;

  public RecordingSession(ProfileDataReport report, bool isLoadedFile,
                          bool isNewSession = false,
                          string title = null, string description = null) {
    report_ = report;
    isLoadedFile_ = isLoadedFile;
    isNewSession_ = isNewSession;
    title_ = title;
    description_ = description;
  }

  public static RecordingSession FromCommandLineArgs() {
    string[] args = Environment.GetCommandLineArgs();

    if (args.Length >= 6 && args[1] == "--open-trace") {
      var report = new ProfileDataReport();
      report.TraceInfo = new ProfileTraceInfo(args[2]);

      if (!File.Exists(report.TraceInfo.TraceFilePath) ||
          Path.GetExtension(report.TraceInfo.TraceFilePath) != ".etl") {
        MessageBox.Show("Trace file does not exist.");
        return null;
      }

      report.SymbolSettings = App.Settings.SymbolSettings.WithSymbolPaths(args[3]);

      if (!int.TryParse(args[4], out int processId)) {
        MessageBox.Show("Process ID is not an integer.");
        return null;
      }

      report.Process = new ProfileProcess(processId, args[5]);
      return new RecordingSession(report, true);
    }

    return null;
  }

  public static RecordingSession FromFile(string filePath) {
    var report = new ProfileDataReport();
    report.TraceInfo = new ProfileTraceInfo(filePath);
    return new RecordingSession(report, true);
  }

  public ProfileDataReport Report => report_;

  public bool IsNewSession {
    get => isNewSession_;
    set => SetAndNotify(ref isNewSession_, value);
  }

  public string Title {
    get {
      if (!string.IsNullOrEmpty(title_)) {
        return title_;
      }

      if (isLoadedFile_) {
        return report_.TraceInfo.TraceFilePath;
      }

      if (report_.RecordingSessionOptions.HasTitle) {
        return report_.RecordingSessionOptions.Title;
      }

      if (report_.IsAttachToProcessSession) {
        return $"Attached to {report_.Process.Name}";
      }

      if (Report.IsStartProcessSession) {
        return Utils.TryGetFileName(report_.RecordingSessionOptions.ApplicationPath);
      }

      return null;
    }
    set => SetAndNotify(ref title_, value);
  }

  public string ToolTip {
    get {
      if (report_ == null) {
        return null;
      }

      if (isLoadedFile_) {
        return report_?.TraceInfo.TraceFilePath;
      }

      if (report_.IsStartProcessSession) {
        return
          $"{report_?.RecordingSessionOptions.ApplicationPath} {report_?.RecordingSessionOptions.ApplicationArguments}";
      }

      return null;
    }
  }

  public string Description {
    get {
      if (!string.IsNullOrEmpty(description_)) {
        return description_;
      }

      if (!IsNewSession) {
        if (isLoadedFile_) {
          return $"Process: {report_?.Process.ImageFileName}";
        }

        if (report_.IsAttachToProcessSession) {
          return $"Id: {report_.Process.ProcessId}";
        }

        if (report_.IsStartProcessSession) {
          return $"Args: {report_.RecordingSessionOptions.ApplicationArguments}";
        }
      }

      return null;
    }
    set => SetAndNotify(ref description_, value);
  }

  public bool ShowDescription {
    get {
      if (!string.IsNullOrEmpty(description_)) {
        return true;
      }

      if (!IsNewSession) {
        return !string.IsNullOrEmpty(isLoadedFile_ ? report_?.Process.ImageFileName
                                       : report_.RecordingSessionOptions.ApplicationArguments);
      }

      return false;
    }
  }

  public string Time => IsNewSession ? ""
    : $"{report_.TraceInfo.ProfileStartTime.ToShortDateString()}, {ToRelativeDate(report_.TraceInfo.ProfileStartTime)}";

  public static string ToRelativeDate(DateTime input) {
    var x = DateTime.Now - input;
    x = new TimeSpan(Math.Abs(x.Ticks));
    return offsets.First(n => x.TotalMinutes < n.Key).Value(x) + " ago";
  }
}
