// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ProfileExplorerCore2.Profile.Data;
using ProfileExplorerCore2.Profile.ETW;
using ProfileExplorerCore2.Session;
using ProfileExplorerCore2.Settings;
using ProfileExplorerCore2.Utilities;

namespace ProfileExplorerCore2Tests;

public class ProfileLoadWindow {
    private CancelableTaskInstance loadTask_;
    private ProfileDataProviderOptions options_;
    private ProfileRecordingSessionOptions recordingOptions_;
    private SymbolFileSourceSettings symbolSettings_;
    private RawProfileData recordedProfile_;
    private List<ProcessSummary> processList_;
    private List<ProcessSummary> selectedProcSummary_;
    private string profileFilePath_;
    private string binaryFilePath_;
    private bool showOnlyManagedProcesses_;

    public ProfileLoadWindow(ISession session) {
        Session = session;
        loadTask_ = new CancelableTaskInstance();
        Options = CoreSettingsProvider.ProfileOptions;
        SymbolSettings = CoreSettingsProvider.SymbolSettings;
        RecordingOptions = new ProfileRecordingSessionOptions();
    }

    public ISession Session { get; set; }
    public string ProfileFilePath {
        get => profileFilePath_;
        set => profileFilePath_ = value;
    }
    public string BinaryFilePath {
        get => binaryFilePath_;
        set => binaryFilePath_ = value;
    }
    public ProfileRecordingSessionOptions RecordingOptions {
        get => recordingOptions_;
        set => recordingOptions_ = value;
    }
    public ProfileDataProviderOptions Options {
        get => options_;
        set => options_ = value;
    }
    public SymbolFileSourceSettings SymbolSettings {
        get => symbolSettings_;
        set => symbolSettings_ = value;
    }
    public bool ShowOnlyManagedProcesses {
        get => showOnlyManagedProcesses_;
        set => showOnlyManagedProcesses_ = value;
    }

    // Entry point: Load ETL file and get process list
    public async Task<List<ProcessSummary>> LoadProcessList(string etlFilePath) {
        await CancelLoadingTask();
        if (!File.Exists(etlFilePath)) return null;
        using var task = await loadTask_.CancelCurrentAndCreateTaskAsync();
        processList_ = await ETWProfileDataProvider.FindTraceProcesses(etlFilePath, options_, ProcessListProgressCallback, task);
      ProfileFilePath = etlFilePath;
      return processList_;
    }

  private void ProcessListProgressCallback(ProcessListProgress progressInfo) {
    Console.WriteLine("Callback");
  }

  // Entry point: Select process by processId
  public void SelectProcess(int processId) {
        if (processList_ == null) throw new InvalidOperationException("Process list not loaded");
        var proc = processList_.FirstOrDefault(p => p.Process.ProcessId == processId);
        if (proc == null) throw new ArgumentException($"Process {processId} not found in ETL");
        selectedProcSummary_ = new List<ProcessSummary> { proc };
        BinaryFilePath = proc.Process.Name;
    }

    // Entry point: Load profile trace for selected process
    public async Task<ProfileDataReport> LoadProfileTrace(SymbolFileSourceSettings symbolSettings = null) {
        if (selectedProcSummary_ == null || ProfileFilePath == null)
            throw new InvalidOperationException("No process selected or ETL file not set");
        symbolSettings ??= SymbolSettings;
        using var task = await loadTask_.CancelCurrentAndCreateTaskAsync();
        var report = new ProfileDataReport {
            RunningProcesses = processList_,
            SymbolSettings = symbolSettings.Clone()
        };
        var processIds = selectedProcSummary_.ConvertAll(proc => proc.Process.ProcessId);
        var success = await Session.LoadProfileData(ProfileFilePath, processIds, Options, symbolSettings, report, null, task);
        if (!success) throw new Exception($"Failed to load profile for process(es) {string.Join(",", processIds)}");
        return report;
    }

    private async Task CancelLoadingTask() {
        await loadTask_.CancelTaskAndWaitAsync();
    }
}