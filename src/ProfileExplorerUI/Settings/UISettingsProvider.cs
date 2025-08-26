// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.UI.Settings;

/// <summary>
/// Settings provider that delegates to the UI application's settings.
/// </summary>
public class UISettingsProvider : ISettingsProvider
{
    public SymbolFileSourceSettings SymbolSettings => App.Settings.SymbolSettings;
    public ProfileDataProviderOptions ProfileOptions => App.Settings.ProfileOptions;
    ProfileExplorer.Core.Settings.SectionSettings ISettingsProvider.SectionSettings => App.Settings.SectionSettings;
    public DiffSettings DiffSettings => App.Settings.DiffSettings;
    ProfileExplorer.Core.Settings.GeneralSettings ISettingsProvider.GeneralSettings => 
        App.Settings.GeneralSettings as ProfileExplorer.Core.Settings.GeneralSettings ?? 
        new ProfileExplorer.Core.Settings.GeneralSettings();
}
