// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using IRExplorerCore;

namespace IRExplorer {
    public class SectionSearchResult {
        public SectionSearchResult(IRTextSection section, string sectionText,
                                   List<TextSearchResult> results = null) {
            Section = section;
            SectionText = sectionText;
            Results = results;
        }

        public SectionSearchResult(IRTextSection section) {
            Section = section;
            Results = new List<TextSearchResult>();
        }

        public IRTextSection Section { get; set; }
        public string SectionText { get; set; }
        public List<TextSearchResult> Results { get; set; }
    }

    public class SectionTextSearcher {
        private SectionLoader sectionLoader_;

        public SectionTextSearcher(SectionLoader sectionLoader) {
            sectionLoader_ = sectionLoader;
        }

        public async Task<List<SectionSearchResult>> SearchAsync(string searchedText,
                                                                 TextSearchKind searchKind,
                                                                 List<IRTextSection> sections) {
            var resultList = new List<SectionSearchResult>(sections.Count);

            if (string.IsNullOrEmpty(searchedText)) {
                return resultList;
            }

            var tasks = new Task<SectionSearchResult>[sections.Count];

            for (int i = 0; i < sections.Count; i++) {
                var section = sections[i];
                tasks[i] = Task.Run(() => SearchSection(searchedText, searchKind, section));
            }

            await Task.WhenAll(tasks);

            foreach (var task in tasks) {
                resultList.Add(task.Result);
            }

            return resultList;
        }

        public async Task<SectionSearchResult> SearchSectionAsync(
            string searchedText, TextSearchKind searchKind, IRTextSection section) {
            var input = new List<IRTextSection> {section};
            var resultList = await SearchAsync(searchedText, searchKind, input);

            if (resultList.Count > 0) {
                return resultList[0];
            }

            return new SectionSearchResult(section);
        }

        public SectionSearchResult SearchSection(string searchedText, TextSearchKind searchKind,
                                                 IRTextSection section) {
            string text = sectionLoader_.LoadSectionText(section);
            return SearchSection(text, searchedText, searchKind, section);
        }

        public SectionSearchResult SearchSection(string text, string searchedText, TextSearchKind searchKind,
                                                 IRTextSection section) {
            return new SectionSearchResult(section, text) {
                Results = TextSearcher.AllIndexesOf(text, searchedText, 0, searchKind)
            };
        }

        public async Task<SectionSearchResult> SearchSectionWithTextAsync(string text, string searchedText,
                                                                          TextSearchKind searchKind,
                                                                          IRTextSection section) {
            var result = await Task.Run(() => TextSearcher.AllIndexesOf(text, searchedText, 0, searchKind));
            return new SectionSearchResult(section, text, result);
        }
    }
}
