// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Windows;
using System.Windows.Data;
using ProfileExplorerCore.Profile.Data;

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
}