// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Providers;

namespace ProfileExplorer.Core.Settings;

/// <summary>
/// Interface for providing access to application settings.
/// This allows the UI project to override default settings used by the Core project.
/// </summary>
public interface ISettingsProvider {
  SymbolFileSourceSettings SymbolSettings { get; }
  ProfileDataProviderOptions ProfileOptions { get; }
  SectionSettings SectionSettings { get; }
  DiffSettings DiffSettings { get; }
  GeneralSettings GeneralSettings { get; }
}
