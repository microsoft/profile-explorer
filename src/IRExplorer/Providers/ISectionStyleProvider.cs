// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows.Media;
using IRExplorerCore;

namespace IRExplorer {
    public class MarkedSectionName {
        public MarkedSectionName() { }

        public MarkedSectionName(string text, TextSearchKind searchKind) {
            SearchedText = text;
            SearchKind = searchKind;
            TextColor = Colors.Transparent;
        }

        public MarkedSectionName(string text, TextSearchKind searchKind, Color textColor) {
            SearchedText = text;
            SearchKind = searchKind;
            TextColor = textColor;
        }

        public string SearchedText { get; set; }
        public TextSearchKind SearchKind { get; set; }
        public Color TextColor { get; set; }
        public Color SeparatorColor { get; set; }
        public int BeforeSeparatorWeight { get; set; }
        public int AfterSeparatorWeight { get; set; }
        public int IndentationLevel { get; set; }
    }

    public interface ISectionStyleProvider {
        string SettingsFilePath { get; }
        bool SaveSettings();
        bool LoadSettings();
        bool IsMarkedSection(IRTextSection section, out MarkedSectionName result);
    }

    class SectionStyleProviderSerializer {
        public bool Save(List<MarkedSectionName> sectionNameMarkers, string path) {
            var data = new SerializedData {
                List = sectionNameMarkers
            };

            return JsonUtils.Serialize(data, path);
        }

        public bool Load(string path, out List<MarkedSectionName> sectionNameMarkers) {
            if (JsonUtils.Deserialize(path, out SerializedData data)) {
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
