// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using ProfileExplorer.UI.Controls;
using ProfileExplorer.UI.Windows;

namespace ProfileExplorer.UI.Services;

public class SettingsDirectoryLauncher : ISettingsDirectoryLauncher {
  public void LaunchCompilerSettingsDirectory() {
    string path = App.GetCompilerSettingsDirectoryPath(App.Session.CompilerInfo.CompilerIRName);
    App.OpenSettingsFolder(path);
  }
}