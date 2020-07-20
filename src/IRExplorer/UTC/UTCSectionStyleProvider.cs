// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using IRExplorerCore;

namespace IRExplorer.UTC {
    public class UTCSectionStyleProvider : ISectionStyleProvider {
        private List<MarkedSectionName> sectionNameMarkers_;

        public UTCSectionStyleProvider() {
            sectionNameMarkers_ = new List<MarkedSectionName>();
            LoadSettings();
        }

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

        public string SettingsFilePath => App.GetSectionsDefinitionFilePath("utc");

        public bool LoadSettings() {
            var serializer = new SectionStyleProviderSerializer();
            var settingsPath = App.GetSectionsDefinitionFilePath("utc");

            if (settingsPath == null) {
                return false;
            }

            if (!serializer.Load(settingsPath, out sectionNameMarkers_)) {
                Trace.TraceError("Failed to load UTCSectionStyleProvider data");
                return false;
            }

            return true;
        }

        public bool SaveSettings() {
            return false;
        }
    }
}
