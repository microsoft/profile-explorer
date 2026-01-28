// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using ProfileExplorer.Core.Profile.Data;
using ProfileExplorer.Core.Utilities;

namespace ProfileExplorer.UI.Profile;

public partial class ProfileReportPanel : ToolPanelControl {
  private ProfileDataReport report_;
  private IUISession session_;

  public ProfileReportPanel(IUISession session) {
    InitializeComponent();
    session_ = session;
  }

  public static void ShowReportWindow(ProfileDataReport report, IUISession session,
                                      ProfileDataReport.ModuleStatus selectedModule = null) {
    var panel = new ProfileReportPanel(session);
    panel.TitleSuffix = "Profile report";

    var window = new Window();
    window.Content = panel;
    window.Title = "Profile report";
    window.WindowStyle = WindowStyle.ToolWindow;
    window.ResizeMode = ResizeMode.CanResizeWithGrip;
    window.Width = 800;
    window.Height = 650;
    window.Owner = Application.Current.MainWindow;
    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    panel.ShowReport(report, selectedModule);
    window.Show();
  }

  public void ShowReport(ProfileDataReport report, ProfileDataReport.ModuleStatus selectedModule) {
    report_ = report;
    DataContext = report;

    var modules = report.Modules;
    modules.Sort((a, b) => a.ImageFileInfo.ImageName.CompareTo(b.ImageFileInfo.ImageName));
    ModuleList.ItemsSource = new ListCollectionView(modules);

    if (report.RunningProcesses != null) {
      // Process list is already sorted by weight.
      ProcessList.ItemsSource = new ListCollectionView(report.RunningProcesses);
    }

    if (selectedModule != null) {
      ModuleList.SelectedItem = selectedModule;
      ModuleList.ScrollIntoView(selectedModule);
      TabHost.SelectedIndex = 1;
    }
  }

  private async void LoadAllBinariesButton_Click(object sender, RoutedEventArgs e) {
    // Find all modules with symbols loaded but binary pending lazy load
    var modulesToLoad = report_.Modules
      .Where(m => m.State == ModuleLoadState.LazyLoadPending && m.HasDebugInfoLoaded)
      .ToList();

    if (modulesToLoad.Count == 0) {
      MessageBox.Show("No modules with pending binary loads found.\n\nBinaries are loaded on-demand when you view disassembly for a function.",
                      "Load All Binaries", MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    var result = MessageBox.Show($"Load binaries for {modulesToLoad.Count} modules that have symbols?\n\nThis may take some time if binaries need to be downloaded from symbol servers.",
                                  "Load All Binaries", MessageBoxButton.YesNo, MessageBoxImage.Question);
    if (result != MessageBoxResult.Yes) {
      return;
    }

    LoadAllBinariesButton.IsEnabled = false;
    LoadAllBinariesButton.Content = "Loading...";

    int loaded = 0;
    int failed = 0;

    // Build a set of module names to load
    var moduleNames = new HashSet<string>(modulesToLoad.Select(m => m.ImageFileInfo.ImageName),
                                          System.StringComparer.OrdinalIgnoreCase);

    await Task.Run(async () => {
      // Find documents by module name and trigger lazy load
      var documents = session_.SessionState?.Documents;
      if (documents == null) return;

      foreach (var doc in documents) {
        try {
          string moduleName = doc.ModuleName;
          if (moduleName != null && moduleNames.Contains(moduleName) && doc.EnsureBinaryLoaded != null) {
            DiagnosticLogger.LogInfo($"[LoadAllBinaries] Loading binary for {moduleName}");
            bool success = await doc.EnsureBinaryLoaded().ConfigureAwait(false);
            if (success) {
              loaded++;
            } else {
              failed++;
            }
          }
        }
        catch {
          failed++;
        }
      }
    });

    LoadAllBinariesButton.IsEnabled = true;
    LoadAllBinariesButton.Content = "Load All Binaries";

    // Refresh the module list
    var modules = report_.Modules;
    modules.Sort((a, b) => a.ImageFileInfo.ImageName.CompareTo(b.ImageFileInfo.ImageName));
    ModuleList.ItemsSource = new ListCollectionView(modules);

    MessageBox.Show($"Loaded {loaded} binaries, {failed} failed.",
                    "Load All Binaries", MessageBoxButton.OK, MessageBoxImage.Information);
  }
}