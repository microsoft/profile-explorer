// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using IRExplorerCore.IR;

namespace IRExplorerCore {
    public class IRTextFunction {
        public List<IRTextSection> Sections { get; }
        public int Number { get; set; }
        public string Name { get; }
        public IRTextSummary ParentSummary { get; set; }
        private int hashCode_;

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

        public int SectionCount => Sections?.Count ?? 0;
        public bool HasSections => SectionCount != 0;

        public void AddSection(IRTextSection section) {
            section.Number = Sections.Count + 1;
            Sections.Add(section);
        }

        public IRTextSection FindSection(string name) {
            return Sections.Find(item => item.Name == name);
        }

        public List<IRTextSection> FindAllSections(string nameSubstring) {
            return Sections.FindAll((section) => section.Name.Contains(nameSubstring));
        }

        public override bool Equals(object obj) {
            return Equals(obj, true);
        }

        public bool Equals(object obj, bool checkParent) {
            return obj is IRTextFunction function &&
                   Name == function.Name &&
                   (!checkParent ||
                   (((ParentSummary != null && function.ParentSummary != null &&
                     ParentSummary.ModuleName == function.ParentSummary.ModuleName) ||
                    (ParentSummary == null && function.ParentSummary == null))));
        }

        public override int GetHashCode() {
            // Compute the hash so that functs. with same name in diff. modules
            // don't get the same hash code.
            if (hashCode_ == 0) {
                hashCode_ = ParentSummary != null ? HashCode.Combine(Name, ParentSummary) : HashCode.Combine(Name);
            }

            return hashCode_;
        }

        public override string ToString() {
            return Name;
        }
    }
}