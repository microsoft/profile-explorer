// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ProfileExplorer.Core.Session;
using ProfileExplorer.Core.Profile.Spgo;

namespace ProfileExplorer.UI.Windows;

public partial class AttachSpgoProfileWindow : Window {
  public const string ProfileFileFilter =
    "SPT profiles (*.spt)|*.spt|All Files (*.*)|*.*";
  private CancellationTokenSource validation_;
  private PreparedSptProfile pendingProfile_;
  private bool loaded_;
  private bool updatingFields_;

  public AttachSpgoProfileWindow(IReadOnlyList<ILoadedDocument> targets,
                                  ILoadedDocument selectedTarget) {
    InitializeComponent();
    DataContext = this;
    TargetBinaryBox.ItemsSource = targets;
    TargetBinaryBox.SelectedItem = selectedTarget;
  }

  public double WindowScaling => App.Settings.GeneralSettings.WindowScaling;
  public PreparedSptProfile PreparedProfile { get; private set; }
  public Guid AttachedTargetId { get; private set; }

  private async void TargetBinaryBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    var target = TargetBinaryBox.SelectedItem as ILoadedDocument;
    updatingFields_ = true;
    PdbPathBox.Text = target?.DebugInfoFileExists == true ? target.DebugInfoFile.FilePath : "";
    TargetBinaryStatus.Text = target == null
      ? "Select a target binary."
      : target.BinaryFileExists
        ? ""
        : "The binary file has not been loaded locally. No download will be attempted.";
    updatingFields_ = false;
    await ValidateAsync();
  }

  private async void ProfileBrowseButton_Click(object sender, RoutedEventArgs e) {
    if (Utils.ShowOpenFileDialog(ProfilePathBox, ProfileFileFilter, "*.*", "Select SPGO profile")) {
      await ValidateAsync();
    }
  }

  private void PdbBrowseButton_Click(object sender, RoutedEventArgs e) {
    Utils.ShowOpenFileDialog(PdbPathBox, "Debug Files (*.pdb)|*.pdb|All Files (*.*)|*.*",
                             "*.pdb", "Select PDB");
  }

  private async void Window_Loaded(object sender, RoutedEventArgs e) {
    loaded_ = true;
    ProfileBrowseButton.Focus();
    await ValidateAsync();
  }

  private void Window_Closed(object sender, EventArgs e) {
    loaded_ = false;
    validation_?.Cancel();
    pendingProfile_ = null;
  }

  private async void PdbPathBox_TextChanged(object sender, TextChangedEventArgs e) {
    if (!updatingFields_) {
      await ValidateAsync(debounce: true);
    }
  }

  private async void ToolBrowseButton_Click(object sender, RoutedEventArgs e) {
    string path = Utils.ShowOpenFileDialog("SPTDump executable|sptdump.exe|Executables|*.exe",
                                           "*.exe", "Select SPTDump");
    if (path != null) {
      App.Settings.SptDumpToolPath = path;
      App.SaveApplicationSettings();
      await ValidateAsync();
    }
  }

  private async void AutoDetectButton_Click(object sender, RoutedEventArgs e) {
    App.Settings.SptDumpToolPath = "";
    App.SaveApplicationSettings();
    await ValidateAsync();
  }

  private async void RetryButton_Click(object sender, RoutedEventArgs e) {
    await ValidateAsync();
  }

  private async Task ValidateAsync(bool debounce = false) {
    if (!loaded_) {
      return;
    }
    validation_?.Cancel();
    using var cancellation = new CancellationTokenSource();
    validation_ = cancellation;
    var token = cancellation.Token;
    pendingProfile_ = null;
    AttachButton.IsEnabled = false;
    ValidationSummary.Text = "";
    ValidationDetails.Clear();
    ValidationDetailsExpander.IsExpanded = false;
    ValidationDetailsExpander.Visibility = Visibility.Collapsed;
    ValidationProgress.Visibility = Visibility.Collapsed;

    string profilePath = ProfilePathBox.Text;
    bool isSpt = string.Equals(Path.GetExtension(profilePath), ".spt", StringComparison.OrdinalIgnoreCase);
    SymbolsToolsExpander.Visibility = isSpt ? Visibility.Visible : Visibility.Collapsed;
    RetryButton.Visibility = isSpt ? Visibility.Visible : Visibility.Collapsed;
    string toolPath = null;

    try {
      if (string.IsNullOrEmpty(profilePath)) {
        ValidationStatus.Text = "Select a profile to begin.";
        return;
      }
      if (!isSpt) {
        ValidationStatus.Text = "This profile format is not implemented yet. Select an SPT file.";
        return;
      }
      if (debounce) {
        ValidationStatus.Text = "Validation pending...";
        await Task.Delay(300, token);
      }
      ValidationProgress.Visibility = Visibility.Visible;
      ValidationStatus.Text = "Locating SPTDump...";
      ToolPathBox.Text = string.IsNullOrEmpty(App.Settings.SptDumpToolPath)
        ? "Searching PATH and MSVC installations..."
        : App.Settings.SptDumpToolPath;
      string configuredPath = App.Settings.SptDumpToolPath;
      toolPath = await Task.Run(() => SptDumpTool.Find(configuredPath, token), token);
      token.ThrowIfCancellationRequested();
      ToolPathBox.Text = toolPath;

      if (TargetBinaryBox.SelectedItem is not ILoadedDocument target) {
        ValidationStatus.Text = "Select a target binary to validate the SPT.";
        return;
      }
      if (!target.BinaryFileExists || string.IsNullOrWhiteSpace(target.BinaryFile.FilePath)) {
        throw new FileNotFoundException("The selected binary must already be available in Profile Explorer. " +
                                        "Load it through the normal binary/trace workflow, then retry. " +
                                        "This dialog does not locate or download binaries.");
      }
      string binaryPath = target.BinaryFile.FilePath;
      string pdbPath = PdbPathBox.Text.Trim();
      var progress = new Progress<string>(message => {
        if (loaded_ && validation_ == cancellation && pendingProfile_ == null && !token.IsCancellationRequested) {
          ValidationStatus.Text = message;
        }
      });
      var result = await Task.Run(() => SptProfileLoader.LoadAsync(profilePath, binaryPath, pdbPath, toolPath,
                                                                   progress, token), token);
      token.ThrowIfCancellationRequested();
      pendingProfile_ = result;
      ValidationStatus.Text = "Ready: profile parsed and compatible with the selected binary.";
      ValidationSummary.Text = Summarize(result);
      ValidationDetails.Text = Describe(result, !string.IsNullOrEmpty(pdbPath));
      ValidationDetailsExpander.Visibility = Visibility.Visible;
      AttachButton.IsEnabled = true;
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) {
      // A newer selection or closing the dialog supersedes this work.
    }
    catch (Exception ex) when (IsInputError(ex)) {
      if (loaded_ && !token.IsCancellationRequested) {
        Trace.TraceError($"SPGO profile validation failed: {ex}");
        ValidationStatus.Text = "Cannot attach: validation failed.";
        ValidationSummary.Text = ex.Message.Split('\n', 2)[0].TrimEnd('\r');
        ValidationDetails.Text = ex.Message;
        ValidationDetailsExpander.Visibility = Visibility.Visible;
        if (toolPath == null || ex is SptPdbValidationException or Win32Exception ||
            ex is FileNotFoundException missingTool &&
            string.Equals(missingTool.FileName, toolPath, StringComparison.OrdinalIgnoreCase)) {
          SymbolsToolsExpander.IsExpanded = true;
          if (toolPath == null && string.IsNullOrEmpty(App.Settings.SptDumpToolPath)) {
            ToolPathBox.Text = "Not found. Use Change to select SPTDump.exe.";
          }
        }
      }
    }
    finally {
      if (validation_ == cancellation) {
        validation_ = null;
        ValidationProgress.Visibility = Visibility.Collapsed;
      }
    }
  }

  private async void AttachButton_Click(object sender, RoutedEventArgs e) {
    if (pendingProfile_ == null || TargetBinaryBox.SelectedItem is not ILoadedDocument target) {
      Trace.TraceWarning("SPGO attachment requested before validation completed.");
      return;
    }
    var result = pendingProfile_;
    using var cancellation = new CancellationTokenSource();
    validation_ = cancellation;
    InputsPanel.IsEnabled = false;
    AttachButton.IsEnabled = false;
    ValidationStatus.Text = "Checking that validated inputs are unchanged...";
    ValidationProgress.Visibility = Visibility.Visible;
    try {
      await result.VerifyUnchangedAsync(cancellation.Token);
      cancellation.Token.ThrowIfCancellationRequested();
      PreparedProfile = result;
      AttachedTargetId = target.Id;
      DialogResult = true;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
      // The user closed the dialog while files were being checked.
    }
    catch (Exception ex) when (IsInputError(ex)) {
      if (loaded_ && !cancellation.IsCancellationRequested) {
        Trace.TraceError($"SPGO attachment input check failed: {ex}");
        pendingProfile_ = null;
        ValidationStatus.Text = "Inputs could not be verified. Retry validation.";
        ValidationSummary.Text = ex.Message.Split('\n', 2)[0].TrimEnd('\r');
        ValidationDetails.Text = ex.Message;
        ValidationDetailsExpander.Visibility = Visibility.Visible;
      }
    }
    finally {
      if (validation_ == cancellation) {
        validation_ = null;
      }
      InputsPanel.IsEnabled = true;
      ValidationProgress.Visibility = Visibility.Collapsed;
    }
  }

  private static bool IsInputError(Exception ex) =>
    ex is InvalidDataException or IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException or
      ArgumentException or NotSupportedException or COMException;

  private static string Summarize(PreparedSptProfile result) {
    var records = result.Profile.Segments
      .Where(segment => result.MatchingProgramIndices.Contains(segment.ProgramIndex))
      .SelectMany(segment => segment.Records).ToList();
    decimal weight = records.Sum(record => (decimal)record.Weight * record.EntryCount);
    return $"{records.Count:N0} records; {weight:N0} weighted entries for the selected binary.";
  }

  private static string Describe(PreparedSptProfile result, bool hasPdb) {
    var text = new StringBuilder();
    text.AppendLine($"Binary GUID: {result.BinaryId:B}, age: {result.BinaryAge}");
    text.AppendLine($"Matching SPT program IDs: {string.Join(", ", result.MatchingProgramIndices)}");
    text.AppendLine(hasPdb ? "PDB identity: matches the binary." : "PDB: not supplied (not required for SPT identity).");
    text.AppendLine($"SPT: {result.Profile.Programs.Count:N0} programs, {result.Profile.RecordCount:N0} records.");
    foreach (var group in result.Profile.Segments
      .Where(segment => result.MatchingProgramIndices.Contains(segment.ProgramIndex))
      .SelectMany(segment => segment.Records).GroupBy(record => record.Kind)) {
      ulong weight = 0;
      foreach (var record in group) {
        weight = checked(weight + record.Weight * (ulong)record.EntryCount);
      }
      text.AppendLine($"{group.Key}: {group.Count():N0} records, {weight:N0} weighted entries.");
    }
    text.Append($"SPTDump version: {result.ToolVersion}. No address-to-function mapping has been performed.");
    return text.ToString();
  }
}
