// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorer.Core.Providers;
using ProfileExplorer.Core.Settings;

namespace ProfileExplorer.Core.Utilities;

/// <summary>
/// Default implementation of ISettingsProvider that provides hardcoded default settings.
/// </summary>
public class DefaultSettingsProvider : ISettingsProvider
{
    private static readonly SymbolFileSourceSettings _symbolSettings = new SymbolFileSourceSettings();
    private static readonly ProfileDataProviderOptions _profileOptions = new ProfileDataProviderOptions();
    private static readonly SectionSettings _sectionSettings = new SectionSettings();
    private static readonly DiffSettings _diffSettings = new DiffSettings();
    private static readonly GeneralSettings _generalSettings = new GeneralSettings();

    public SymbolFileSourceSettings SymbolSettings => _symbolSettings.Clone();
    public ProfileDataProviderOptions ProfileOptions => _profileOptions;
    public SectionSettings SectionSettings => _sectionSettings;
    public DiffSettings DiffSettings => _diffSettings.Clone();
    public GeneralSettings GeneralSettings => _generalSettings;
}

public static class CoreSettingsProvider
{
    private static ISettingsProvider _provider = new DefaultSettingsProvider();

    /// <summary>
    /// Sets the settings provider to use. This allows the UI project to override default settings.
    /// </summary>
    /// <param name="provider">The settings provider to use, or null to reset to default.</param>
    public static void SetProvider(ISettingsProvider provider)
    {
        _provider = provider ?? new DefaultSettingsProvider();
    }

    public static SymbolFileSourceSettings SymbolSettings => _provider.SymbolSettings;
    public static ProfileDataProviderOptions ProfileOptions => _provider.ProfileOptions;
    public static SectionSettings SectionSettings => _provider.SectionSettings;
    public static DiffSettings DiffSettings => _provider.DiffSettings;
    public static GeneralSettings GeneralSettings => _provider.GeneralSettings;
}
