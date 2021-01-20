// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

        [DisplayName("Searched Text"), Display(Order=1)]
        public string SearchedText { get; set; }
        [DisplayName("Searched Kind"), Display(Order = 2)]
        public TextSearchKind SearchKind { get; set; }
        [DisplayName("Text Color"), Display(Order = 3)]
        public Color TextColor { get; set; }
        [DisplayName("Separator Color"), Display(Order = 4)]
        public Color SeparatorColor { get; set; }
        [DisplayName("Separator weight before"), Display(Order = 5)]
        public int BeforeSeparatorWeight { get; set; }
        [DisplayName("Separator weight after"), Display(Order = 6)]
        public int AfterSeparatorWeight { get; set; }
        [DisplayName("Indentation level"), Display(Order = 7)]
        public int IndentationLevel { get; set; }

        public override string ToString() {
            if (!string.IsNullOrEmpty(SearchedText)) {
                return SearchedText;
            }

            return "<untitled>";
        }
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
