// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using IRExplorerCore;

namespace IRExplorerUI {
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

        [DisplayName("Searched Text")]
        public string SearchedText { get; set; }
        [DisplayName("Searched Kind")]
        [Editor(typeof(EnumCheckComboBoxEditor), typeof(EnumCheckComboBoxEditor))]
        public TextSearchKind SearchKind { get; set; }
        [DisplayName("Text Color")]
        public Color TextColor { get; set; }
        [DisplayName("Separator Color")]
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
