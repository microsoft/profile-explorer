// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;

namespace ProfileExplorer.UI.Services;

public interface ISettingsFileLauncher {
  void LaunchSettingsFile(string path);
}