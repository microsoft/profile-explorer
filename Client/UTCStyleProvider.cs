// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Windows.Media;
using Core;

namespace Client {
    public class UTCStyleProvider : IStyleProvider {
        private List<MarkedSectionName> sectionNameMarkers_;

        public UTCStyleProvider() {
            sectionNameMarkers_ = new List<MarkedSectionName>();

            sectionNameMarkers_.Add(new MarkedSectionName("Tuples before SSA Optimizer - second pass", TextSearchKind.Default) {
                TextColor = Brushes.Indigo
            });

            sectionNameMarkers_.Add(new MarkedSectionName("SSA Optimizer", TextSearchKind.Default) {
                TextColor = Brushes.MediumBlue
            });

            sectionNameMarkers_.Add(new MarkedSectionName("SSAOpt", TextSearchKind.Default) {
                TextColor = Brushes.MediumBlue
            });

            sectionNameMarkers_.Add(new MarkedSectionName("SSA CFG Optimizer", TextSearchKind.Default) {
                TextColor = Brushes.DarkGreen
            });

            sectionNameMarkers_.Add(new MarkedSectionName("Vectorizer", TextSearchKind.Default) {
                TextColor = Brushes.Indigo
            });

            sectionNameMarkers_.Add(new MarkedSectionName("const/copy prop", TextSearchKind.Default) { TextColor = Brushes.DarkRed });
            sectionNameMarkers_.Add(new MarkedSectionName("dead store", TextSearchKind.Default) { TextColor = Brushes.DarkRed });
            sectionNameMarkers_.Add(new MarkedSectionName("build hash table", TextSearchKind.Default) { TextColor = Brushes.DarkRed });
            sectionNameMarkers_.Add(new MarkedSectionName("common sub expressions", TextSearchKind.Default) { TextColor = Brushes.DarkRed });
            sectionNameMarkers_.Add(new MarkedSectionName("loop opts", TextSearchKind.Default) { TextColor = Brushes.DarkRed });
        }

        public bool IsMarkedSection(IRTextSection section, out MarkedSectionName result) {
            foreach (var nameMarker in sectionNameMarkers_) {
                if (TextSearcher.Contains(section.Name, nameMarker.Text, nameMarker.SearchKind)) {
                    result = nameMarker;
                    return true;
                }
            }

            result = null;
            return false;
        }
    }
}
