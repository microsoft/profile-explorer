// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using ProfileExplorerCore2.Providers;
using ProfileExplorerCore2.Settings;

namespace ProfileExplorerCore2.Utilities;

public static class CoreSettingsProvider
{
    // Hardcoded default settings for now
    private static readonly SymbolFileSourceSettings _symbolSettings = new SymbolFileSourceSettings();
    private static readonly ProfileDataProviderOptions _profileOptions = new ProfileDataProviderOptions();
    private static readonly SectionSettings _sectionSettings = new SectionSettings();
    private static readonly DiffSettings _diffSettings = new DiffSettings();
    private static readonly GeneralSettings _generalSettings = new GeneralSettings();

    public static SymbolFileSourceSettings SymbolSettings => _symbolSettings.Clone();
    public static ProfileDataProviderOptions ProfileOptions => _profileOptions;
    public static SectionSettings SectionSettings => _sectionSettings;
    public static DiffSettings DiffSettings => _diffSettings.Clone();
    public static GeneralSettings GeneralSettings => _generalSettings;
}

public class SectionSettings
{
    // Add more settings as needed
    public bool ShowDemangledNames { get; set; } = true;
    public FunctionNameDemanglingOptions DemanglingOptions { get; set; } = FunctionNameDemanglingOptions.Default;
}
