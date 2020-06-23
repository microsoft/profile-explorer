// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows.Media;
using CoreLib;

namespace Client {
    public class UTCSectionStyleProvider : ISectionStyleProvider {
        private static readonly string SettingsFile = "utc-section-styles.json";
        private List<MarkedSectionName> sectionNameMarkers_;

        public UTCSectionStyleProvider() {
            sectionNameMarkers_ = new List<MarkedSectionName>();

            sectionNameMarkers_.Add(
                new MarkedSectionName("Tuples before SSA Optimizer - second pass", TextSearchKind.Default) {
                    TextColor = Colors.Indigo
                });

            sectionNameMarkers_.Add(new MarkedSectionName("SSA Optimizer", TextSearchKind.Default) {
                TextColor = Colors.MediumBlue
            });

            sectionNameMarkers_.Add(new MarkedSectionName("SSAOpt", TextSearchKind.Default) {
                TextColor = Colors.MediumBlue
            });

            sectionNameMarkers_.Add(new MarkedSectionName("SSA CFG Optimizer", TextSearchKind.Default) {
                TextColor = Colors.DarkGreen
            });

            sectionNameMarkers_.Add(new MarkedSectionName("Vectorizer", TextSearchKind.Default) {
                TextColor = Colors.Indigo
            });

            sectionNameMarkers_.Add(new MarkedSectionName("const/copy prop", TextSearchKind.Default)
                                        {TextColor = Colors.DarkRed});

            sectionNameMarkers_.Add(new MarkedSectionName("dead store", TextSearchKind.Default)
                                        {TextColor = Colors.DarkRed});

            sectionNameMarkers_.Add(new MarkedSectionName("build hash table", TextSearchKind.Default)
                                        {TextColor = Colors.DarkRed});

            sectionNameMarkers_.Add(new MarkedSectionName("common sub expressions", TextSearchKind.Default)
                                        {TextColor = Colors.DarkRed});

            sectionNameMarkers_.Add(new MarkedSectionName("loop opts", TextSearchKind.Default)
                                        {TextColor = Colors.DarkRed});

            //new SectionStyleProviderSerializer().Save(sectionNameMarkers_, @"C:\test\sectionMarkers.json");
            //new SectionStyleProviderSerializer().Load(@"C:\test\sectionMarkers.json", out var result);
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

        public string SettingsFilePath => SettingsFile;

        public bool LoadSettings() {
            return false;
        }

        public bool SaveSettings() {
            return false;
        }
    }
}
