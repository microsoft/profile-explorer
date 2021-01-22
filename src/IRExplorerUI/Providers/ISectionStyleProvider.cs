// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using IRExplorerCore;

namespace IRExplorerUI {
    public interface ISectionStyleProvider {
        string SettingsFilePath { get; }
        List<MarkedSectionName> SectionNameMarkers { get; set; }
        bool SaveSettings();
        bool LoadSettings();
        bool IsMarkedSection(IRTextSection section, out MarkedSectionName result);
    }

    class SectionStyleProviderSerializer {
        public bool Save(List<MarkedSectionName> sectionNameMarkers, string path) {
            var data = new SerializedData {
                List = sectionNameMarkers
            };

            return JsonUtils.SerializeToFile(data, path);
        }

        public bool Load(string path, out List<MarkedSectionName> sectionNameMarkers) {
            if (JsonUtils.DeserializeFromFile(path, out SerializedData data)) {
                sectionNameMarkers = data.List;
                return true;
            }

            sectionNameMarkers = new List<MarkedSectionName>();
            return false;
        }

        private class SerializedData {
            public string Comment => "Description";

            public string[] SearchKindValues =>
                new[] {
                    "default", "regex", "caseSensitive", "wholeWord"
                };

            public List<MarkedSectionName> List { get; set; }
        }
    }
}
