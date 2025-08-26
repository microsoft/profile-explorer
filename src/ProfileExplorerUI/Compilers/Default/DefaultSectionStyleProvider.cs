// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Diagnostics;
using ProfileExplorer.Core;
using ProfileExplorer.Core.Providers;

namespace ProfileExplorer.UI.Compilers.Default;

public sealed class DefaultSectionStyleProvider : ISectionStyleProvider {
  private List<MarkedSectionName> sectionNameMarkers_;
  private ICompilerInfoProvider compilerInfo_;

  public DefaultSectionStyleProvider(ICompilerInfoProvider compilerInfo) {
    compilerInfo_ = compilerInfo;
    sectionNameMarkers_ = new List<MarkedSectionName>();
    LoadSettings();
  }

  public string SettingsFilePath => App.GetSectionsDefinitionFilePath(compilerInfo_.CompilerIRName);

  public bool IsMarkedSection(IRTextSection section, out MarkedSectionName result) {
    foreach (var nameMarker in sectionNameMarkers_) {
      if (TextSearcher.Contains(section.Name, nameMarker.SearchedText, nameMarker.SearchKind)) {
        result = nameMarker;
        return true;
      }
    }

    result = null;
    return false;
  }

  public bool LoadSettings() {
    var serializer = new SectionStyleProviderSerializer();
    string settingsPath = App.GetSectionsDefinitionFilePath(compilerInfo_.CompilerIRName);

    if (settingsPath == null) {
      return false;
    }

    if (!serializer.Load(settingsPath, out sectionNameMarkers_)) {
      Trace.TraceError("Failed to load SectionStyleProvider data");
      return false;
    }

    return true;
  }

  public bool SaveSettings() {
    return false;
  }
}