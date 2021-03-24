// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace IRExplorerCore {
    public class IRTextFunction {
        public List<IRTextSection> Sections { get; }
        public int Number { get; set; }
        public string Name { get; }
        public IRTextSummary ParentSummary { get; set; }
        public int SectionCount => Sections?.Count ?? 0;

        public IRTextFunction(string name) {
            Name = name;
            Sections = new List<IRTextSection>();
        }

        public int MaxBlockCount {
            get {
                IRTextSection maxSection = null;

                foreach (var section in Sections) {
                    if (maxSection == null) {
                        maxSection = section;
                    }
                    else if (section.BlockCount > maxSection.BlockCount) {
                        maxSection = section;
                    }
                }

                return maxSection?.BlockCount ?? 0;
            }
        }

        public IRTextSection FindSection(string name) {
            return Sections.Find(item => item.Name == name);
        }

        public List<IRTextSection> FindAllSections(string nameSubstring) {
            return Sections.FindAll((section) => section.Name.Contains(nameSubstring));
        }
    }
}